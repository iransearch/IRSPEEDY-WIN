"""Run the real display scan controller and cache with an offline probe transport."""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
background = source.split('        #region Background URL tests', 1)[1].split('        #endregion', 1)[0]
country = source.split('        #region Country picker', 1)[1].split('        #endregion', 1)[0]
assert country.count('testUrl: testUrl);') == 1, 'route fixture cache writes to the temporary directory'
country = country.replace('testUrl: testUrl);', 'directory: cacheDirectory, testUrl: testUrl);')
manual = (root / 'IRSpeedyVPN/UserControls/UCServerList.ServerResultsRefresh.cs').read_text()
manual = manual[manual.index('        private bool manualRefreshBusy;'):manual.rindex('\n    }')]
assert 'probeTimer' not in source and 'ServerProbeSchedule' not in source

stubs = r'''
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services;

namespace IRSpeedyVPN.Models { public enum VPNType { NORMAL, CHAIN } }
namespace IRSpeedyVPN.Interfaces {
    internal interface IVPNService {
        int ID { get; }
        string Name { get; }
        string Country { get; }
        string CountryCode { get; }
        byte CountryIndex { get; set; }
        string[] Protocols { get; }
        bool IsUrlTestSupported { get; }
        Url SelectedServerUrl { get; }
        List<Url> GetServerUrls();
    }
    internal interface ISmartFastConnection { bool IsSmartFast { get; } }
}
class TunnelPlusService : IVPNService {
    public int ID { get; set; }
    public string Name => "xfast";
    public string CountryCode { get; set; }
    public string Country => CountryCode;
    public byte CountryIndex { get; set; }
    public string[] Protocols => new[] { "vless" };
    public Url SelectedServerUrl => null;
    public bool IsUrlTestSupported { get; set; } = true;
    public readonly List<Url> urls = new List<Url>();
    public List<Url> GetServerUrls() => urls;
    public void UpdateListTestResult() {}
    public static Func<Url, string, CancellationToken, ServerProbeMeasurement> Probe;
    public static ServerProbeMeasurement TestRefreshBatch(Url url, Info info, string testUrl, CancellationToken token)
        => Probe(url, testUrl, token);
}
class CiscoService : TunnelPlusService {
    public static Func<Url, CancellationToken, ServerProbeMeasurement> ProbeCisco;
    public static ServerProbeMeasurement TestDisplayServer(Url url, CancellationToken token) => ProbeCisco(url, token);
}
namespace IRSpeedyVPN.Residential {
    class ResidentialService : global::TunnelPlusService {}
    static class ResidentialPresentation {
        internal static string ConnectionUri(string value) => (value ?? "").Split('#')[0];
    }
}
class Info {
    public string Username;
    public IVPNService CurrentService;
    public readonly Settings settings = new Settings();
}
class Settings { public readonly Setting setting = new Setting(); }
class Setting { public string url_test = "http://probe.example.invalid/generate_204"; }
class Factory { public IVPNService[] Services; }
class ResidentialCatalog {
    public IVPNService[] Services;
    public Func<bool, CancellationToken, Task<bool>> Load;
    public Task<bool> RefreshAsync(bool force, CancellationToken cancellation) => Load(force, cancellation);
}
class FakeDispatcher {
    readonly SynchronizationContext context = SynchronizationContext.Current;
    readonly int thread = Thread.CurrentThread.ManagedThreadId;
    public Action Before;
    public bool CheckAccess() => Thread.CurrentThread.ManagedThreadId == thread;
    public void Invoke(Action action) {
        Action apply = () => { Before?.Invoke(); action(); };
        if (CheckAccess()) apply(); else context.Send(_ => apply(), null);
    }
}
class UiContext : SynchronizationContext {
    readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
    public override void Post(SendOrPostCallback callback, object state) => queue.Add(() => callback(state));
    public override void Send(SendOrPostCallback callback, object state) {
        Exception error = null;
        using (var done = new ManualResetEventSlim()) {
            queue.Add(() => {
                try { callback(state); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait();
        }
        if (error != null) throw error;
    }
    public void Run(Func<Task> action) {
        SetSynchronizationContext(this);
        var task = action();
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted) {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("UI test did not finish");
            if (queue.TryTake(out var next, 25)) next();
        }
        task.GetAwaiter().GetResult();
    }
}
class Picker {
    readonly int thread = Thread.CurrentThread.ManagedThreadId;
    public readonly HashSet<IVPNService> Checking = new HashSet<IVPNService>();
    public readonly List<IVPNService> Published = new List<IVPNService>();
    public readonly List<IVPNService> Animated = new List<IVPNService>();
    public readonly List<IVPNService> Restored = new List<IVPNService>();
    public IVPNService SelectedService;
    public IVPNService[] Retained = new IVPNService[0];
    public int CycleStarts;
    public void SetGroupChecking(IVPNService row, bool checking) {
        CheckThread();
        if (checking) Checking.Add(row); else Checking.Remove(row);
    }
    public void RefreshGroup(IVPNService row, bool allowSortAnimation = true) {
        CheckThread();
        if (Checking.Contains(row)) throw new Exception("row published before checking was cleared");
        Published.Add(row);
        if (allowSortAnimation) Animated.Add(row);
    }
    public void RetainCompletedResults(IEnumerable<IVPNService> rows) {
        CheckThread();
        Retained = rows.ToArray();
    }
    public void Load(IVPNService[] rows, bool supported) { CheckThread(); Checking.Clear(); }
    public void RestoreTestProgress(IVPNService row) { CheckThread(); Restored.Add(row); }
    public void BeginCheckCycle() { CheckThread(); CycleStarts++; }
    public void EndCheckCycleSetup() { CheckThread(); }
    public void SetFilter(string text) { CheckThread(); }
    void CheckThread() {
        if (Thread.CurrentThread.ManagedThreadId != thread) throw new Exception("picker updated off UI thread");
    }
}
class LogHelper {
    public static void WriteLog(Exception error) => throw error;
    public static void WriteExLog(string value) {}
}
class Program {
    readonly FakeDispatcher Dispatcher = new FakeDispatcher();
    readonly Picker countryPicker = new Picker();
    readonly Info globalInfo = new Info();
    readonly Factory serviceFactory = new Factory();
    readonly ResidentialCatalog residential = new ResidentialCatalog();
    readonly string cacheDirectory;
    readonly SearchBox txtSearch = new SearchBox();
    class SearchBox { public string Text; }

    bool IsLoaded = true, IsVisible = true;
    CancellationTokenSource _urlTestCts;
    IVPNService[] _currentServices;
    IVPNService selectedService;
    string _selectedServiceName = "xfast";
    bool isResidentialTab, _isLoading, _isUrlTestSupported;
    Func<Func<bool>, CancellationToken, Task<bool>> RefreshServerListRequest;

    void TrySelectResidentialFastest() {}
    void UpdateResidentialVisuals() {}
    void UpdateHeaderIcons() {}
    Program(string directory, string account) { cacheDirectory = directory; globalInfo.Username = account; }

    void Bind(IVPNService[] rows, bool preserveManualRefresh = false) {
        serviceFactory.Services = residential.Services = rows;
        RefreshCountry(null, preserveManualRefresh);
    }

    static void Check(bool value, string name) {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    static TunnelPlusService S(string country, int id, string config = null) {
        var row = new TunnelPlusService { ID = id, CountryCode = country };
        row.urls.Add(new Url { url = config ?? ("vless://fixture-" + id + "@example.invalid"), extra_field_1 = "config" });
        return row;
    }
    static long Ping(IVPNService row) => row.GetServerUrls()[0].latency;
    static async Task Until(Func<bool> condition) {
        for (int i = 0; i < 1000 && !condition(); i++) await Task.Delay(5);
        if (!condition()) throw new TimeoutException("expected asynchronous condition");
    }
    Task Settle() => Until(() => !probeRunning);
    static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    static void Main() {
        string directory = Path.Combine(Path.GetTempPath(), "irspeedy-check-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { new UiContext().Run(() => MainAsync(directory)); }
        finally { Directory.Delete(directory, true); }
    }
'''

automatic_checks = r'''
    static async Task MainAsync(string directory) {
        var view = new Program(directory, "display-auto");
        var success = S("DE", 1);
        var failure = S("DE", 2);
        var slow = S("US", 3);
        var unsupported = S("GB", 4);
        unsupported.IsUrlTestSupported = false;
        var cisco = new CiscoService { ID = 5, CountryCode = "CA" };
        cisco.urls.Add(new Url { url = "cisco://fixture.example.invalid" });
        var entered = Signal();
        var canceled = Signal();
        var calls = new ConcurrentQueue<string>();
        CiscoService.ProbeCisco = (url, token) => {
            calls.Enqueue(url.url);
            return new ServerProbeMeasurement(179);
        };
        using (var release = new ManualResetEventSlim()) {
            TunnelPlusService.Probe = (url, target, token) => {
                calls.Enqueue(url.url);
                if (target != view.globalInfo.settings.setting.url_test) throw new Exception("wrong configured target");
                if (url.url == failure.urls[0].url) return ServerProbeMeasurement.Failed;
                if (url.url == slow.urls[0].url) {
                    url.latency = 999;
                    using (token.Register(() => canceled.TrySetResult(true))) {
                        entered.SetResult(true);
                        if (!release.Wait(5000)) throw new TimeoutException("waiting to release old display probe");
                        // Even a successful late reply must be ignored after cancellation.
                        return new ServerProbeMeasurement(1);
                    }
                }
                return new ServerProbeMeasurement(175);
            };
            view.Bind(new IVPNService[] { success, failure, slow, unsupported, cisco });
            await entered.Task;
            await Until(() => view.countryPicker.Published.Count == 3);
            Check(success.CountryIndex == 1 && failure.CountryIndex == 2 &&
                  Ping(success) == 175 && Ping(failure) == -1 && Ping(cisco) == 179 &&
                  view.countryPicker.Checking.SetEquals(new[] { slow }) && Ping(slow) == 0 &&
                  calls.Count == 4 && !calls.Contains(unsupported.urls[0].url),
                  "startup completes independent numbered rows and Cisco while the slow row stays pending and unsupported rows stay idle");
            view.PauseServerChecksForConnection();
            await canceled.Task;
            var drain = view.DrainServerChecksAsync();
            Check(!drain.IsCompleted, "connection waits for the canceled display probe to release its worker");
            release.Set();
            await drain;
            await view.Settle();
            Check(Ping(slow) == 0 && view.countryPicker.Restored.SequenceEqual(new[] { slow }) &&
                  !view.countryPicker.Checking.Any() && !view.countryPicker.Animated.Contains(slow),
                  "cancellation discards the late result and restores row presentation without sorting it");
        }

        var afterSuccess = S("DE", 1);
        var afterFailure = S("DE", 2);
        var afterSlow = S("US", 3);
        var cache = new ServerCheckCache("display-auto", "xfast\n", directory, testUrl: view.globalInfo.settings.setting.url_test);
        cache.Bind(new[] { afterSuccess, afterFailure, afterSlow });
        Check(Ping(afterSuccess) == 175 && Ping(afterFailure) == -1 && Ping(afterSlow) == 0 && cache.NextService == null,
              "completed outcomes survive app restart while the connection-consumed scan stays stopped");
        view.ResumeServerChecksAfterCleanup();
        Check(!view.probeRunning && calls.Count == 4, "ordinary failed-connect cleanup cannot resume the canceled round");

        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            return new ServerProbeMeasurement(220);
        };
        view.globalInfo.CurrentService = success;
        view.ResumeServerChecksAfterCleanup(true);
        Check(!view.probeRunning, "active VPN suppresses display probes");
        view.globalInfo.CurrentService = null;
        view.ResumeServerChecksAfterCleanup(true);
        await view.Settle();
        Check(calls.ToArray().Skip(4).OrderBy(link => link).SequenceEqual(new[] { failure.urls[0].url, slow.urls[0].url }) &&
              Ping(success) == 175 && Ping(failure) == 220 && Ping(slow) == 220 &&
              view.countryPicker.Retained.ToHashSet().SetEquals(new IVPNService[] { success, cisco }),
              "explicit disconnect retries only failed and interrupted rows, retaining previous sortable results");

        var changed = S("DE", 2);
        changed.urls[0].extra_field_1 = "changed";
        view.PrepareServerChecksForLogin();
        view.Bind(new[] { afterSuccess, changed, afterSlow });
        await view.Settle();
        Check(calls.Count == 7 && calls.Last() == changed.urls[0].url && Ping(afterSuccess) == 175 &&
              Ping(afterSlow) == 220 && Ping(changed) == 220,
              "login rechecks changed configuration while retaining matching results");
        var fresh = new Program(directory, "display-auto");
        fresh.Bind(new[] { S("DE", 1), S("US", 3) });
        await fresh.Settle();
        Check(calls.Count == 7 && fresh.countryPicker.Retained.Length == 2, "startup with cached successes schedules no probes");
        Check(Directory.GetFiles(directory, "*.json").All(file => !File.ReadAllText(file).Contains("@example.invalid")),
              "display cache stores no subscription links");

        await RebindDuringProbe(directory);
        string legacyDirectory = Path.Combine(directory, "legacy");
        Directory.CreateDirectory(legacyDirectory);
        CacheContracts(legacyDirectory);
    }

    static async Task RebindDuringProbe(string directory) {
        var view = new Program(directory, "display-rebind");
        var old = S("DE", 31);
        var replacement = S("DE", 31);
        replacement.urls[0].extra_field_1 = "new";
        var entered = Signal();
        var canceled = Signal();
        var calls = new ConcurrentQueue<string>();
        using (var release = new ManualResetEventSlim()) {
            TunnelPlusService.Probe = (url, target, token) => {
                calls.Enqueue(url.extra_field_1);
                if (url.extra_field_1 == "new") return new ServerProbeMeasurement(233);
                using (token.Register(() => canceled.TrySetResult(true))) {
                    entered.SetResult(true);
                    if (!release.Wait(5000)) throw new TimeoutException("waiting to release replaced display probe");
                    return new ServerProbeMeasurement(1);
                }
            };
            view.Bind(new[] { old });
            await entered.Task;
            view.PauseServerChecksForConnection();
            view.Bind(new[] { replacement });
            view.ResumeServerChecksAfterCleanup(true);
            await canceled.Task;
            Check(calls.SequenceEqual(new[] { "config" }), "fast disconnect/rebind waits for the old worker to drain");
            release.Set();
            await view.Settle();
            Check(calls.SequenceEqual(new[] { "config", "new" }) && Ping(old) == 0 && Ping(replacement) == 233 &&
                  view.countryPicker.Published.SequenceEqual(new[] { replacement }),
                  "only the current row can publish a result after rapid disconnect and API replacement");
        }
    }
'''

legacy_checks = r'''
    // Historical cache formats are still supported by the cache API. These
    // fixtures exercise migration independently of the display controller.
    static void RecordResults(ServerCheckCache cache, TunnelPlusService service, params long[] latencies) {
        if (service.urls.Count != latencies.Length) throw new Exception("result count does not match row");
        var started = DateTime.Now;
        for (int i = 0; i < latencies.Length; i++) {
            service.urls[i].latency = latencies[i];
            service.urls[i].latencychkTime = DateTime.Now;
        }
        cache.Record(service, started);
    }

    static List<int> CompleteQueue(ServerCheckCache cache) {
        var seen = new List<int>();
        while (cache.NextService != null) {
            var next = cache.NextService;
            seen.Add(next.ID);
            cache.CompleteService(next);
        }
        return seen;
    }

    static void CacheContracts(string dir) {
        var partialBootstrap = new ServerCheckCache("bootstrap", "xfast", dir, new Random(31));
        var fresh = new[] { S("DE", 1), S("DE", 2), S("US", 3) };
        partialBootstrap.Bind(fresh);
        var first = (TunnelPlusService)partialBootstrap.NextService;
        RecordResults(partialBootstrap, first, 175);
        partialBootstrap.CompleteService(first);
        var pendingId = partialBootstrap.NextService.ID;
        partialBootstrap.PersistQueue();
        var reloaded = new ServerCheckCache("bootstrap", "xfast", dir, new Random(999));
        reloaded.Bind(fresh.Reverse());
        Check(reloaded.NextService.ID == pendingId && !reloaded.InitialScanCompleted,
              "legacy bootstrap preserves the unfinished queue head after API reorder and reload");
        var migrationDir = Path.Combine(dir, "v1");
        Directory.CreateDirectory(migrationDir);
        foreach (var file in Directory.GetFiles(dir, "*.json")) {
            var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            data["Version"] = 1;
            data.Remove("PendingRows");
            data.Remove("CompletedRows");
            data["NextCountry"] = "DE";
            File.WriteAllText(Path.Combine(migrationDir, Path.GetFileName(file)), data.ToString());
        }
        var legacy = new ServerCheckCache("bootstrap", "xfast", migrationDir, new Random(7));
        legacy.Bind(fresh);
        var missing = CompleteQueue(legacy);
        Check(missing.Distinct().Count() == 2 && !missing.Contains(first.ID) && legacy.InitialScanCompleted,
              "v1 migration retains completed results and checks only missing bootstrap rows");
        var connectionRow = S("DE", 901);
        var connection = new ServerCheckCache("connection-result", "xfast", dir);
        connection.Bind(new[] { connectionRow });
        connectionRow.urls[0].latency = 155;
        connectionRow.urls[0].latencychkTime = DateTime.Now;
        connection.RecordConnectionResults(connectionRow);
        var replacement = S("DE", 901);
        new ServerCheckCache("connection-result", "xfast", dir).Bind(new[] { replacement });
        Check(Ping(replacement) == 155, "legacy connection-time result survives row replacement and restart");
        new ServerCheckCache("other-account", "xfast", dir).Bind(new[] { replacement });
        Check(Ping(replacement) == 0, "legacy connection result is scoped to its account");
        // A row is available only when its latest full test had a success. Numbered
        // rows are independent, and an older success must not hide a newer failure.
        var mixed=S("DE",11);mixed.urls.Add(new Url{url="vless://secret-mixed@host",extra_field_1="config"});
        var formerlySuccessful=S("DE",12);var partial=S("BR",35);
        var priorityInput=new[]{S("US",90),formerlySuccessful,S("AT",61),S("CA",41),mixed,S("BE",30),S("AT",60),partial,S("CA",40)};
        var priority=new ServerCheckCache("priority","xfast",dir,new Random(73));priority.Bind(priorityInput);
        Check(!priority.PrioritizeFailedServers,"bootstrap does not request failed server ordering");
        foreach(var row in priorityInput.Where(s=>s.ID==90 || s.ID==60 || s.ID==61))RecordResults(priority,row,100+row.ID);
        RecordResults(priority,mixed,-1,211);
        RecordResults(priority,formerlySuccessful,212);RecordResults(priority,formerlySuccessful,-1);
        foreach(var row in priorityInput.Where(s=>s.ID==40 || s.ID==41))RecordResults(priority,row,-1);
        RecordResults(priority,partial,235);
        // Adding a member makes this a new row configuration without a complete test,
        // although one member still retains a successful measurement.
        partial.urls.Add(new Url{url="vless://secret-added@host",extra_field_1="config"});priority.Bind(priorityInput.Reverse());
        Check(partial.urls[0].latency==235 && partial.urls[1].latency==0,"missing member result does not discard an unchanged member measurement");
        Check(formerlySuccessful.urls[0].latency==-1 && formerlySuccessful.urls[0].LastSuccessfulLatency==212,"latest failure retains success history separately");
        CompleteQueue(priority);
        var expectedPriority=new[]{30,35,40,41,12};
        priority.RestartFromFirstCountry();
        Check(priority.PrioritizeFailedServers,"disconnect round requests failed server ordering");
        Check(CompleteQueue(priority).SequenceEqual(expectedPriority),"only unknown, incomplete and latest-failed rows enter stable country/ID order");
        var priorityReload=new ServerCheckCache("priority","xfast",dir,new Random(999));priorityReload.Bind(priorityInput.Reverse());
        Check(priorityReload.NextService==null,"completed priority round stays idle after reload");
        priorityReload.RestartFromFirstCountry();
        Check(CompleteQueue(priorityReload).SequenceEqual(expectedPriority),"latest full row outcome and failure priority survive reload");

        // Ordering is chosen once per round. A later result change or API/display
        // reorder cannot move an already pending row ahead of the saved queue.
        priorityReload.RestartFromFirstCountry();priorityReload.PersistQueue();
        var firstPriority=priorityReload.NextService;priorityReload.CompleteService(firstPriority);
        RecordResults(priorityReload,priorityInput.Single(s=>s.ID==90),-1);
        priorityReload.Bind(priorityInput.Reverse());
        Check(priorityReload.NextService.ID==35,"result updates and rebind keep the next pending row stable");
        var inProgressReload=new ServerCheckCache("priority","xfast",dir,new Random(21));inProgressReload.Bind(priorityInput);
        Check(CompleteQueue(inProgressReload).SequenceEqual(expectedPriority.Skip(1).Concat(new[]{90})),"reload preserves pending order and appends a newly failed country");
        inProgressReload.RestartFromFirstCountry();
        Check(CompleteQueue(inProgressReload).SequenceEqual(new[]{30,35,40,41,12,90}),"next disconnect incorporates outcomes completed during the prior round");

        // A core/RPC infrastructure error can return without testing every member.
        // Even if per-member failures are recorded, priority keeps the last full test.
        var unfinished=S("US",403);unfinished.urls.Add(new Url{url="vless://secret-stale@host",extra_field_1="config"});
        var completeFailure=S("DE",402);var completeSuccess=S("AT",401);
        var incompleteRows=new[]{unfinished,completeFailure,completeSuccess};
        var incompleteCache=new ServerCheckCache("incomplete-priority","xfast",dir);incompleteCache.Bind(incompleteRows);
        RecordResults(incompleteCache,unfinished,503,504);RecordResults(incompleteCache,completeFailure,-1);RecordResults(incompleteCache,completeSuccess,501);CompleteQueue(incompleteCache);
        DateTime laterStart=DateTime.Now.AddSeconds(1);
        unfinished.urls[0].latency=-1;unfinished.urls[0].latencychkTime=laterStart;
        Check(unfinished.urls[1].latencychkTime<laterStart,"infrastructure-error fixture leaves one member untested");
        incompleteCache.Record(unfinished,laterStart);
        incompleteCache.RestartFromFirstCountry();
        Check(CompleteQueue(incompleteCache).SequenceEqual(new[]{402}),"incomplete response preserves the latest full successful row priority");
        unfinished.urls.Reverse();incompleteCache.Bind(incompleteRows.Reverse());
        var incompleteReload=new ServerCheckCache("incomplete-priority","xfast",dir);incompleteReload.Bind(incompleteRows);
        incompleteReload.RestartFromFirstCountry();
        Check(CompleteQueue(incompleteReload).SequenceEqual(new[]{402}),"full-row snapshot survives incomplete member results, URL reorder and reload");
        RecordResults(incompleteReload,unfinished,-1,-1);incompleteReload.RestartFromFirstCountry();
        Check(CompleteQueue(incompleteReload).SequenceEqual(new[]{402,403}),"a subsequent full failed test replaces the previous successful priority");

        // Version 3 has per-member results but no explicit full-row metadata. Empty
        // CompletedRows is normal after cleanup; derive migration from recorded members
        // and their current result, never a historical successful latency.
        string v3Dir=Path.Combine(dir,"v3-priority");Directory.CreateDirectory(v3Dir);
        var v3Failed=S("US",301);var v3Success=S("DE",302);
        var v3Seed=new ServerCheckCache("v3-priority","xfast",v3Dir);v3Seed.Bind(new[]{v3Failed,v3Success});
        RecordResults(v3Seed,v3Failed,401);RecordResults(v3Seed,v3Failed,-1);RecordResults(v3Seed,v3Success,402);
        CompleteQueue(v3Seed);v3Seed.StopRound();
        foreach(var file in Directory.GetFiles(v3Dir,"*.json")){
         var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));old["Version"]=3;old.Remove("RowResults");
         Check(!old["CompletedRows"].Any(),"legacy completed-row list is empty after cleanup");File.WriteAllText(file,old.ToString());
        }
        var v3Reload=new ServerCheckCache("v3-priority","xfast",v3Dir);var v3Rows=new[]{S("DE",302),S("US",301)};v3Reload.Bind(v3Rows);
        Check(v3Reload.InitialScanCompleted && v3Reload.NextService==null,"v3 migration does not revive a completed scan");
        Check(v3Rows[1].urls[0].latency==-1 && v3Rows[1].urls[0].LastSuccessfulLatency==401,"v3 migration preserves latest failure and historical success");
        v3Reload.RestartFromFirstCountry();
        Check(CompleteQueue(v3Reload).SequenceEqual(new[]{301}),"v3 migration checks only latest failure with empty completed rows");


        // Bootstrap remains shuffled; each disconnect resets a finite, ordered queue.
        var input=new IVPNService[]{S("US",3),S("DE",2),S("DE",1),S("CA",4),S("AT",5)};
        var queue=new ServerCheckCache("queue","xfast",dir,new Random(17));queue.Bind(input);queue.PersistQueue();
        var bootstrapOrder=new List<int>();
        for(int i=0;i<input.Length;i++){var next=queue.NextService;bootstrapOrder.Add(next.ID);queue.CompleteService(next);}
        Check(bootstrapOrder.Distinct().Count()==input.Length && queue.NextService==null,"bootstrap finishes once without a pre-created periodic round");
        for(int round=0;round<3;round++){
        queue.RestartFromFirstCountry();var seen=new List<int>();
        for(int i=0;i<input.Length;i++){
         var next=queue.NextService;seen.Add(next.ID);
         queue.Bind(input.Reverse());Check(queue.NextService.ID==next.ID,"display reorder cannot change pending turn");
         queue.CompleteService(next);
        }
        Check(seen.SequenceEqual(new[]{5,4,1,2,3}) && queue.NextService==null,"disconnect round covers countries and numbered rows in order exactly once");
        queue.Bind(input);Check(queue.NextService==null,"rebind after completion cannot start another round");
        }
        queue.PersistQueue();
        var queueReload=new ServerCheckCache("queue","xfast",dir,new Random(123));queueReload.Bind(input.Reverse());
        Check(queueReload.NextService==null,"completed round remains idle after app restart");
        queueReload.RestartFromFirstCountry();var oldNext=queueReload.NextService;int nextId=oldNext.ID;
        var replacements=input.Select(s=>(IVPNService)S(s.CountryCode,s.ID)).ToArray();queueReload.Bind(replacements);
        queueReload.CompleteService(oldNext);Check(queueReload.NextService.ID==nextId,"late old-row completion cannot skip replacement");
        var surviving=replacements.Where(s=>s.ID!=nextId).Concat(new[]{S("NL",6)}).ToArray();queueReload.Bind(surviving);
        var reconciled=new List<int>();while(queueReload.NextService!=null){var next=queueReload.NextService;reconciled.Add(next.ID);queueReload.CompleteService(next);}
        Check(!reconciled.Contains(nextId) && reconciled.Distinct().Count()==surviving.Length && reconciled.Contains(6),"active round prunes removed rows and includes new ones without repeating survivors");
        queueReload.Bind(surviving.Concat(new[]{S("FR",7)}));Check(queueReload.NextService==null,"API refresh after completion waits for the next disconnect");

        // Upgrade a completed v2 cache with its automatically created pending rotation.
        var legacyCompleted=new ServerCheckCache("completed-v2","xfast",dir);legacyCompleted.Bind(fresh);legacyCompleted.PersistQueue();
        foreach(var file in Directory.GetFiles(dir,"*.json")){
        var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));old["Version"]=2;old["InitialScanCompleted"]=true;old.Remove("RoundInProgress");old.Remove("SequentialRound");File.WriteAllText(file,old.ToString());}
        var upgraded=new ServerCheckCache("completed-v2","xfast",dir);upgraded.Bind(fresh);
        Check(upgraded.NextService==null,"upgrade discards old periodic queue after completed bootstrap");
        upgraded.RestartFromFirstCountry();Check(upgraded.NextService.ID==1,"upgraded cache starts from first country on explicit disconnect");
        foreach(var file in Directory.GetFiles(dir,"*.json"))File.WriteAllText(file,"broken");
        var corrupt=new ServerCheckCache("bootstrap","xfast",dir);corrupt.Bind(fresh);
        Check(corrupt.NextService!=null && !corrupt.InitialScanCompleted,"corrupt cache starts a fresh scan safely");
    }
}
'''

def run_checks(checks, dotnet):
    with tempfile.TemporaryDirectory(prefix='server-display-fixture-') as directory:
        path = Path(directory)
        (path / 'Program.cs').write_text(stubs + country + background + manual + checks)
        linked = [
            'Services/ServerCheckCache.cs', 'Services/ServerRefreshPlan.cs', 'Services/ServerProbeScheduler.cs',
            'Services/UrlTestCoordinator.cs', 'Services/UrlTestRetryPolicy.cs', 'Services/Libcore/LibcoreMessages.cs',
            'Models/NewService/Url.cs',
        ]
        includes = ''.join(f'<Compile Include="{root / "IRSpeedyVPN" / name}" />' for name in linked)
        (path / 'Checks.csproj').write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit>'
            '<NoWarn>CS0649;CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>' + includes +
            '<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>'
        )
        subprocess.run([dotnet, 'run', '--project', str(path / 'Checks.csproj'), '-v:q'], check=True)


if __name__ == '__main__':
    run_checks(automatic_checks + legacy_checks, sys.argv[1] if len(sys.argv) > 1 else 'dotnet')
