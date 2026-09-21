using System.Text;

namespace IRSpeedy.Hotspot;

public sealed class HotspotException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record Adapter(Guid Id, string Name, string Description, bool Up, int? SharingRole);
public sealed record Journal(int Version, Guid PublicId, Guid? PrivateId, bool StartAttempted, Guid? BootstrapId = null, string Kind = "mobile-hotspot");
public sealed record SharingAdapter(Guid Id, bool Up, int? Role);
public sealed record SharingObservation(string Phase, int Poll, Guid PublicId, Guid? PrivateId, SharingAdapter[] Adapters);
public sealed record IcsAttempt(int Attempt, int Role, string Result, string? Hresult = null);
public sealed record HealthReport(string? Reason, bool? BackendOn, SharingAdapter[] Adapters,
    string? ExceptionType = null, string? Hresult = null)
{
    public bool Healthy => Reason is null;
}

public static class IcsPreparation
{
    public static void ResetIncompletePrivate(Guid publicId, Guid privateId,
        Func<IReadOnlyList<Adapter>> read, Action<Guid, int> disable,
        Action<int> delay, Action<string> report)
    {
        IReadOnlyList<Adapter> CheckedRead()
        {
            var state = read();
            Safety.RequireTun(state, publicId);
            if (publicId == privateId || !state.Any(a => a.Id == privateId && a.Up &&
                a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)))
                throw new HotspotException("invalid-private-adapter");
            Safety.RequireRebindPair(state, publicId, privateId, null);
            return state;
        }
        var current = CheckedRead();
        if (Safety.PairMatches(current, publicId, privateId)) { report("complete-pair-preserved"); return; }
        if (!current.Any(a => a.Id == privateId && a.SharingRole == 1)) return;
        report("incomplete-private-detected");
        // This is the selected session-owned adapter, never a disable-all reset.
        disable(privateId, 1);
        report("disable-private-returned");
        for (int poll = 0; poll < 5; poll++)
        {
            current = CheckedRead();
            if (Safety.PairMatches(current, publicId, privateId)) { report("complete-pair-observed"); return; }
            if (!current.Any(a => a.Id == privateId && a.SharingRole == 1))
            { report("private-reset-verified"); return; }
            if (poll < 4) delay(250);
        }
        throw new HotspotException("private-reset-not-confirmed");
    }
}

public static class IcsRetry
{
    // Only this HRESULT is eligible, and each fresh read revalidates ownership.
    public const int SubscriberFailure = unchecked((int)0x80040201);
    public static void Enable(Guid publicId, Guid privateId, int role,
        Func<IReadOnlyList<Adapter>> read, Action<Guid, int> enable,
        Action<int> delay, Action<IcsAttempt> report)
    {
        int[] waits = [250, 350, 500, 500];
        Guid target = role == 0 ? publicId : privateId;
        bool AlreadyEnabled()
        {
            var state = read();
            Safety.RequireTun(state, publicId);
            if (publicId == privateId || !state.Any(a => a.Id == privateId && a.Up &&
                a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)))
                throw new HotspotException("invalid-private-adapter");
            Safety.RequireRebindPair(state, publicId, privateId, null);
            return state.Any(a => a.Id == target && a.SharingRole == role);
        }
        for (int attempt = 1; attempt <= waits.Length + 1; attempt++)
        {
            if (AlreadyEnabled()) { report(new(attempt, role, "already-enabled")); return; }
            try { enable(target, role); report(new(attempt, role, "call-succeeded")); return; }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == SubscriberFailure)
            {
                report(new(attempt, role, "subscriber-failure", ex.HResult.ToString("X8")));
                // A failing native call may nevertheless have changed ICS. Never
                // repeat a mutation blindly or overwrite a newly shared foreign pair.
                if (AlreadyEnabled()) { report(new(attempt, role, "verified-after-error")); return; }
                if (attempt > waits.Length) throw;
                delay(waits[attempt - 1]);
            }
        }
    }
}

public interface IJournal
{
    Journal? Read();
    void Write(Journal state);
    void Clear();
}

public interface IHotspotBackend
{
    string Kind => "mobile-hotspot";
    bool AutomaticSharing => true;
    object? Diagnostics => null;
    void EnableClients() { }
    IReadOnlyList<Adapter> ReadAdapters();
    void Prepare(Guid publicId);
    void PrepareBootstrap(Guid publicId);
    Guid? BootstrapId { get; }
    Task Start(string ssid, string password);
    Task Stop();
    void Bind(Guid publicId, Guid privateId);
    void Disable(Guid id, int expectedRole);
    bool IsOn { get; }
    uint ClientCount { get; }
}

public static class Safety
{
    public static void ValidateCredentials(string ssid, string password)
    {
        if (string.IsNullOrWhiteSpace(ssid) || Encoding.UTF8.GetByteCount(ssid) > 32 || ssid.Any(char.IsControl))
            throw new HotspotException("invalid-ssid");
        if (password.Length is < 8 or > 63 || password.Any(c => c < 32 || c > 126))
            throw new HotspotException("invalid-passphrase");
    }

    public static Adapter RequireTun(IEnumerable<Adapter> adapters, Guid id)
    {
        var a = adapters.SingleOrDefault(a => a.Id == id);
        // Exact application-owned interface name; NEVER choose the default route.
        if (a is null || !a.Up || !string.Equals(a.Name, "irspeedy-tun", StringComparison.OrdinalIgnoreCase))
            throw new HotspotException("active-irspeedy-tun-required");
        return a;
    }

    public static Guid SelectPrivate(IReadOnlyList<Adapter> before, IReadOnlyList<Adapter> after, Guid publicId)
    {
        var candidates = after.Where(a => a.Id != publicId && a.Up &&
            a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)).ToArray();
        var shared = candidates.Where(a => a.SharingRole == 1).ToArray();
        if (shared.Length == 1) return shared[0].Id;
        if (shared.Length > 1) throw new HotspotException("ambiguous-hotspot-adapter");
        var activated = candidates.Where(a => !before.Any(b => b.Id == a.Id && b.Up)).ToArray();
        if (activated.Length == 0) throw new HotspotException("hotspot-adapter-not-ready");
        if (activated.Length != 1) throw new HotspotException("ambiguous-hotspot-adapter");
        return activated[0].Id;
    }

    public static bool PairMatches(IEnumerable<Adapter> adapters, Guid publicId, Guid privateId)
    {
        var shared = adapters.Where(a => a.SharingRole.HasValue).ToArray();
        return shared.Length == 2 && shared.Any(a => a.Id == publicId && a.Up && a.SharingRole == 0) &&
            shared.Any(a => a.Id == privateId && a.Up && a.SharingRole == 1);
    }

    public static void RequireRebindPair(IReadOnlyList<Adapter> adapters, Guid publicId, Guid privateId, Guid? bootstrapId)
    {
        var shared = adapters.Where(a => a.SharingRole.HasValue).ToArray();
        if (shared.Any(a => !(a.Id == publicId && a.SharingRole == 0 ||
            a.Id == privateId && a.SharingRole == 1 || a.Id == bootstrapId && a.SharingRole == 0)))
            throw new HotspotException("ics-ownership-conflict");
        if (bootstrapId != publicId && shared.Any(a => a.Id == bootstrapId && a.SharingRole == 0) &&
            !PairMatches(adapters, bootstrapId!.Value, privateId))
            throw new HotspotException("bootstrap-pair-incomplete");
    }
}

// Commands are serialized by the host. No thread is allowed to start/stop concurrently.
public sealed class Session(IHotspotBackend backend, IJournal journal, Func<Task>? pollDelay = null)
{
    // Immediate read + 53 x 150 ms ~= 8 seconds per phase - the SAME worst-case
    // ceiling as the previous 16 x 500 ms, just polled more finely. The 8s ceiling
    // itself is left untouched (it was tuned against real ICS settle-time logs and
    // shortening it risks reintroducing the flaky failures that ceiling was chosen to
    // avoid); only the polling grain shrank, so a pair that settles quickly is
    // observed up to ~350 ms sooner on average without changing slow-device behavior.
    private const int LastPoll = 53;
    private readonly Func<Task> pause = pollDelay ?? (() => Task.Delay(150));
    private readonly List<SharingObservation> observations = new();
    public IReadOnlyList<SharingObservation> Observations => observations;
    public bool Active { get; private set; }
    public Journal? State { get; private set; }

    public async Task Start(Guid publicId, string ssid, string password, bool experimental)
    {
        if (!experimental) throw new HotspotException("experimental-opt-in-required");
        if (Active || journal.Read() is not null) throw new HotspotException("recovery-required");
        observations.Clear();
        Safety.ValidateCredentials(ssid, password);
        var before = backend.ReadAdapters();
        Safety.RequireTun(before, publicId);
        // Existing ICS is deliberately NOT taken over in this PoC. Its empty baseline
        // is the snapshot, so rollback never needs to disturb another application's pair.
        if (before.Any(a => a.SharingRole.HasValue)) throw new HotspotException("existing-ics-conflict");
        backend.PrepareBootstrap(publicId);
        if (backend.IsOn) throw new HotspotException("existing-hotspot-conflict");
        State = new Journal(backend.Kind == "wifi-direct" ? 3 : backend.BootstrapId.HasValue ? 2 : 1,
            publicId, null, false, backend.BootstrapId, backend.Kind);
        journal.Write(State);
        try
        {
            State = State with { StartAttempted = true };
            journal.Write(State); // durable BEFORE any networking mutation
            await backend.Start(ssid, password);
            // Give WinRT time to finish publishing its ICS state before attempting
            // any legacy write. A working pair bypasses Bind entirely.
            if (!await WaitForPair(before, publicId, backend.AutomaticSharing ? "winrt-settle" : "wfd-adapter-ready"))
            {
                if (State.PrivateId is not Guid privateId)
                    throw new HotspotException("hotspot-adapter-not-ready");
                Observe("before-bind", 0, backend.ReadAdapters());
                backend.Bind(publicId, privateId); // one transaction; native retry is bounded by IcsRetry
                if (!await WaitForPair(before, publicId, "bind-verify"))
                    throw new HotspotException("ics-verification-failed");
            }
            backend.EnableClients();
            Active = true;
        }
        catch (Exception primary)
        {
            // Snapshot BEFORE rollback; preserve the original failure if a diagnostic
            // read itself fails. Never capture names, SSID, passphrase or MAC addresses.
            try { Observe("failure-before-cleanup", 0, backend.ReadAdapters()); } catch { }
            primary.Data["hotspot.observations"] = observations.ToArray();
            primary.Data["hotspot.backendState"] = backend.Diagnostics;
            try { await Stop(); }
            catch (Exception cleanup)
            {
                var failure = new HotspotException("start-failed-recovery-required");
                failure.Data["hotspot.primaryType"] = primary.GetType().Name;
                failure.Data["hotspot.primaryHresult"] = primary.HResult.ToString("X8");
                failure.Data["hotspot.stage"] = primary.Data["hotspot.stage"];
                failure.Data["hotspot.cleanupHresult"] = cleanup.HResult.ToString("X8");
                failure.Data["hotspot.observations"] = observations.ToArray();
                failure.Data["hotspot.backendState"] = primary.Data["hotspot.backendState"];
                throw failure;
            }
            throw;
        }
    }

    private async Task<bool> WaitForPair(IReadOnlyList<Adapter> before, Guid publicId, string phase)
    {
        for (int poll = 0; poll <= LastPoll; poll++)
        {
            var current = backend.ReadAdapters();
            Observe(phase, poll, current);
            Safety.RequireTun(current, publicId);
            if (!backend.IsOn) throw new HotspotException("hotspot-stopped-during-settle");
            if (State!.PrivateId is null)
            {
                try
                {
                    State = State with { PrivateId = Safety.SelectPrivate(before, current, publicId) };
                    journal.Write(State);
                }
                catch (HotspotException ex) when (ex.Code == "hotspot-adapter-not-ready") { }
            }
            if (current.Any(a => a.SharingRole.HasValue && !
                (a.Id == publicId && a.SharingRole == 0 || a.Id == State.PrivateId && a.SharingRole == 1 ||
                 phase == "winrt-settle" && a.Id == State.BootstrapId && a.SharingRole == 0)))
                throw new HotspotException("ics-ownership-conflict");
            if (State.PrivateId is Guid privateId && Safety.PairMatches(current, publicId, privateId)) return true;
            // Legacy AP does not set up ICS; bind as soon as its adapter appears.
            if (phase == "wfd-adapter-ready" && State.PrivateId.HasValue) return false;
            if (poll < LastPoll) await pause();
        }
        return false;
    }

    private void Observe(string phase, int poll, IReadOnlyList<Adapter> adapters)
    {
        if (State is null || observations.Count >= 40) return;
        observations.Add(new SharingObservation(phase, poll, State.PublicId, State.PrivateId,
            adapters.Where(a => a.Id == State.PublicId || a.Id == State.PrivateId || a.SharingRole.HasValue ||
                a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
            .Select(a => new SharingAdapter(a.Id, a.Up, a.SharingRole)).ToArray()));
    }

    public bool Healthy() => CheckHealth().Healthy;

    public HealthReport CheckHealth()
    {
        if (!Active || State?.PrivateId is not Guid privateId)
            return new("session-inactive", null, []);
        bool? on = null;
        SharingAdapter[] snapshot = [];
        try
        {
            var adapters = backend.ReadAdapters();
            snapshot = adapters.Where(a => a.Id == State.PublicId || a.Id == privateId || a.SharingRole.HasValue)
                .Select(a => new SharingAdapter(a.Id, a.Up, a.SharingRole)).ToArray();
            on = backend.IsOn;
            var tun = adapters.SingleOrDefault(a => a.Id == State.PublicId);
            var output = adapters.SingleOrDefault(a => a.Id == privateId);
            string? reason = !on.Value ? "backend-stopped"
                : tun is null ? "tun-missing"
                : !tun.Up ? "tun-down"
                : !tun.Name.Equals("irspeedy-tun", StringComparison.OrdinalIgnoreCase) ? "tun-name-changed"
                : output is null ? "private-missing"
                : !output.Up ? "private-down"
                : !Safety.PairMatches(adapters, State.PublicId, privateId) ? "ics-pair-changed" : null;
            return new(reason, on, snapshot);
        }
        catch (Exception ex) { return new("health-read-failed", on, snapshot, ex.GetType().Name, ex.HResult.ToString("X8")); }
    }

    public async Task Stop()
    {
        Active = false;
        var state = State ?? journal.Read();
        if (state is null) return;
        if (state.Version is not (1 or 2 or 3) || state.Version == 2 && state.BootstrapId is null ||
            state.Version == 3 && (state.Kind != "wifi-direct" || state.BootstrapId.HasValue) ||
            state.Version < 3 && state.Kind != "mobile-hotspot" || state.Kind != backend.Kind ||
            state.Version == 1 && state.BootstrapId.HasValue ||
            state.PublicId == Guid.Empty || state.PrivateId == state.PublicId ||
            state.BootstrapId == Guid.Empty || state.BootstrapId.HasValue && state.BootstrapId == state.PrivateId)
            throw new HotspotException("invalid-recovery-journal");
        State = state;
        // Recover the exact recorded startup profile, never the current default.
        backend.Prepare(state.BootstrapId ?? state.PublicId);
        if (state.StartAttempted) await backend.Stop();
        var afterStop = backend.ReadAdapters();
        if (state.PrivateId is Guid privateId && afterStop.Any(a => a.Id == privateId))
            backend.Disable(privateId, 1);
        if (afterStop.Any(a => a.Id == state.PublicId)) backend.Disable(state.PublicId, 0);
        if (state.BootstrapId is Guid bootstrapId && bootstrapId != state.PublicId &&
            afterStop.Any(a => a.Id == bootstrapId)) backend.Disable(bootstrapId, 0);
        // The accepted baseline had NO ICS; any remaining sharing means restoration
        // is not proven. Keep the journal, but do not disable an unrelated pair.
        if (backend.IsOn || backend.ReadAdapters().Any(a => a.SharingRole.HasValue))
            throw new HotspotException("cleanup-verification-failed");
        journal.Clear();
        State = null;
    }
}
