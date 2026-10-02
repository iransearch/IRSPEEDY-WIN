using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;

namespace IRSpeedyVPN.Common
{
    internal static class ErrorReporting
    {
        internal static int Observed, Captured;
        internal static void ObserveDiagnostic(string message) { Observed++; }
        internal static void Capture(Exception exception, bool crash) { Captured++; }
    }
}

internal static class Program
{
    private static int passed;
    private static void Check(bool valid, string name)
    {
        if (!valid) throw new Exception(name);
        Console.WriteLine("PASS " + name); passed++;
    }

    private static void Main(string[] args)
    {
        var root = args[0];
        var today = new DateTime(2026, 10, 2);
        var folder = Path.Combine(root, "retention");
        Directory.CreateDirectory(folder);
        Func<DateTime, string> daily = d => Path.Combine(folder, "log-" + d.ToString("yyyy-MM-dd") + ".txt");
        for (int i = 0; i < 10; i++) File.WriteAllText(daily(today.AddDays(-i)), "old\n");
        File.WriteAllText(Path.Combine(folder, "log-invalid.txt"), "unrelated");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "unrelated");
        var store = new DailyLogStore(folder);
        store.Append(today, "today\n");
        Check(Enumerable.Range(0, 7).All(i => File.Exists(daily(today.AddDays(-i)))), "today and previous six dates survive");
        Check(Enumerable.Range(7, 3).All(i => !File.Exists(daily(today.AddDays(-i)))), "older daily logs removed");
        Check(File.Exists(Path.Combine(folder, "notes.txt")) && File.Exists(Path.Combine(folder, "log-invalid.txt")), "unrelated files preserved");
        store.Append(today.AddDays(1), "tomorrow\n");
        Check(!File.Exists(daily(today.AddDays(-6))) && File.ReadAllText(daily(today.AddDays(1))) == "tomorrow\n", "midnight rotates file and repeats retention cleanup");
        var parallelFolder = Path.Combine(root, "parallel");
        var parallelStore = new DailyLogStore(parallelFolder);
        Parallel.For(0, 400, i => parallelStore.Append(today, i + "\n"));
        var lines = File.ReadAllLines(Path.Combine(parallelFolder, "log-2026-10-02.txt"));
        Check(lines.Length == 400 && lines.Distinct().Count() == 400, "concurrent writes lose no records");

        Check(!LogPolicy.ShouldWrite("[LoginPerformance] stage=complete elapsedMs=1")
            && !LogPolicy.ShouldWrite("[ProbeDetail] result=success")
            && !LogPolicy.ShouldWrite("[NetworkMonitor] host=example attempt=1 status=Success"), "routine timing and successful probe chatter suppressed");
        Check(!LogPolicy.UsefulDiagnostic("runtime-snapshot", "changed=False")
            && LogPolicy.UsefulDiagnostic("runtime-snapshot", "changed=True")
            && !LogPolicy.UsefulDiagnostic("ui-navigation", "screen=home"), "unchanged state snapshots and UI chatter suppressed");
        Check(!LogPolicy.UsefulDiagnostic("core-output", "category=route")
            && LogPolicy.UsefulDiagnostic("core-output", "category=pool-selection-error")
            && LogPolicy.UsefulDiagnostic("core-output", "category=showip-route"), "ordinary routes filtered while pool failures and explicit IP-check routes survive");
        Check(LogPolicy.UsefulDiagnostic("core-detail", "event=hy2-reset error=none")
            && LogPolicy.UsefulDiagnostic("core-detail", "event=pool-probes-ready error=none")
            && !LogPolicy.UsefulDiagnostic("core-detail", "event=probe-end error=none"), "core resets and pool readiness preserved without successful per-probe noise");
        Check(LogPolicy.UsefulDiagnostic("core-detail", "event=probe-end error=timeout")
            && LogPolicy.UsefulDiagnostic("core-health-end", "exception=IOException")
            && LogPolicy.ShouldWrite("[ProbeDetail] result=failure error=timeout")
            && LogPolicy.ShouldWrite("[UrlTest] stage=retry-failed exception=IOException"), "failures override routine filters");
        Check(LogPolicy.ShouldWrite("[UrlTest] stage=final succeeded=3")
            && !LogPolicy.ShouldWrite("[UrlTest] stage=primary-start candidates=3")
            && LogPolicy.ShouldWrite("[NetworkMonitor] online=false reason=failed")
            && LogPolicy.ShouldWrite("Unknown future diagnostic"), "test outcomes, offline changes and unclassified messages retained");

        var resolved = DailyLogStore.ResolveDirectory();
        Check(resolved == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRSpeedyVPN", "Logs")
            && resolved.StartsWith(root, StringComparison.Ordinal), "logger resolves local app data rather than installation directory");
        LogHelper.WriteExLog("[ProbeDetail] result=success");
        LogHelper.WriteLog("[LoginPerformance] stage=complete");
        LogHelper.WriteExLog("[ProbeDetail] result=failure error=timeout");
        LogHelper.WriteLog(new InvalidOperationException("test exception"), true);
        var written = string.Join("\n", Directory.GetFiles(resolved).Select(File.ReadAllText));
        Check(!written.Contains("result=success") && !written.Contains("LoginPerformance")
            && written.Contains("result=failure") && written.Contains("[Crashed]")
            && written.Contains("InvalidOperationException"), "actual logger filters routine records and retains complete exceptions");
        Check(ErrorReporting.Observed == 2 && ErrorReporting.Captured == 1, "error reporting still receives filtered diagnostic observations and crashes");
        Directory.Delete(resolved, true);
        File.WriteAllText(resolved, "blocking directory creation");
        LogHelper.WriteLog("disk unavailable");
        LogHelper.WriteLog(new IOException("still capture when disk unavailable"));
        Check(ErrorReporting.Captured == 2, "failed log storage cannot interrupt application or error capture");
        Console.WriteLine(passed + " logging checks passed.");
    }
}
