using IRSpeedyVPN.Services;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Components.ServerListControl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private const string Primary = "https://example.invalid/check";
    private const string Fallback = "http://connectivitycheck.gstatic.com/generate_204";

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }

    private static Row MakeRow(int id, int members = 12)
    {
        var row = new Row { ID = id };
        for (int i = 1; i <= members; i++) row.Members.Add(new Url
        { id = i, url = "vless://private-fixture-" + id + "-" + i, extra_field_1 = "private-parameter" });
        return row;
    }

    private static async Task RunCapped(int count, int cap)
    {
        var rows = Enumerable.Range(1, count).Select(i => MakeRow(i)).ToArray();
        var started = new List<(ServerRefreshPlan.Candidate Candidate, TaskCompletionSource<DisplayProbeResult> Reply)>();
        var outcomes = new List<(IVPNService Row, ServerRefreshPlan.Outcome Result)>();
        var firstWin = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scan = new ServerRefreshPlan(rows, new Random(7)).RunAsync((member, _) =>
        {
            var reply = new TaskCompletionSource<DisplayProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (started) started.Add((member, reply));
            return reply.Task;
        }, (row, result) => outcomes.Add((row, result)), CancellationToken.None,
            (row, ping) => { if (ReferenceEquals(row, rows[0]) && ping == 299) firstWin.TrySetResult(true); });
        lock (started)
        {
            Check(started.Count == 8 && started.GroupBy(x => x.Candidate.Service).All(g => g.Count() <= cap),
                count + " active countries admit at most " + cap + " probes per country and 8 in total");
            if (count == 2)
            {
                started.First(x => ReferenceEquals(x.Candidate.Service, rows[0])).Reply.SetResult(new DisplayProbeResult(299));
            }
            else foreach (var job in started) job.Reply.SetResult(new DisplayProbeResult(299));
        }
        if (count == 2)
        {
            // Hold other completions until the early result is observed.
            await firstWin.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lock (started)
            {
                var same = started.Where(x => ReferenceEquals(x.Candidate.Service, rows[0])).ToArray();
                same[1].Reply.SetResult(new DisplayProbeResult(140));
                same[2].Reply.SetResult(DisplayProbeResult.Failed);
                same[3].Reply.SetResult(new DisplayProbeResult(301));
            }
        }
        int released = 8;
        var limit = System.Diagnostics.Stopwatch.StartNew();
        while (!scan.IsCompleted && limit.Elapsed < TimeSpan.FromSeconds(5))
        {
            lock (started)
            {
                if (started.Count(x => !x.Reply.Task.IsCompleted) > 8)
                    throw new InvalidOperationException("subsequent waves exceed global capacity");
                foreach (var job in started.Skip(count == 2 ? 0 : released)) job.Reply.TrySetResult(new DisplayProbeResult(299));
                released = started.Count;
            }
            await Task.WhenAny(scan, Task.Delay(1));
        }
        await scan.WaitAsync(TimeSpan.FromSeconds(5));
        Check(outcomes.Count == count, "each country publishes exactly once");
        if (count == 2)
        {
            var result = outcomes.Single(x => ReferenceEquals(x.Row, rows[0])).Result;
            Check(started.Count(x => ReferenceEquals(x.Candidate.Service, rows[0])) == 4 && result.Latency == 140
                && result.Failed.Count == 1, "299 skips waiting routes, keeps all four in-flight results and counts only the measured failure");
        }
    }

    private static async Task BoundariesAndOrdering()
    {
        foreach (int ping in new[] { 299, 300 })
        {
            var row = MakeRow(1, 11);
            int total = 0;
            await new ServerRefreshPlan(new[] { row }, new Random(72)).RunAsync((_, __) =>
            { total++; return Task.FromResult(new DisplayProbeResult(ping)); }, (_, __) => { }, CancellationToken.None);
            Check(ping == 299 ? total < 11 : total == 11, "early finish boundary " + ping + " ms");
        }
        var rows = Enumerable.Range(1, 12).Select(i => MakeRow(i, 2)).ToArray();
        bool Failed(IVPNService service, Url member) => member.id == 2;
        async Task<string[]> Sequence(int seed)
        {
            var order = new List<string>();
            await new ServerRefreshPlan(rows, new Random(seed), Failed).RunAsync((member, _) =>
            { order.Add(member.Probe.url); return Task.FromResult(new DisplayProbeResult(300)); }, (_, __) => { }, CancellationToken.None);
            return order.ToArray();
        }
        var first = await Sequence(19);
        Check(first.SequenceEqual(await Sequence(19)) && !first.SequenceEqual(await Sequence(20))
            && rows.All(r => Array.IndexOf(first, r.Members[1].url) < Array.IndexOf(first, r.Members[0].url)),
            "seeded shuffle is deterministic and previously failed members start first in each country");

        var winnerRow = MakeRow(77, 6);
        var values = new[] { new DisplayProbeResult(301), new DisplayProbeResult(300, 1, 7),
            new DisplayProbeResult(300, 0, 1), new DisplayProbeResult(300, 0, 3),
            new DisplayProbeResult(300, 0, 3), DisplayProbeResult.Failed };
        await new ServerRefreshPlan(new[] { winnerRow }, new Random(19)).RunAsync((member, _) =>
            Task.FromResult(values[member.Probe.id - 1]), (_, result) =>
            Check(result.Winner.id == 4 && result.Latency == 300, "winner compares ping, failures, samples and ID"), CancellationToken.None);
    }

    private static async Task Deferrals()
    {
        var row = MakeRow(1, 1);
        var times = new List<long>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await new ServerRefreshPlan(new[] { row }, new Random(3)).RunAsync((_, __) =>
        {
            times.Add(clock.ElapsedMilliseconds);
            if (times.Count == 1) return Task.FromResult(DisplayProbeResult.Deferred);
            if (times.Count == 2) return Task.FromCanceled<DisplayProbeResult>(new CancellationToken(true));
            return Task.FromResult(new DisplayProbeResult(123));
        }, (_, result) => Check(result.Latency == 123 && result.Failed.Count == 0, "capacity and attempt cancellation produce no failure"), CancellationToken.None);
        Check(times.Count == 3 && times.Zip(times.Skip(1), (a, b) => b - a).All(t => t >= 249), "deferred attempts wait 250 ms before requeue");
        using var cancellation = new CancellationTokenSource();
        var task = new ServerRefreshPlan(new[] { row }).RunAsync(async (_, token) =>
        { await Task.Delay(Timeout.Infinite, token); return DisplayProbeResult.Failed; }, (_, __) =>
        throw new Exception("globally canceled probe published a result"), cancellation.Token);
        cancellation.Cancel();
        try { await task; throw new Exception("canceled round resumed"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS CancelAll-equivalent token stops round without another probe"); }
    }

    private static void Deadlines()
    {
        void Probe(string url, long initial, int advance, DisplayProbeResult a, DisplayProbeResult b,
            long expected, (string, int)[] callsExpected)
        {
            long elapsed = initial;
            var calls = new List<(string, int)>();
            var result = UrlTestRetryPolicy.RunDisplay(url, (target, timeout) =>
            { calls.Add((target, timeout)); if (calls.Count == 1) { elapsed += advance; return a; } return b; },
                () => elapsed, CancellationToken.None);
            Check(calls.SequenceEqual(callsExpected) && result.Latency == expected,
                "budget and fallback: " + url + " " + a.Latency + " -> " + expected + " at initial " + initial);
        }
        Probe(Primary, 0, 500, new DisplayProbeResult(500), DisplayProbeResult.Failed, 500, new[] { (Primary, 4000) });
        Probe(Primary, 0, 4000, new DisplayProbeResult(501), new DisplayProbeResult(281), 281, new[] { (Primary, 4000), (Fallback, 4000) });
        Probe(Primary, 3000, 4000, DisplayProbeResult.Failed, new DisplayProbeResult(150), 150, new[] { (Primary, 4000), (Fallback, 1000) });
        Probe(Primary, 7800, 200, DisplayProbeResult.Failed, new DisplayProbeResult(5), -1, new[] { (Primary, 200) });
        Probe(Primary, 10, 510, new DisplayProbeResult(501), DisplayProbeResult.Failed, 501, new[] { (Primary, 4000), (Fallback, 7480) });
        Probe(Primary, 0, 5, DisplayProbeResult.Deferred, new DisplayProbeResult(10), 0, new[] { (Primary, 4000) });
        Probe(null, 0, 0, DisplayProbeResult.Failed, new DisplayProbeResult(10), -1, new[] { (Fallback, 8000) });
        Probe(Fallback, 0, 0, new DisplayProbeResult(600), new DisplayProbeResult(10), 600, new[] { (Fallback, 8000) });
        Probe(Primary, 0, 4001, new DisplayProbeResult(100), new DisplayProbeResult(281), 281,
            new[] { (Primary, 4000), (Fallback, 3999) });
        int remaining = 175;
        var waits = new List<int>();
        Check(!UrlTestRetryPolicy.WaitForDisplayListener(() => remaining, ms =>
        { waits.Add(ms); remaining -= ms; return false; }, null, CancellationToken.None)
            && waits.SequenceEqual(new[] { 100, 75 }),
            "Core and SNI listener startup share the attempt deadline, including the final partial wait");
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        try
        {
            UrlTestRetryPolicy.WaitForDisplayListener(() => 8000, ms =>
                throw new Exception("Canceled startup must not connect"), null, stopped.Token);
            throw new Exception("Canceled startup continued");
        }
        catch (OperationCanceledException) { Console.WriteLine("PASS canceled startup makes no listener attempt"); }
        string split = "vless://example.invalid?path=%2F&TYPE=Split%48ttp&extra=%7B%22x%22%3A1%7D#type=splithttp";
        Check(UrlTestRetryPolicy.NormalizeDisplayTransport(split) ==
            "vless://example.invalid?path=%2F&type=xhttp&extra=%7B%22x%22%3A1%7D#type=splithttp",
            "SplitHTTP probe normalizes only the transport, retaining all other URI fields");
        Check(UrlTestRetryPolicy.NormalizeDisplayTransport("vless://example.invalid?type=ws#fixture") ==
            "vless://example.invalid?type=ws#fixture", "ordinary transports keep their connection options");
    }

    private static void Cache()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var incomplete = MakeRow(8, 2);
            var cache = new ServerCheckCache("account", "VPN", path);
            cache.BindDisplay(new[] { incomplete }, Primary);
            cache.RememberDisplayProgress(incomplete, incomplete.Members[0], 640);
            cache.PersistQueue();
            incomplete = MakeRow(8, 2);
            cache = new ServerCheckCache("account", "VPN", path);
            cache.BindDisplay(new[] { incomplete }, Primary);
            Check(cache.DisplayPendingServices.Single() == incomplete && incomplete.Members[0].latency == 640
                 && incomplete.Members[1].latency == 0, "cancellation retains a real partial ping but does not complete the country");

            var row = MakeRow(1, 3);
            cache.BindDisplay(new[] { row }, Primary);
            cache.CompleteDisplayRow(row, row.Members[1], 153, new[] { row.Members[0] });
            var restored = MakeRow(1, 3);
            cache = new ServerCheckCache("account", "VPN", path);
            cache.BindDisplay(new[] { restored }, Primary);
            Check(cache.DisplayPendingServices.Length == 0 && restored.Members.Select(x => x.latency).SequenceEqual(new long[] { -1, 153, 0 }),
                "cache restores winner, measured failures, and unknown skipped members");
            restored.Members.Reverse();
            cache.BindDisplay(new[] { restored }, Primary);
            Check(cache.DisplayPendingServices.Length == 0, "API roster reorder retains winner");
            restored.Members.Add(new Url { id = 4, url = "vless://new-private" });
            cache.BindDisplay(new[] { restored }, Primary);
            Check(cache.DisplayPendingServices.Single() == restored && restored.Members.All(x => x.latency == 0), "roster change invalidates even an unchanged winner");
            cache.CompleteDisplayRow(restored, restored.Members[1], 90, Array.Empty<Url>());
            restored.Members[0].extra_field_1 = "changed-connection-option";
            cache.BindDisplay(new[] { restored }, Primary);
            Check(cache.DisplayPendingServices.Single() == restored && restored.Members.All(x => x.latency == 0), "change to a sibling's config invalidates the country");
            cache.CompleteDisplayRow(restored, restored.Members[1], 90, Array.Empty<Url>());
            cache.BindDisplay(new[] { restored }, Fallback);
            Check(cache.DisplayPendingServices.Single() == restored && restored.Members.All(x => x.latency == 0), "test URL invalidates cached results");
            cache.CompleteDisplayRow(restored, null, -1, restored.Members);
            var other = new ServerCheckCache("other", "VPN", path);
            other.BindDisplay(new[] { restored }, Fallback);
            Check(other.DisplayPendingServices.Single() == restored && restored.Members.All(x => x.latency == 0), "account isolation");
            cache.BindDisplay(new[] { restored }, Fallback);
            Check(cache.DisplayPendingServices.Single() == restored && restored.Members.All(x => cache.WasDisplayFailure(restored, x)), "completed no-result country queues again with measured failures first");
            Check(Directory.GetFiles(path).All(f => !File.ReadAllText(f).Contains("private") && !File.ReadAllText(f).Contains(Primary)), "cache contains hashes only, no credentials or test URL");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private static async Task Main()
    {
        await RunCapped(9, 1);
        await RunCapped(2, 4);
        await BoundariesAndOrdering();
        await Deferrals();
        Deadlines();
        Cache();
    }
}
