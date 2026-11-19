using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    private static List<ServerRefreshPlan.Country> Plan(params TestRow[] rows)
        => ServerRefreshPlan.Countries(rows, (_, __) => false, new Random(17));

    private static async Task Main()
    {
        await Limits(8, 1);
        await Limits(9, 1);
        await Limits(2, 4);
        await LimitsExpandAfterRowsFinish();
        await Satisfied(299, true);
        await Satisfied(300, false);
        await InFlightResultsAreKept();
        await DeferredDoesNotMeasure();
        await CancellationDrainsAndDoesNotPublish();
        ProbeBudgets();
        OrderAndCache();
    }

    private static async Task Limits(int countries, int perCountry)
    {
        var rows = Enumerable.Range(1, countries).Select(i => new TestRow(i, 12)).ToArray();
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = new Dictionary<IVPNService, int>();
        int active = 0, max = 0;
        var round = ServerProbeScheduler.RunAsync(Plan(rows), async (candidate, token) =>
        {
            var row = candidate.Owners.Single().Service;
            lock (admitted)
            {
                admitted.TryGetValue(row, out int count);
                admitted[row] = count + 1;
                max = Math.Max(max, ++active);
            }
            await stop.Task;
            lock (admitted) active--;
            return new ServerProbeMeasurement(299);
        }, (row, winner, measured) => { }, CancellationToken.None);
        Check(admitted.Count == Math.Min(countries, 8) && admitted.Values.All(n => n == perCountry) && max == 8,
            $"{countries} active countries admit {perCountry} per row and at most eight overall");
        stop.SetResult(true);
        await round;
        Check(max == 8 && active == 0, "in-flight slots drain without exceeding eight");
    }

    private static async Task Satisfied(int ping, bool skip)
    {
        var row = new TestRow(1, 5);
        int calls = 0, completed = 0;
        await ServerProbeScheduler.RunAsync(Plan(row), (_, __) =>
        {
            calls++;
            return Task.FromResult(new ServerProbeMeasurement(ping));
        }, (service, winner, results) =>
        {
            completed++;
            Check(winner != null && results.Count == (skip ? 1 : 5) && results.Values.All(ms => ms == ping),
                $"{ping} ms {(skip ? "skips queued probes without failure records" : "does not satisfy the row early")}");
        }, CancellationToken.None);
        Check(calls == (skip ? 1 : 5) && completed == 1, "only admitted work runs and the row publishes once");
    }

    private static async Task LimitsExpandAfterRowsFinish()
    {
        var rows = Enumerable.Range(1, 8).Select(i => new TestRow(i, 12)).ToArray();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expanding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expanded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var countsByRow = new int[3];
        int holdCount = 0;
        var test = ServerProbeScheduler.RunAsync(Plan(rows), async (candidate, token) =>
        {
            int id = candidate.Owners[0].Service.ID;
            if (id > 2) await finished.Task;
            else
            {
                Interlocked.Increment(ref countsByRow[id]);
                if (Interlocked.Increment(ref holdCount) == 8) expanded.SetResult(true);
                await expanding.Task;
            }
            return new ServerProbeMeasurement(200);
        }, (row, winner, results) => { }, CancellationToken.None);
        Check(holdCount == 2, "each of the last two countries begins with one slot while eight are active");
        finished.SetResult(true);
        await expanded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(countsByRow[1] == 4 && countsByRow[2] == 4, "the remaining two countries expand to four slots each as the others finish");
        expanding.SetResult(true);
        await test;
    }

    private static async Task InFlightResultsAreKept()
    {
        var row = new TestRow(1, 12);
        var plan = Plan(row);
        var first = plan.Single().Pending[0];
        var later = plan.Single().Pending[1];
        var fast = new TaskCompletionSource<ServerProbeMeasurement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, published = 0;
        var round = ServerProbeScheduler.RunAsync(plan, async (candidate, token) =>
        {
            calls++;
            if (candidate == first) return await fast.Task;
            await remaining.Task;
            return new ServerProbeMeasurement(candidate == later ? 150 : 400);
        }, (service, winner, results) =>
        {
            published++;
            Check(winner == later.Owners[0].Url && results.Count == 8 && results.Values.All(v => v > 0),
                "already running probes still compete for the winner after an early success");
        }, CancellationToken.None);
        fast.SetResult(new ServerProbeMeasurement(299));
        Check(SpinWait.SpinUntil(() => plan[0].Pending.Count == 0, TimeSpan.FromSeconds(2)),
            "the first fast result clears pending work while the rest are still running");
        remaining.SetResult(true);
        await round;
        Check(calls == 8 && published == 1, "country completion waits for the original eight workers");
    }

    private static async Task DeferredDoesNotMeasure()
    {
        int calls = 0, backoffs = 0;
        await ServerProbeScheduler.RunAsync(Plan(new TestRow(1, 1)), (_, __) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(default(ServerProbeMeasurement));
            if (calls == 2) throw new OperationCanceledException();
            return Task.FromResult(new ServerProbeMeasurement(500));
        }, (row, winner, results) =>
        {
            Check(results.Count == 1 && results[winner] == 500 && calls == 3 && backoffs == 2,
                "capacity and local cancellation do not publish and their candidate retries");
        }, CancellationToken.None, (delay, token) =>
        {
            Check(delay == 250, "unmeasured candidates wait 250 ms");
            backoffs++;
            return Task.CompletedTask;
        });
    }

    private static async Task CancellationDrainsAndDoesNotPublish()
    {
        UrlTestCoordinator.BeginBatch();
        var token = UrlTestCoordinator.DisplayCancellation;
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int published = 0;
        var round = ServerProbeScheduler.RunAsync(Plan(new TestRow(1, 1)), async (candidate, c) =>
        {
            entered.SetResult(true);
            try { await Task.Delay(Timeout.Infinite, c); }
            catch (OperationCanceledException) { await cleanup.Task; throw; }
            return new ServerProbeMeasurement(10);
        }, (row, winner, results) => published++, token);
        await entered.Task;
        UrlTestCoordinator.CancelAll();
        UrlTestCoordinator.BeginBatch();
        Check(token.IsCancellationRequested && !UrlTestCoordinator.DisplayCancellation.IsCancellationRequested
            && !round.IsCompleted, "CancelAll retires the old round and holds its slot through cleanup");
        cleanup.SetResult(true);
        try { await round; throw new Exception("The canceled scan returned successfully."); }
        catch (OperationCanceledException) { }
        Check(published == 0, "global cancellation leaves no country result");
    }

    private static void ProbeBudgets()
    {
        const string primary = "https://example.invalid/test";
        var calls = new List<(string Target, int Timeout)>();
        ServerProbeMeasurement Measure(ServerProbeMeasurement a, ServerProbeMeasurement b, long startup = 500)
        {
            calls.Clear();
            long elapsed = startup;
            return UrlTestRetryPolicy.RunDisplay(primary, (url, ms) =>
            {
                calls.Add((url, ms));
                elapsed += ms;
                return calls.Count == 1 ? a : b;
            }, () => elapsed, CancellationToken.None);
        }
        Check(Measure(new ServerProbeMeasurement(500), new ServerProbeMeasurement(10)).Latency == 500
            && calls.SequenceEqual(new[] { (primary, 4000) }), "500 ms is accepted without fallback and primary is capped at four seconds");
        Check(Measure(new ServerProbeMeasurement(501), new ServerProbeMeasurement(200)).Latency == 200
            && calls.SequenceEqual(new[] { (primary, 4000), (UrlTestRetryPolicy.RetryUrl, 3500) }),
            "501 ms retries and startup plus both attempts share eight seconds");
        Check(Measure(ServerProbeMeasurement.Failed, new ServerProbeMeasurement(230)).Latency == 230
            && calls.Count == 2, "a failed primary can be recovered by fallback");
        Check(Measure(new ServerProbeMeasurement(600), ServerProbeMeasurement.Failed).Latency == 600
            && calls.Count == 2, "fallback failure preserves the first success");
        Check(Measure(new ServerProbeMeasurement(600), new ServerProbeMeasurement(700)).Latency == 600
            && calls.Count == 2, "the faster successful result wins");
        Check(Measure(ServerProbeMeasurement.Failed, new ServerProbeMeasurement(200), 6000).Latency < 0
            && calls.SequenceEqual(new[] { (primary, 2000) }), "startup can leave a shorter primary budget with no remaining fallback time");
        int direct = 0;
        UrlTestRetryPolicy.RunDisplay("", (target, ms) =>
        {
            Check(target == UrlTestRetryPolicy.RetryUrl && ms == 8000, "without API URL the fallback is the first and only attempt");
            direct++;
            return ServerProbeMeasurement.Failed;
        }, () => 0, CancellationToken.None);
        Check(direct == 1, "the same target is never retried");
    }

    private static void OrderAndCache()
    {
        var seed = new Random(22);
        var row = new TestRow(20, 7);
        var members = row.GetServerUrls();
        var plan = ServerRefreshPlan.Countries(new[] { row }, (_, u) => u.id == 2 || u.id == 5, seed);
        var ordered = plan.Single().Pending;
        var same = ServerRefreshPlan.Countries(new[] { row }, (_, u) => u.id == 2 || u.id == 5, new Random(22));
        Check(ordered.Take(2).Select(c => c.Probe.id).OrderBy(id => id).SequenceEqual(new[] { 2, 5 })
            && ordered.Select(c => c.Probe.id).SequenceEqual(same.Single().Pending.Select(c => c.Probe.id))
            && !ordered.Select(c => c.Probe.id).SequenceEqual(members.Select(u => u.id)),
            "failed-before-unmeasured admission is seeded and independent of display order");

        var first = ordered.Single(c => c.Probe.id == 1);
        var second = ordered.Single(c => c.Probe.id == 2);
        int Compare(ServerProbeMeasurement a, ServerProbeMeasurement b) => ServerRefreshPlan.Compare(first, a, second, b);
        Check(Compare(new ServerProbeMeasurement(200, 1), new ServerProbeMeasurement(201)) < 0
            && Compare(new ServerProbeMeasurement(200, 2), new ServerProbeMeasurement(200, 1)) > 0
            && Compare(new ServerProbeMeasurement(200, 0, 2), new ServerProbeMeasurement(200, 0, 1)) < 0
            && Compare(new ServerProbeMeasurement(200), new ServerProbeMeasurement(200)) < 0,
            "winner ties break by failures, samples, then ID after ping");
        Check(ServerListRanking.Order(new[] { "bravo", "Alpha", "zulu" }, _ => false, s => s == "zulu" ? 10 : 20,
            s => s, _ => 0).SequenceEqual(new[] { "zulu", "Alpha", "bravo" }), "row ranking uses ping and then country title");

        string dir = Path.Combine(Path.GetTempPath(), "display-probe-" + Guid.NewGuid().ToString("N"));
        const string target = "https://example.invalid/ping";
        try
        {
            var failed = new TestRow(21, 2);
            var cache = new ServerCheckCache("fixture-account", "vpn", dir, testUrl: target);
            cache.Bind(new[] { row, failed });
            cache.RestartForLogin();
            var picked = members[2];
            Check(cache.CommitDisplayRow(row, picked, new Dictionary<Url, long> { [members[1]] = -1, [picked] = 230 })
                && cache.CommitDisplayRow(failed, null, failed.GetServerUrls().ToDictionary(u => u, u => -1L)),
                "successful and failed country snapshots commit separately");

            var winnerReloaded = new TestRow(20, 7);
            var failedReloaded = new TestRow(21, 2);
            var reopened = new ServerCheckCache("fixture-account", "vpn", dir, testUrl: target);
            reopened.Bind(new[] { winnerReloaded, failedReloaded });
            reopened.RestartForLogin();
            var hydrated = winnerReloaded.GetServerUrls();
            Check(reopened.PendingServices.SequenceEqual(new[] { failedReloaded })
                && hydrated[2].latency == 230 && hydrated[1].latency == -1 && hydrated[0].latency == 0
                && !reopened.FailedLastDisplayRound(winnerReloaded, hydrated[0])
                && reopened.FailedLastDisplayRound(winnerReloaded, hydrated[1]),
                "a new cycle keeps the winner, requeues failed countries and distinguishes skipped servers");

            foreach (var changed in new[] { "account", "test URL", "config", "roster" })
            {
                var duplicate = new TestRow(20, changed == "roster" ? 6 : 7);
                if (changed == "config") duplicate.GetServerUrls()[0].extra_field_1 = "changed-chain";
                var scope = new ServerCheckCache(changed == "account" ? "another" : "fixture-account", "vpn", dir,
                    testUrl: changed == "test URL" ? "http://example.invalid/other" : target);
                scope.Bind(new[] { duplicate });
                scope.RestartForLogin();
                Check(scope.PendingServices.Length == 1 && duplicate.GetServerUrls().All(u => u.latency == 0),
                    "changing " + changed + " invalidates the country's cached ping");
            }
            var restoredRoster = new TestRow(20, 7);
            var changedBack = new ServerCheckCache("fixture-account", "vpn", dir, testUrl: target);
            changedBack.Bind(new[] { restoredRoster });
            Check(changedBack.PendingServices.Single() == restoredRoster && restoredRoster.GetServerUrls().All(u => u.latency == 0)
                && changedBack.FailedLastDisplayRound(restoredRoster, restoredRoster.GetServerUrls()[1]),
                "changing back to an older roster preserves failed priority without reviving its old ping");
            Check(Directory.GetFiles(dir).All(file => !File.ReadAllText(file).Contains("example.invalid")
                && !File.ReadAllText(file).Contains("fixture-account")), "only fingerprints identify accounts, targets and server configs on disk");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
