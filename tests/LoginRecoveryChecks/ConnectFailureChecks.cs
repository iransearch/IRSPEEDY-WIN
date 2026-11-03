using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class ConnectFailureChecks
{
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    // Exact failed-connect trailer from the November 4 Windows report.
    static LoginCurlResult Captured()
        => CurlHelper.ParseLoginResult("__HTTP_CODE__:000\n__LOGIN_CONNECT__:0.000000\n"
            + "__LOGIN_TLS__:0.000000\n__LOGIN_READY__:0.014414\n__LOGIN_UPLOAD__:0\n"
            + "__LOGIN_REDIRECTS__:0\n__LOGIN_HTTP__:000\n__LOGIN_REMOTE__:\n"
            + "__LOGIN_FIRST_BYTE__:0.014414\n__LOGIN_TOTAL__:0.014416\n"
            + "__LOGIN_DOWNLOAD__:0\n__LOGIN_VERSION__:0\n", 7);
    static LoginCurlResult Send(ILoginTransport transport, LoginDnsCache cache, List<string> logs = null,
        string url = "https://login.test/", LoginNetworkContext context = null)
        => new LoginRecovery(transport, cache, logs == null ? (Action<string>)null : logs.Add, LoginDnsOrder.ResolverFirst)
            .Send(url, "POST", "", "credential-must-not-be-logged", 8000,
                context ?? new LoginNetworkContext(CancellationToken.None));
    internal static void Run(Certificates certificates)
    {
        var recorded = Captured();
        Check(recorded.MetricsKnown && recorded.TransferMetricsKnown && recorded.MayHaveBeenSent
            && recorded.DefinitelyNotSent && recorded.CanRecover && !recorded.MayHaveBeenSentFor(true)
            && recorded.MayHaveBeenSentFor(false),
            "captured Windows connect failure permits only a fully evidenced HTTPS pre-send recovery");
        foreach (var mutation in new Dictionary<string, Action<LoginCurlResult>>
        {
            { "unknown core metrics", r => r.MetricsKnown = false },
            { "missing first-byte metric", r => r.FirstByteSeconds = null },
            { "missing total metric", r => r.TotalSeconds = null },
            { "missing download metric", r => r.DownloadedBytes = null },
            { "missing protocol metric", r => r.HttpVersion = null },
            { "TCP connection", r => r.ConnectSeconds = .001 },
            { "TLS completion", r => r.TlsSeconds = .001 },
            { "upload", r => r.UploadedBytes = 1 },
            { "HTTP response", r => r.HttpCode = 200 },
            { "redirect", r => r.Redirects = 1 },
            { "known peer", r => r.RemoteIp = "93.184.216.34" },
            { "download", r => r.DownloadedBytes = 1 },
            { "response body", r => r.Body = "partial" },
            { "HTTP protocol", r => r.HttpVersion = "1.1" },
            { "unavailable curl", r => r.Unavailable = true }
        })
        {
            var result = Captured(); mutation.Value(result);
            Check(!result.DefinitelyNotSent && !result.CanRecover && result.MayHaveBeenSentFor(true),
                "connect-failure proof rejects " + mutation.Key);
        }
        foreach (int code in new[] { 28, 35, 60, 52, 55, 56, -1 })
        {
            var result = Captured(); result.ExitCode = code;
            Check(!result.DefinitelyNotSent && !result.CanRecover && result.MayHaveBeenSentFor(true),
                "complete diagnostic trailer never overrides positive pretransfer for exit " + code);
        }
        // Both families retain identical completion-order priority, even if the loser
        // already has a valid answer when its canceled worker is joined.
        foreach (bool ipv6First in new[] { true, false })
        using (var ready = new CountdownEvent(2))
        {
            var answer = LoginDnsRace.Resolve("login.test", 2000, CancellationToken.None, (provider, ms, token) =>
            {
                ready.Signal(); if (!ready.Wait(1000)) throw new TimeoutException();
                if ((provider == "AAAA") != ipv6First) token.WaitHandle.WaitOne();
                return provider == "AAAA" ? new[] { "2606:4700:3037::6815:136a" } : new[] { "93.184.216.34" };
            }, null, new[] { "AAAA", "A" }, new[] { "fixture-AAAA", "fixture-A" });
            Check(answer.Single().Contains(":") == ipv6First,
                "first valid DNS answer wins with no address-family preference (IPv6=" + ipv6First + ")");
        }
        var logs = new List<string>();
        using (var server = new LocalServer(certificates.Correct))
        {
            var native = new NativeTransport(certificates.CaPath);
            string url = "https://login.test:" + server.Port + "/";
            int lookups = 0;
            var fresh = new LoginDnsCache((host, ms, token) => { lookups++; return new[] { "2606:4700:3037::6815:136a" }; });
            var transport = new StubTransport { Handler = resolve => resolve != null ? Captured()
                : native.Curl(url, "POST", "", "fixture", null, 4000, CancellationToken.None) };
            Check(Send(transport, fresh, logs, url).HttpCode == 200 && lookups == 1
                && transport.Calls == 2 && native.Resolves.Single() == null && server.Posts == 1,
                "fresh failed IPv6 answer falls back once to certificate-verified system HTTPS with one actual POST");
            fresh.Resolve("login.test", 1000, CancellationToken.None, false);
            Check(lookups == 2, "failed fresh answer was removed from the five-minute cache");
        }
        using (var server = new LocalServer(certificates.Correct))
        {
            var native = new NativeTransport(certificates.CaPath);
            string url = "https://login.test:" + server.Port + "/";
            int lookups = 0;
            var cache = new LoginDnsCache((host, ms, token) => ++lookups == 1
                ? new[] { "2606:4700:3037::6815:136a" } : new[] { "93.184.216.34" });
            cache.Resolve("login.test", 1000, CancellationToken.None, false);
            var transport = new StubTransport { Handler = resolve => resolve.Contains("[") ? Captured()
                : native.Curl(url, "POST", "", "fixture", resolve, 4000, CancellationToken.None) };
            Check(Send(transport, cache, logs, url).HttpCode == 200 && lookups == 2
                && transport.Calls == 2 && server.Posts == 1,
                "cached failed IPv6 is evicted and refreshed exactly once before one successful native POST");
            Check(Send(transport, cache, logs, url).HttpCode == 200 && lookups == 2 && server.Posts == 2,
                "next independent login reuses the working answer without restarting the app");
        }
        foreach (bool ambiguousSystem in new[] { false, true })
        {
            int endpoints = 0;
            var ctx = new LoginNetworkContext(CancellationToken.None);
            var transport = new StubTransport { Handler = resolve => endpoints > 1
                ? new LoginCurlResult { HttpCode = 200 } : resolve == null && ambiguousSystem
                    ? new LoginCurlResult { ExitCode = 28, MetricsKnown = false } : Captured() };
            var cache = new LoginDnsCache((host, ms, token) => new[] { "2606:4700:3037::6815:136a" });
            try
            {
                var response = new ControllerHarness().Run((host, timeout) =>
                {
                    endpoints++;
                    var result = Send(transport, cache, logs, host.BaseAddress, ctx);
                    return new BaseHttpResponse<object> { StatusCode = (HttpStatusCode)result.HttpCode };
                }, ctx);
                Check(!ambiguousSystem && endpoints == 2 && transport.Calls == 3 && response.StatusCode == HttpStatusCode.OK,
                    "proven-unsent resolver and system failures allow the next API hostname");
            }
            catch (LoginTransportException ex)
            {
                Check(ambiguousSystem && ex.MayHaveBeenSent && endpoints == 1 && transport.Calls == 2,
                    "ambiguous system fallback still blocks every subsequent credential attempt");
            }
        }
        Check(logs.Any(l => l.Contains("effectiveMayHaveBeenSent=False"))
            && logs.All(l => !l.Contains("credential-must-not-be-logged")),
            "connect-failure recovery logs the proof without credentials");
    }
}
