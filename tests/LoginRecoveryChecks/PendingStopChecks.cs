using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class PendingStopChecks
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static Process Sleep(string duration)
        => Process.Start(new ProcessStartInfo("/bin/sleep", duration) { UseShellExecute = false });

    internal static void Run()
    {
        // An independent monitor owns its own handle; registry ownership is
        // transferred and must dispose the original even without another login.
        var released = Sleep("30");
        using (var monitor = Process.GetProcessById(released.Id))
        {
            var releasedHandle = released.SafeHandle;
            CurlHelper.RememberPendingForTest(released);
            var stillOwned = Sleep("0.1");
            var ownedHandle = stillOwned.SafeHandle;
            CurlHelper.RememberOwnedForTest(stillOwned);
            try
            {
                Thread.Sleep(1200);
                Check(!releasedHandle.IsClosed && !ownedHandle.IsClosed && CurlHelper.PendingCountForTest == 2,
                    "background cleanup retains a live process and never disposes a process still owned by ExecuteCurl");
                try { CurlHelper.EnsureCredentialCurlStopped(); throw new Exception("live pending process accepted"); }
                catch (LoginTransportTimeoutException) { }
                CurlHelper.ReleaseOwnerForTest(stillOwned);
                Check(ownedHandle.IsClosed, "owner handoff disposes an already exited pending process");
                var timer = CurlHelper.PendingTimerInstanceForTest;
                monitor.Kill(); monitor.WaitForExit(2000);
                // These accessors inspect only: neither invokes cleanup/pruning.
                Check(SpinWait.SpinUntil(() => CurlHelper.PendingCountForTest == 0 && !CurlHelper.PendingTimerForTest, 3500)
                    && releasedHandle.IsClosed,
                    "pending process handle and shared timer are disposed after exit without any subsequent login");
                bool timerDisposed = false;
                try { timerDisposed = !timer.Change(Timeout.Infinite, Timeout.Infinite); }
                catch (ObjectDisposedException) { timerDisposed = true; }
                finally { timer.Dispose(); }
                Check(timerDisposed, "empty-registry cleanup disposes the timer object rather than merely dropping its reference");
            }
            finally
            {
                if (!monitor.HasExited) { monitor.Kill(); monitor.WaitForExit(2000); }
                CurlHelper.ReleaseOwnerForTest(stillOwned);
                CurlHelper.EnsureCredentialCurlStopped();
            }
        }
        // A later registration must re-arm cleanup after the previous timer died.
        var next = Sleep("0.1"); var nextHandle = next.SafeHandle;
        CurlHelper.RememberPendingForTest(next);
        Check(SpinWait.SpinUntil(() => CurlHelper.PendingCountForTest == 0 && !CurlHelper.PendingTimerForTest, 3500) && nextHandle.IsClosed,
            "a new pending process re-arms cleanup and closes its handle without a POST");
        TestProbe("--pending-exit-probe", "actual ProcessExit hook disposes retained handles and stops the timer");
        TestProbe("--pending-shutdown-probe", "explicit shutdown handles active owners and late registrations without allowing credentials");
    }

    internal static void Probe(string mode, string report)
    {
        var pending = Sleep("30"); var handle = pending.SafeHandle;
        File.AppendAllText(report + ".pids", pending.Id + "\n");
        CurlHelper.RememberPendingForTest(pending);
        Action write = () =>
        {
            bool clean = handle.IsClosed && CurlHelper.PendingCountForTest == 0 && !CurlHelper.PendingTimerForTest;
            bool blocked = false;
            try { CurlHelper.EnsureCredentialCurlStopped(); } catch (LoginTransportTimeoutException) { blocked = true; }
            File.WriteAllText(report, clean && blocked ? "clean" : "failed");
        };
        if (mode == "--pending-exit-probe")
        {
            // Registered after CurlHelper's real hook. No explicit shutdown call.
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => write();
            return;
        }
        var owned = Sleep("30"); var ownedHandle = owned.SafeHandle;
        File.AppendAllText(report + ".pids", owned.Id + "\n");
        CurlHelper.RememberOwnedForTest(owned);
        CurlHelper.ShutdownPendingCurlStops();
        bool ownerPreserved = !ownedHandle.IsClosed;
        CurlHelper.ReleaseOwnerForTest(owned);
        var late = Sleep("30"); var lateHandle = late.SafeHandle;
        File.AppendAllText(report + ".pids", late.Id + "\n");
        CurlHelper.RememberPendingForTest(late);
        CurlHelper.ShutdownPendingCurlStops(); // Idempotent, like the later ProcessExit.
        write();
        if (!ownerPreserved || !ownedHandle.IsClosed || !lateHandle.IsClosed) File.WriteAllText(report, "failed-owner");
    }

    static void TestProbe(string mode, string name)
    {
        string report = Path.Combine(Path.GetTempPath(), "pending-exit-" + Guid.NewGuid().ToString("N"));
        try
        {
            string host = Environment.GetEnvironmentVariable("IRSPEEDY_TEST_DOTNET");
            if (string.IsNullOrEmpty(host)) throw new Exception("test runner did not supply its .NET host");
            string assembly = Assembly.GetExecutingAssembly().Location;
            using (var child = Process.Start(new ProcessStartInfo(host, "\"" + assembly + "\" " + mode + " \"" + report + "\"")
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(2000); throw new Exception("shutdown probe timed out"); }
                Check(child.ExitCode == 0 && File.Exists(report) && File.ReadAllText(report) == "clean", name);
            }
        }
        finally
        {
            if (File.Exists(report + ".pids"))
            {
                foreach (var line in File.ReadAllLines(report + ".pids"))
                {
                    try { using (var process = Process.GetProcessById(int.Parse(line))) { process.Kill(); process.WaitForExit(2000); } }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                File.Delete(report + ".pids");
            }
            if (File.Exists(report)) File.Delete(report);
        }
    }
}
