using System.Diagnostics;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;

namespace IRSpeedy.Hotspot;

internal sealed class Request
{
    public string Command { get; set; } = "";
    public Guid TunId { get; set; }
    public string Ssid { get; set; } = "";
    public string Password { get; set; } = "";
    public int CorePid { get; set; }
    public bool Experimental { get; set; }
}

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // The mutex stays on this OS thread; async continuations must not release it.
    [MTAThread]
    private static int Main()
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        { Emit(new { ok = false, code = "windows-10-2004-or-later-required" }); return 1; }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { Emit(new { ok = false, code = "elevation-required" }); return 1; }
        try
        {
            using var gate = new Mutex(false, @"Global\IRSpeedyHotspotPoC.v1");
            bool acquired;
            try { acquired = gate.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new HotspotException("helper-already-running");
            try { return Run().GetAwaiter().GetResult(); }
            finally { gate.ReleaseMutex(); }
        }
        catch (Exception ex) { EmitError(ex); return 1; }
    }

    private static async Task<int> Run()
    {
        var backend = new WindowsBackend();
        var journal = new JournalStore();
        var session = new Session(backend, journal);
        Process? core = null;
        DateTime coreStarted = default;
        var lease = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        int exitCode = 0;
        try
        {
            Emit(new { ok = true, state = "ready", experimental = true, recoveryRequired = journal.Read() is not null });
            var pending = Task.Run(() => ReadBoundedLine(cancellation.Token));
            while (!cancellation.IsCancellationRequested)
            {
                await Task.WhenAny(pending, Task.Delay(500, cancellation.Token));
                if (session.Active)
                {
                    if (lease.Elapsed > TimeSpan.FromSeconds(10) || core is null || core.HasExited ||
                        core.StartTime.ToUniversalTime() != coreStarted || !session.Healthy())
                    {
                        Emit(new { ok = false, code = "session-health-or-lease-lost" });
                        await session.Stop();
                        exitCode = 2;
                        break;
                    }
                }
                if (!pending.IsCompleted) continue;
                string? line = await pending;
                if (line is null) break; // parent pipe closed: tear down in finally
                try
                {
                    var request = JsonSerializer.Deserialize<Request>(line, JsonOptions) ??
                        throw new HotspotException("invalid-request");
                    switch (request.Command)
                    {
                        case "capability":
                            Emit(new { ok = true, command = request.Command, adapters = backend.ReadAdapters(),
                                recoveryRequired = journal.Read() is not null, requiresTunProfile = false,
                                startupMode = "default-profile-then-tun-v2",
                                leakProtectionVerified = false });
                            break;
                        case "start":
                            if (session.Active || session.State is not null || journal.Read() is not null)
                                throw new HotspotException("recovery-required");
                            core?.Dispose();
                            core = Process.GetProcessById(request.CorePid);
                            if (core.HasExited) throw new HotspotException("core-not-running");
                            coreStarted = core.StartTime.ToUniversalTime();
                            // Never start a physical upstream with credentials already known to clients.
                            // This secret is returned only after exact TUN/private ICS verification.
                            var accessPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                            var accessSsid = "IRSpeedy-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
                            try
                            {
                                await session.Start(request.TunId, accessSsid, accessPassword, request.Experimental);
                                if (core.HasExited || core.StartTime.ToUniversalTime() != coreStarted || !session.Healthy())
                                {
                                    await session.Stop();
                                    throw new HotspotException("core-or-tun-lost-during-start");
                                }
                            }
                            finally { request.Password = ""; }
                            lease.Restart();
                            Emit(new { ok = true, state = "active", experimental = true,
                                startupMode = "default-profile-then-tun-v2", ssid = accessSsid, password = accessPassword,
                                observations = session.Observations });
                            break;
                        case "heartbeat":
                            lease.Restart();
                            Emit(new { ok = true, active = session.Active });
                            break;
                        case "status":
                        case "clients":
                            Emit(new { ok = true, active = session.Active,
                                clientCount = session.Active ? backend.ClientCount : 0,
                                recoveryRequired = journal.Read() is not null && !session.Active });
                            break;
                        case "stop":
                        case "recover":
                            await session.Stop();
                            Emit(new { ok = true, state = "stopped" });
                            break;
                        default: throw new HotspotException("unknown-command");
                    }
                }
                catch (Exception ex)
                {
                    EmitError(ex);
                    // A failed rollback retains ownership; don't accept more starts.
                    if (!session.Active && session.State is not null) { exitCode = 2; break; }
                }
                pending = Task.Run(() => ReadBoundedLine(cancellation.Token));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { EmitError(ex); exitCode = 2; }
        finally
        {
            // Only clean up this process's session, not an old journal on a read-only
            // capability invocation. Explicit recover is required after a hard crash.
            if (session.State is not null)
            {
                try { await session.Stop(); }
                catch (Exception ex) { EmitError(ex); exitCode = 2; }
            }
            core?.Dispose();
            Console.CancelKeyPress -= cancel;
        }
        return exitCode;
    }

    private static async Task<string?> ReadBoundedLine(CancellationToken token)
    {
        var buffer = new char[1];
        var text = new System.Text.StringBuilder();
        while (await Console.In.ReadAsync(buffer.AsMemory(), token) != 0)
        {
            if (buffer[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= 4096) throw new HotspotException("request-too-large");
            text.Append(buffer[0]);
        }
        if (text.Length != 0) throw new HotspotException("truncated-request");
        return null;
    }

    private static void Emit(object value)
    {
        // A broken parent pipe must not prevent cleanup.
        try { Console.Out.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
        catch (IOException) { }
    }
    private static void EmitError(Exception ex) => Emit(new
    {
        ok = false,
        code = ex is HotspotException h ? h.Code : "operation-failed",
        exceptionType = ex.GetType().Name,
        hresult = ex.HResult.ToString("X8"),
        stage = ex.Data["hotspot.stage"] as string ?? "unclassified",
        observations = ex.Data["hotspot.observations"],
        primaryType = ex.Data["hotspot.primaryType"],
        primaryHresult = ex.Data["hotspot.primaryHresult"],
        cleanupHresult = ex.Data["hotspot.cleanupHresult"],
        callSites = new StackTrace(ex, false).GetFrames()?.Select(frame =>
            frame.GetMethod()?.DeclaringType?.FullName + "." + frame.GetMethod()?.Name).Take(12).ToArray()
        // Never serialize request, Exception.Message/ToString(), password or MACs.
    });
}
