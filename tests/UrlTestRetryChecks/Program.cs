using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;

internal static class Program
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static TestResp Response(params int[] latencies)
    {
        return new TestResp { Results = latencies.Select((latency, index) => new URLTestResp
            { OutboundTag = index.ToString(), LatencyMs = latency }).ToList() };
    }

    private static void Main()
    {
        // The five requested outcomes plus both threshold boundaries.
        foreach (var row in new[] {
            new[] { 600, 630, 600, 2 }, new[] { 600, 400, 400, 2 },
            new[] { 600, -1, 600, 2 }, new[] { -1, 630, 630, 2 },
            new[] { -1, -1, -1, 2 }, new[] { 500, 100, 500, 1 },
            new[] { 499, 100, 499, 1 }, new[] { 501, 100, 100, 2 } })
        {
            int calls = 0;
            var result = UrlTestRetryPolicy.Run(Request(), request => Response(row[calls++]), () => false, _ => { });
            Check(result.Results[0].LatencyMs == row[2] && calls == row[3], "Threshold/minimum regression");
        }
        var same = Request();
        same.Url = "http://CONNECTIVITYCHECK.GSTATIC.COM:80/generate_204";
        int sameCalls = 0;
        UrlTestRetryPolicy.Run(same, _ => { sameCalls++; return Response(600); }, () => false, _ => { });
        Check(sameCalls == 1, "Equivalent URL was retried");
        Check(!UrlTestRetryPolicy.SameEndpoint("https://connectivitycheck.gstatic.com/generate_204",
            UrlTestRetryPolicy.RetryUrl), "HTTP and HTTPS were incorrectly equated");

        var batch = Request();
        batch.OutboundTags = new List<string> { "0", "1", "2" };
        int pass = 0;
        bool finalLogged = false;
        var merged = UrlTestRetryPolicy.Run(batch, request =>
        {
            Check(!finalLogged, "Batch published before retry returned");
            if (++pass == 1) return Response(200, 700, -1);
            Check(request.OutboundTags.SequenceEqual(new[] { "1", "2" }), "Wrong retry subset");
            Check(request.Url == UrlTestRetryPolicy.RetryUrl && request.Config == batch.Config
                && request.XrayConfig == batch.XrayConfig && request.TestTimeoutMs == batch.TestTimeoutMs,
                "Retry changed routing/timeout");
            // Unexpected fast tag and errored positive latency must not affect final results.
            return new TestResp { Results = new List<URLTestResp> {
                new URLTestResp { OutboundTag = "0", LatencyMs = 1 },
                new URLTestResp { OutboundTag = "1", LatencyMs = 50, Error = "failed" },
                new URLTestResp { OutboundTag = "1", LatencyMs = 900 },
                new URLTestResp { OutboundTag = "2", LatencyMs = 300 } } };
        }, () => false, line => { if (line.Contains("stage=final")) finalLogged = true; });
        Check(finalLogged && merged.Results.Select(x => x.LatencyMs).SequenceEqual(new[] { 200, 700, 300 }),
            "Partial result preservation failed");

        int failedPass = 0;
        var survived = UrlTestRetryPolicy.Run(Request(), _ =>
        {
            if (++failedPass == 1) return Response(600);
            throw new TimeoutException();
        }, () => false, _ => { });
        Check(survived.Results[0].LatencyMs == 600, "Second-pass exception erased primary success");
        int emptyPass = 0;
        Check(UrlTestRetryPolicy.Run(Request(), _ => ++emptyPass == 1 ? null : Response(630),
            () => false, _ => { }).Results[0].LatencyMs == 630, "Missing primary result was not retried");
        int timedOutPass = 0;
        Check(UrlTestRetryPolicy.Run(Request(), _ =>
        {
            if (++timedOutPass == 1) throw new TimeoutException();
            return Response(630);
        }, () => false, _ => { }).Results[0].LatencyMs == 630, "Primary timeout was not retried");
        bool configRejected = false;
        foreach (var failure in new Exception[] { new System.IO.IOException(), new System.Net.Sockets.SocketException() })
        {
            int transportPass = 0;
            var recovered = UrlTestRetryPolicy.Run(Request(), _ =>
            {
                if (++transportPass == 1) throw failure;
                return Response(350);
            }, () => false, _ => { });
            Check(transportPass == 2 && recovered.Results[0].LatencyMs == 350,
                "Transport failure skipped the alternate URL");
        }
        int configCalls = 0;
        try
        {
            UrlTestRetryPolicy.Run(Request(), _ =>
            {
                configCalls++;
                throw new InvalidOperationException("core configuration rejected");
            }, () => false, _ => { });
        }
        catch (InvalidOperationException) { configRejected = true; }
        Check(configRejected && configCalls == 1, "Configuration error bypassed candidate isolation");

        bool cancelled = false, rejected = false;
        int cancelledCalls = 0;
        try
        {
            UrlTestRetryPolicy.Run(Request(), _ =>
            {
                cancelledCalls++;
                cancelled = true;
                return Response(600);
            }, () => cancelled, _ => { });
        }
        catch (OperationCanceledException) { rejected = true; }
        Check(rejected && cancelledCalls == 1, "Cancelled batch issued retry or published results");
        CheckProgress();
        CheckCountryRounds();
        CheckParallelCountries();
        Console.WriteLine("URL test retry, progress and country scheduling checks passed.");
    }

    private static void CheckCountryRounds()
    {
        var order = new List<string>();
        var updates = new List<long>();
        Func<string, int[], InitialCountryTest> country = (name, latencies) =>
            new InitialCountryTest(latencies.Select((latency, index) =>
                new Action<int, Action<long>>((slot, report) =>
                {
                    order.Add(name + (index + 1));
                    report(latency);
                })), latency => { if (name == "A") updates.Add(latency); },
                () => order.Add(name + "-done"));
        InitialUrlTestSchedule.Run(new[] {
            country("A", new[] { 600, 700, 400 }),
            country("B", new[] { 300 }),
            country("C", new[] { -1, 500 })
        }, () => false, ex => { throw ex; }, maxConcurrency: 1);
        Check(order.SequenceEqual(new[] {
            "A1", "B1", "B-done", "C1", "A2", "C2", "C-done", "A3", "A-done"
        }), "Countries did not follow stable rounds or completed before their last member");
        Check(updates.SequenceEqual(new long[] { 600, 400 }), "Country minimum was reset between rounds");

        order.Clear();
        int pass = 0;
        var withRetry = new InitialCountryTest(new Action<int, Action<long>>[] {
            (slot, report) => UrlTestRetryPolicy.Run(Request(), (req, partial) =>
            {
                order.Add(++pass == 1 ? "A-primary" : "A-retry");
                return Response(pass == 1 ? 600 : 400);
            }, () => false, _ => { }, report)
        }, _ => { }, () => order.Add("A-done"));
        InitialUrlTestSchedule.Run(new[] { withRetry, country("B", new[] { 200 }) },
            () => false, ex => { throw ex; }, maxConcurrency: 1);
        Check(order.SequenceEqual(new[] { "A-primary", "A-retry", "A-done", "B1", "B-done" }),
            "Next country started before the current server retry finished");

        order.Clear();
        int failures = 0;
        var invalid = new InitialCountryTest(new Action<int, Action<long>>[] {
            (slot, report) => { order.Add("invalid"); throw new InvalidOperationException(); }
        }, _ => { }, () => order.Add("invalid-done"));
        InitialUrlTestSchedule.Run(new[] { invalid, country("B", new[] { 200 }) },
            () => false, _ => failures++, maxConcurrency: 1);
        Check(failures == 1 && order.Contains("B-done"), "One invalid member stopped the schedule");

        bool cancelled = false;
        order.Clear();
        var stopping = new InitialCountryTest(new Action<int, Action<long>>[] {
            (slot, report) => { cancelled = true; report(100); }
        }, _ => order.Add("late-progress"), () => order.Add("late-completion"));
        InitialUrlTestSchedule.Run(new[] { stopping, country("B", new[] { 200 }) },
            () => cancelled, ex => { throw ex; }, maxConcurrency: 1);
        Check(order.Count == 0, "Cancellation allowed progress, completion or another country");
    }

    private static void CheckParallelCountries()
    {
        var entered = Enumerable.Range(0, 6).Select(_ => new ManualResetEventSlim()).ToArray();
        var release = Enumerable.Range(0, 6).Select(_ => new ManualResetEventSlim()).ToArray();
        int active = 0, peak = 0, secondRound = 0, completed = 0, cleanup = 0;
        var perCountry = new int[6];
        var slotBusy = new int[5];
        var countries = Enumerable.Range(0, 6).Select(country => new InitialCountryTest(
            Enumerable.Range(0, 2).Select(round => new Action<int, Action<long>>((slot, report) =>
            {
                Check(Interlocked.Increment(ref perCountry[country]) == 1, "Same country overlapped");
                Check(Interlocked.Increment(ref slotBusy[slot]) == 1, "Core slot overlapped");
                int now = Interlocked.Increment(ref active);
                int previous;
                do { previous = Volatile.Read(ref peak); }
                while (now > previous && Interlocked.CompareExchange(ref peak, now, previous) != previous);
                try
                {
                    if (round == 0)
                    {
                        entered[country].Set();
                        Check(release[country].Wait(5000), "First-round gate timed out");
                        report(600);
                    }
                    else
                    {
                        Check(release.All(gate => gate.IsSet), "Next round started before all first members finished");
                        Interlocked.Increment(ref secondRound);
                        report(400);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                    Interlocked.Decrement(ref perCountry[country]);
                    Interlocked.Decrement(ref slotBusy[slot]);
                }
            })), _ => { }, () => Interlocked.Increment(ref completed))).ToArray();
        var job = Task.Run(() => InitialUrlTestSchedule.Run(countries, () => false,
            ex => { throw ex; }, () => Interlocked.Increment(ref cleanup)));
        try
        {
            Check(entered.Take(5).All(gate => gate.Wait(5000)), "Five countries did not start concurrently");
            Check(!entered[5].IsSet && Volatile.Read(ref active) == 5, "Concurrency limit was not five");
            release[0].Set();
            Check(entered[5].Wait(5000), "Freed slot did not refill with the sixth country");
            Check(Volatile.Read(ref secondRound) == 0, "Round barrier was skipped");
        }
        finally
        {
            foreach (var gate in release) gate.Set();
            Check(job.Wait(5000), "Parallel test did not drain");
            foreach (var gate in entered.Concat(release)) gate.Dispose();
        }
        Check(peak == 5 && active == 0 && secondRound == 6 && completed == 6 && cleanup == 1,
            "Parallel scheduling/completion/cleanup regression");

        using (var fiveStarted = new CountdownEvent(5))
        using (var unblock = new ManualResetEventSlim())
        {
            int cancelled = 0, started = 0, late = 0, cleaned = 0;
            var stopping = Enumerable.Range(0, 8).Select(_ => new InitialCountryTest(
                new Action<int, Action<long>>[] { (slot, report) =>
                {
                    Interlocked.Increment(ref started);
                    fiveStarted.Signal();
                    Check(unblock.Wait(5000), "Cancellation gate timed out");
                    report(100);
                } }, _latency => Interlocked.Increment(ref late),
                () => Interlocked.Increment(ref late))).ToArray();
            var stopJob = Task.Run(() => InitialUrlTestSchedule.Run(stopping,
                () => Volatile.Read(ref cancelled) != 0, ex => { throw ex; },
                () => Interlocked.Increment(ref cleaned)));
            try { Check(fiveStarted.Wait(5000), "Cancellation test did not start five workers"); }
            finally
            {
                Interlocked.Exchange(ref cancelled, 1);
                unblock.Set();
                Check(stopJob.Wait(5000), "Cancelled workers did not drain");
            }
            Check(started == 5 && late == 0 && cleaned == 1, "Cancellation started queued work or published results");
        }
    }

    private static void CheckProgress()
    {
        var updates = new List<long>();
        bool complete = false;
        int pass = 0;
        var request = Request();
        request.OutboundTags = new List<string> { "0", "1" };
        var result = UrlTestRetryPolicy.Run(request, (req, report) =>
        {
            Check(!complete, "Country completed before test returned");
            if (++pass == 1)
            {
                report(Response(800, -1));
                Check(updates.SequenceEqual(new long[] { 800 }), "First success was not published immediately");
                report(Response(800, 600));
                report(Response(900, 650));
                Check(updates.SequenceEqual(new long[] { 800, 600 }), "Slower/duplicate progress replaced the best");
                // A query snapshot from a previous country must never change this row.
                report(new TestResp { Results = new List<URLTestResp> {
                    new URLTestResp { OutboundTag = "previous-country", LatencyMs = 1 } } });
                return Response(800, 600);
            }
            report(Response(700, 400));
            Check(updates.SequenceEqual(new long[] { 800, 600, 400 }), "Retry improvement was not published");
            throw new TimeoutException();
        }, () => false, line => { if (line.Contains("stage=final")) complete = true; }, updates.Add);
        Check(complete && result.Results.Select(x => x.LatencyMs).SequenceEqual(new[] { 700, 400 }),
            "Successful progress was lost after final RPC failure");

        bool cancelled = false, rejected = false;
        int calls = 0;
        updates.Clear();
        try
        {
            UrlTestRetryPolicy.Run(Request(), (req, report) =>
            {
                calls++;
                report(Response(600));
                cancelled = true;
                report(Response(400));
                return Response(400);
            }, () => cancelled, _ => { }, updates.Add);
        }
        catch (OperationCanceledException) { rejected = true; }
        Check(rejected && calls == 1 && updates.SequenceEqual(new long[] { 600 }),
            "Cancelled country published progress or retried");
    }

    private static TestReq Request() => new TestReq
    {
        Url = "https://example.invalid/probe", Config = "test-config", XrayConfig = "test-xray",
        OutboundTags = new List<string> { "0" }, NeedXray = true, MaxConcurrency = 15, TestTimeoutMs = 5000
    };
}
