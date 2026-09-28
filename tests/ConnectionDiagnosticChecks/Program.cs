using System;
using System.Collections.Concurrent;
using System.Linq;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;

internal static class Program
{
    private static void Main()
    {
        string safe;
        const string core = "[CoreDiagnostic] schema=core-network-v2 event=hy2-reset pid=7 seq=1 box=2 reason=interface-update";
        Require(CoreDiagnosticMetadata.TryParse(core, out safe) && safe.Contains("reason=interface-update"), "Structured Core reset metadata missing.");
        Require(!CoreDiagnosticMetadata.TryParse(core + " password=PRIVATE_VALUE", out safe), "Unknown sensitive field accepted.");
        Require(!CoreDiagnosticMetadata.TryParse(core + " tag=private.example", out safe), "Raw tag accepted.");
        Require(!CoreDiagnosticMetadata.TryParse(core + " reason=power-event", out safe), "Duplicate field accepted.");
        Require(!CoreDiagnosticMetadata.TryParse(core + " index=1\n", out safe), "Control characters accepted in numeric metadata.");
        Require(CoreDiagnosticMetadata.Hash("proxy") == "1241936d4dd3aad6", "Go/C# correlation mismatch.");
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            CoreDiagnosticMetadata.Register(19810, process);
            Require(CoreDiagnosticMetadata.Pid(19810) == process.Id, "Shared Core PID unavailable to another service.");
        }
        var lines = new ConcurrentQueue<string>();
        LogHelper.Sink = lines.Enqueue;
        TunnelPlusService.Exercise();
        ConnectionDiagnostics.Flush();
        var output = string.Join("\n", lines.ToArray());
        Require(output.Contains("selectedMode=Proxy") && output.Contains("appliedMode=TUN"), "Selected/applied mode must be distinguished.");
        Require(output.Contains("appliedMode=Proxy"), "Established proxy mode must be explicit.");
        Require(output.Contains("appliedMode=not-connected"), "Do not label an idle service as connected.");
        Require(output.Contains("schema=network-core-v2") && output.Contains("appMvid="), "Build/session identification missing.");
        Require(output.Contains("category=network-changed"), "Core signal missing.");
        Require(!output.Contains("PRIVATE_VALUE") && !output.Contains("example.com") && !output.Contains("secret://"), "Core output disclosed private data.");
        Require(output.Contains("activeConnected=True") && output.Contains("activeProxyRequested=True"), "Active state must not come from idle core reader.");
        Require(output.Contains("serviceConnected=False") && output.Contains("activeService="), "Reader and active state must remain separate.");
        Require(output.Contains("category=showip-route") && output.Contains("outbound=ai-proxy-2") && output.Contains("target=showip"), "Routing signal was redacted or starved by unrelated route chatter.");
        Require(output.Contains("connection refused") && output.Contains("context deadline exceeded"), "Useful error causes were lost.");
        string scrubbed = ConnectionDiagnostics.SafeCoreText("error password=PRIVATE_VALUE token=abcdef12345 https://user:pass@private.example/path 203.0.113.8 uuid=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee connection refused context deadline exceeded");
        Require(!scrubbed.Contains("PRIVATE_VALUE") && !scrubbed.Contains("abcdef") && !scrubbed.Contains("203.0.113") && !scrubbed.Contains("aaaa"), "Sensitive tokens leaked.");
        Require(ConnectionDiagnostics.IsLoopbackProxy("http://127.0.0.1:10808") && !ConnectionDiagnostics.IsLoopbackProxy("http://127.0.0.1:10808@private.example"), "Proxy classification must anchor whole value.");
        Require(ConnectionDiagnostics.Fingerprint("one") == ConnectionDiagnostics.Fingerprint("one"), "Session fingerprints must be stable.");
        Require(ConnectionDiagnostics.Fingerprint("one") != ConnectionDiagnostics.Fingerprint("two"), "Different members need distinct fingerprints.");
        string routeCategory, routeDetails;
        Require(CoreRoutingSignal.TryRead("[Info] [1234] app/dispatcher: taking detour [ai-proxy-2] for [tcp:showip.net:443]", out routeCategory, out routeDetails)
            && routeCategory == "showip-route" && routeDetails.Contains("outbound=ai-proxy-2") && routeDetails.Contains("request=1234"), "AI route selection lost");
        Require(CoreRoutingSignal.TryRead("[Info] [1234] app/dispatcher: default route for tcp:showip.net:443", out routeCategory, out routeDetails)
            && routeCategory == "showip-route" && routeDetails.Contains("decision=default-route"), "Implicit default route hidden");
        Require(CoreRoutingSignal.TryRead("[Info] [1234] app/dispatcher: Hit route rule: [ai-routing] so taking detour [block] for [tcp:showip.net:443]", out routeCategory, out routeDetails)
            && routeCategory == "showip-route" && routeDetails.Contains("outbound=block"), "Empty AI blocking was hidden from routing diagnostics");
        Require(CoreRoutingSignal.TryRead("[Info] app/router: least load: no qualified outbound", out routeCategory, out routeDetails)
            && routeCategory == "pool-selection-error", "Empty pool selection hidden");
        Require(CoreRoutingSignal.TryRead("[Info] [9] app/dispatcher: taking detour [ai-proxy-1] for [tcp:private.example:443]", out routeCategory, out routeDetails)
            && !routeDetails.Contains("private.example"), "Route metadata leaked a destination");
        Require(CoreRoutingSignal.TryRead("[Info] app/dispatcher: taking detour [smart-proxy-0] for [tcp:showip.net.evil.example:443]", out routeCategory, out routeDetails)
            && routeCategory != "showip-route", "Unrelated domain classified as ShowIP");
        Require(!CoreRoutingSignal.TryRead("[Info] app/dispatcher: taking detour [PRIVATE_VALUE] for [tcp:private.example:443]", out routeCategory, out routeDetails), "Unknown tag accepted");
        using (var entered = new System.Threading.ManualResetEventSlim())
        using (var release = new System.Threading.ManualResetEventSlim())
        {
            LogHelper.Sink = s => { entered.Set(); release.Wait(3000); };
            ConnectionDiagnostics.Write("slow-disk", "first");
            Require(entered.Wait(2000), "Async writer did not start.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) ConnectionDiagnostics.Write("slow-disk", "queued=" + i);
            bool nonblocking = clock.ElapsedMilliseconds < 1000;
            release.Set(); ConnectionDiagnostics.Flush();
            Require(nonblocking, "Slow diagnostic sink blocked its caller.");
        }
        LogHelper.Sink = s => throw new InvalidOperationException("simulated disk failure");
        TunnelPlusService.Exercise();
        ConnectionDiagnostics.Write("check", "disk-failure");
        ConnectionDiagnostics.Flush();
        Console.WriteLine("PASS: active/reader separation, error budget under route flood, proxy wrapper compilation, privacy, asynchronous slow/failing sink isolation, build identity and correlation.");
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
}
