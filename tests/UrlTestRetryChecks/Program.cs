using System;
using System.Collections.Generic;
using System.Linq;
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
        Console.WriteLine("URL test retry and progress checks passed.");
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
