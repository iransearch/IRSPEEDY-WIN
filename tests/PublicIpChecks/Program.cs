using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.SingBox;
using IRSpeedyVPN.UserControls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static int passed;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); passed++; }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { passed++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task Wait(Func<bool> predicate)
    {
        for (int i = 0; i < 1000 && !predicate(); i++) await Task.Delay(2);
        Check(predicate(), "Async lookup did not settle");
    }
    private static HttpResponseMessage Response(string ip, HttpStatusCode status = HttpStatusCode.OK)
        => new HttpResponseMessage(status) { Content = new StringContent(ip) };
    private static PublicIpLookup Lookup(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        int attemptMs = 200, int budgetMs = 2000, int delayMs = 1)
        => new PublicIpLookup(port => new Handler(send), TimeSpan.FromMilliseconds(attemptMs),
            TimeSpan.FromMilliseconds(budgetMs), TimeSpan.FromMilliseconds(delayMs));
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) { this.send = send; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) => send(message, token);
    }

    private static async Task Main()
    {
        await ParsingAndProxy();
        await ProviderFallback();
        await WarmupAndFailureBounds();
        await CancellationAndDeadline();
        await PageLifecycle();
        await StaleConnectionResult();
        await FailureDiagnostics();
        Routing();
        Console.WriteLine("Public IP checks passed: " + passed);
    }

    private static async Task ParsingAndProxy()
    {
        Check(PublicIpLookup.Parse(" 8.8.8.8\n").ToString() == "8.8.8.8", "Plain IPv4 response");
        Check(PublicIpLookup.Parse("2001:4860:4860::8888\n").AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6, "IPv6 response");
        foreach (string body in new[] { "", "<html>blocked</html>", "127.0.0.1", "192.168.1.1", "10.0.0.1", "172.16.0.1", "169.254.1.1", "100.64.1.1", "224.0.0.1", "::1", "::ffff:127.0.0.1", "fd00::1", "fe80::1", new string('x', 129) })
            await Throws<InvalidDataException>(() => { PublicIpLookup.Parse(body); return Task.CompletedTask; });
        using (var handler = PublicIpLookup.CreateHandler(7788))
        {
            foreach (string host in PublicIpLookup.Hosts)
            {
                var uri = new Uri("https://" + host + "/");
                Check(handler.UseProxy && !handler.Proxy.IsBypassed(uri)
                    && handler.Proxy.GetProxy(uri).AbsoluteUri == "http://127.0.0.1:7788/"
                    && !handler.AllowAutoRedirect && !handler.UseCookies, "Every provider uses explicit VPN proxy without redirects");
            }
        }
        int calls = 0;
        var lookup = Lookup((m, t) => { calls++; return Task.FromResult(Response("8.8.8.8")); });
        await Throws<ArgumentOutOfRangeException>(() => lookup.ResolveAsync(0, CancellationToken.None));
        await Throws<ArgumentOutOfRangeException>(() => lookup.ResolveAsync(65536, CancellationToken.None));
        Check(calls == 0, "Invalid listeners never cause a direct request");
    }

    private static async Task ProviderFallback()
    {
        var hosts = new List<string>();
        var diagnostics = new List<string>();
        var lookup = Lookup(async (m, t) =>
        {
            hosts.Add(m.RequestUri.Host);
            Check(m.RequestUri.Scheme == "https" && m.Headers.ConnectionClose == true && m.Headers.CacheControl.NoStore,
                "Fresh TLS request without cached response");
            if (hosts.Count == 1) await Task.Delay(Timeout.Infinite, t);
            return Response("1.1.1.1\n");
        }, attemptMs: 30);
        var result = await lookup.ResolveAsync(7788, CancellationToken.None, (s, f) => diagnostics.Add(s + " " + f));
        Check(hosts.SequenceEqual(PublicIpLookup.Hosts.Take(2)) && result.Provider == PublicIpLookup.Hosts[1]
            && result.Address.ToString() == "1.1.1.1", "Timed-out ipify falls back to a different provider");
        Check(diagnostics.Any(d => d.Contains("reason=timeout")) && diagnostics.All(d => !d.Contains("1.1.1.1")), "Failures identify provider without logging IP");

        int calls = 0;
        lookup = Lookup((m, t) => Task.FromResult(++calls == 1 ? Response("blocked", HttpStatusCode.ServiceUnavailable)
            : calls == 2 ? Response("192.168.1.1") : Response("8.8.4.4")));
        result = await lookup.ResolveAsync(7788, CancellationToken.None);
        Check(calls == 3 && result.Provider == PublicIpLookup.Hosts[2], "HTTP failure and invalid address both advance to next provider");
    }

    private static async Task WarmupAndFailureBounds()
    {
        int calls = 0;
        var lookup = Lookup((m, t) => ++calls <= 3 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("cold listener"))
            : Task.FromResult(Response("8.8.8.8")));
        var result = await lookup.ResolveAsync(7788, CancellationToken.None);
        Check(calls == 4 && result.Provider == PublicIpLookup.Hosts[0], "VPN becoming ready during second round can publish IP");
        calls = 0;
        lookup = Lookup((m, t) => { calls++; return Task.FromException<HttpResponseMessage>(new HttpRequestException("unreachable")); });
        await Throws<HttpRequestException>(() => lookup.ResolveAsync(7788, CancellationToken.None));
        Check(calls == 6, "All failures stop after two rounds; no continuous polling");
        calls = 0;
        lookup = Lookup((m, t) => { calls++; return Task.FromResult(Response(new string('x', 129))); });
        await Throws<HttpRequestException>(() => lookup.ResolveAsync(7788, CancellationToken.None));
        Check(calls == 6, "Oversized responses cannot be displayed and retries remain bounded");
    }

    private static async Task CancellationAndDeadline()
    {
        int calls = 0;
        var lookup = Lookup(async (m, t) => { Interlocked.Increment(ref calls); await Task.Delay(Timeout.Infinite, t); return Response("8.8.8.8"); });
        using (var cancellation = new CancellationTokenSource())
        {
            var task = lookup.ResolveAsync(7788, cancellation.Token);
            await Wait(() => calls == 1);
            cancellation.Cancel();
            await Throws<OperationCanceledException>(() => task);
            Check(calls == 1, "Disconnect cancels outstanding request without starting a fallback");
        }
        calls = 0;
        lookup = Lookup(async (m, t) => { calls++; await Task.Delay(Timeout.Infinite, t); return Response("8.8.8.8"); }, attemptMs: 1000, budgetMs: 30);
        await Throws<TimeoutException>(() => lookup.ResolveAsync(7788, CancellationToken.None));
        Check(calls == 1, "Total time budget overrides per-provider attempts");
    }

    private static async Task PageLifecycle()
    {
        int calls = 0;
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default(CancellationToken);
        var lookup = Lookup((m, t) => { calls++; firstToken = t; return calls == 1 ? pending.Task : Task.FromResult(Response("1.1.1.1")); });
        var page = new UCUserInfo(lookup);
        page.Refresh(); page.Refresh();
        Check(calls == 1 && !firstToken.IsCancellationRequested, "Loaded and visibility events share one request");
        pending.SetResult(Response("8.8.8.8"));
        await Wait(() => !page.Pending);
        Check(page.Displayed == "8.8.8.8", "IP is displayed after success");
        page.Hide(); page.Show();
        Check(calls == 1 && page.Displayed == "8.8.8.8", "Reopening same connection restores cached observed IP");
        page.Reconnect();
        await Wait(() => !page.Pending);
        Check(calls == 2 && page.Displayed == "1.1.1.1", "Reconnect invalidates previous connection's IP");
        page.ChangePort(8899);
        await Wait(() => !page.Pending);
        Check(calls == 3, "Listener change invalidates cached lookup");
    }

    private static async Task StaleConnectionResult()
    {
        int calls = 0;
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default(CancellationToken);
        var lookup = Lookup((m, t) => { calls++; if (calls == 1) { oldToken = t; return late.Task; } return Task.FromResult(Response("1.1.1.1")); });
        var page = new UCUserInfo(lookup);
        page.Refresh(); page.Reconnect();
        await Wait(() => !page.Pending);
        Check(oldToken.IsCancellationRequested && page.Displayed == "1.1.1.1", "New connection cancels old lookup");
        late.SetResult(Response("8.8.8.8")); // Transport deliberately ignores cancellation until it returns.
        await Wait(() => ConnectionDiagnostics.Records.Any(r => r.StartsWith("public-ip-canceled ")));
        Check(page.Displayed == "1.1.1.1", "Late result from disconnected connection cannot replace current IP");
    }

    private static void Routing()
    {
        var root = JObject.Parse(@"{'inbounds':[{'type':'mixed','tag':'local'},{'type':'tun','tag':'tun'}],
            'outbounds':[{'type':'auto-selector-round-robin','tag':'proxy'},{'type':'auto-selector','tag':'ai-proxy'}],
            'route':{'rules':[{'process_name':['IRSpeedyVPN.exe'],'outbound':'direct'},
                {'domain_suffix':['showip.net'],'outbound':'ai-proxy'}],'final':'proxy'}}");
        var originalOutbounds = root["outbounds"].DeepClone();
        var originalRules = root["route"]["rules"].DeepClone();
        var applied = JObject.Parse(VpnTimeZoneRouting.Apply(root.ToString()));
        var rules = (JArray)applied["route"]["rules"];
        var first = (JObject)rules[0];
        Check(first["domain"].Values<string>().SequenceEqual(PublicIpLookup.Hosts.Concat(new[] { TimeZoneLookup.Host })), "Every IP provider has primary VPN routing");
        Check((string)first["outbound"] == "proxy" && (string)first["network"] == "tcp" && (int)first["port"] == 443
            && first["inbound"].Values<string>().SequenceEqual(new[] { "local" }), "Exact lookup domains on HTTP listener precede process/direct bypass");
        Check(JToken.DeepEquals(applied["outbounds"], originalOutbounds) && JToken.DeepEquals(new JArray(rules.Skip(1)), originalRules), "Pool configuration and independent AI rules remain intact");
    }

    private static async Task FailureDiagnostics()
    {
        string discarded;
        while (ConnectionDiagnostics.Records.TryDequeue(out discarded)) { }
        var lookup = Lookup((m, t) => Task.FromException<HttpResponseMessage>(new HttpRequestException("PRIVATE_URL",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused))));
        var page = new UCUserInfo(lookup);
        page.Refresh(); await Wait(() => !page.Pending);
        var lines = ConnectionDiagnostics.Records.ToArray();
        Check(lines.Count(l => l.StartsWith("snapshot-request cause=public-ip-first-failure")) == 1
            && lines.Count(l => l.StartsWith("snapshot-request cause=public-ip-failed")) == 1, "Six provider failures request only first/final core snapshots");
        Check(lines.Any(l => l.StartsWith("public-ip-attempt-failed ") && l.Contains("socketError="))
            && lines.All(l => !l.Contains("PRIVATE_URL")), "Provider diagnostics retain safe inner socket cause");
        Check(page.Displayed == "—", "Diagnostic failure never fabricates a received IP");
    }
}
