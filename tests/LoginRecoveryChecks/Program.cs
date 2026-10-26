using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using IRSpeedyVPN.WebServices;

sealed class NativeTransport : ILoginConnectTransport
{
    internal readonly List<string> Resolves = new List<string>();
    internal readonly List<LoginCurlResult> Results = new List<LoginCurlResult>();
    internal string SystemIp = "127.0.0.1";
    internal readonly string CaPath;
    internal NativeTransport(string caPath) { CaPath = caPath; }
    public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeoutMs, CancellationToken cancellation)
        => Run(url, method, headers, body, resolve, timeoutMs, cancellation, null);
    public LoginCurlResult CurlObserved(string url, string method, string headers, string body, int timeoutMs, CancellationToken cancellation, LoginConnectProgress progress)
        => Run(url, method, headers, body, null, timeoutMs, cancellation, progress);
    private LoginCurlResult Run(string url, string method, string headers, string body, string resolve, int timeoutMs, CancellationToken cancellation, LoginConnectProgress progress)
    {
        Resolves.Add(resolve);
        var uri = new Uri(url);
        string actual = resolve == null ? uri.Host + ":" + uri.Port + ":" + SystemIp
            : resolve.Replace("93.184.216.34", "127.0.0.1").Replace("93.184.216.35", "127.0.0.1").Replace("93.184.216.36", "127.0.0.2");
        string args = CurlHelper.BuildLoginArgs(url, method, headers, body, actual, timeoutMs - 100);
        if (args.Contains("--insecure") || args.Contains(" -k ")) throw new Exception("TLS verification disabled");
        args += " --cacert \"" + CaPath + "\"";
        if (progress != null && !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) args += " --verbose";
        var result = CurlHelper.ExecuteLocal(args, timeoutMs, cancellation, progress);
        Results.Add(result); return result;
    }
    public LoginCurlResult Managed(string url, string method, string headers, string body, int timeoutMs, CancellationToken cancellation)
        { throw new Exception("network failure must never use managed system DNS retry"); }
}
sealed class StubTransport : ILoginTransport
{
    internal Func<string, LoginCurlResult> Handler;
    internal int Calls, ManagedCalls;
    public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeoutMs, CancellationToken cancellation)
    { Calls++; return Handler(resolve); }
    public LoginCurlResult Managed(string url, string method, string headers, string body, int timeoutMs, CancellationToken cancellation)
    { ManagedCalls++; return new LoginCurlResult { HttpCode = 200 }; }
}
class Program
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Failure(int code) => new LoginCurlResult { ExitCode = code, MetricsKnown = true };
    static LoginNetworkContext Context(CancellationToken cancellation = default(CancellationToken)) => new LoginNetworkContext(cancellation);
    static LoginCurlResult Send(LoginRecovery recovery, string url, LoginNetworkContext context = null)
        => recovery.Send(url, "POST", "Content-Type: application/json\r\n", "{\"username\":\"secret-user\",\"pwd\":\"secret-password\"}", 8000, context ?? Context());
    static LoginTransportException Fails(Action action)
    { try { action(); } catch (LoginTransportException ex) { return ex; } throw new Exception("expected transport failure"); }
    static void Canceled(Action action)
    { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("expected cancellation"); }
    static void Main(string[] args)
    {
        if (args.Length == 2 && (args[0] == "--pending-exit-probe" || args[0] == "--pending-shutdown-probe"))
        { PendingStopChecks.Probe(args[0], args[1]); return; }
        using (var certificates = new Certificates())
        using (var good = new LocalServer(certificates.Correct))
        using (var wrong = new LocalServer(certificates.Wrong))
        using (var doh = new LocalServer())
        {
            string url = "https://login.test:" + good.Port + "/api/server/list/all";
            int fetches = 0;
            Func<string, int, CancellationToken, string[]> fetch = (host, timeout, cancellation) =>
            {
                Interlocked.Increment(ref fetches);
                var response = HttpFallback.Send("http://127.0.0.1:" + doh.Port + "/resolve?name=" + host,
                    "GET", "", null, null, timeout, cancellation);
                return CurlHelper.ParseLoginAddresses(response.Body);
            };
            // Wrong nonempty system answer: target the wrong-name certificate on
            // the same loopback port using a dedicated transport routing override.
            var logs = new List<string>();
            var transport = new NativeTransport(certificates.CaPath);
            var resolver = new LoginDnsCache(fetch);
            var recovery = new LoginRecovery(transport, resolver, logs.Add);
            int posts = good.Posts;
            Check(Send(recovery, url).HttpCode == 200 && fetches == 0 && good.Posts == posts + 1,
                "healthy system DNS sends one POST and never calls DoH");
            Check(good.LastBody == "{\"username\":\"secret-user\",\"pwd\":\"secret-password\"}",
                "native curl preserves the serialized credential POST body");
            foreach (int status in new[] { 401, 406, 500 })
            {
                good.Status = status; posts = good.Posts;
                Check(Send(recovery, url).HttpCode == status && fetches == 0 && good.Posts == posts + 1,
                    "HTTP " + status + " returns once without DoH or POST replay");
            }
            good.Status = 200;
            good.Drop = true; posts = good.Posts; int attempts = transport.Resolves.Count;
            var dropped = Fails(() => Send(recovery, url));
            Check(dropped.MayHaveBeenSent && fetches == 0 && good.Posts == posts + 1 && transport.Resolves.Count == attempts + 1,
                "connection dropped after POST is not replayed");
            good.Drop = false;
            good.DelayMs = 1000; posts = good.Posts; attempts = transport.Resolves.Count;
            var timed = Fails(() => recovery.Send(url, "POST", "Content-Type: application/json\r\n", "secret-user-and-password", 700, Context()));
            Check(timed.MayHaveBeenSent && transport.Results.Last().ExitCode == 28 && fetches == 0
                && good.Posts == posts + 1 && transport.Resolves.Count == attempts + 1,
                "real timeout after connection/upload never replays POST or starts DoH");
            good.DelayMs = 0;

            good.DelayMs = 1400; posts = good.Posts;
            Check(Send(recovery, url).HttpCode == 200 && good.Posts == posts + 1 && fetches == 0,
                "native curl connected/TLS milestones suppress prefetch during a slow HTTP response");
            good.DelayMs = 0;

            // A wrong DNS answer can lead to a server with the wrong certificate.
            // Route only the FIRST native attempt to that loopback listener; the
            // production --resolve argument and hostname stay unchanged on retry.
            var redirected = new FirstWrongTransport(certificates.CaPath, wrong.Port);
            var dns = new LoginDnsCache(fetch);
            var repaired = new LoginRecovery(redirected, dns, logs.Add);
            posts = good.Posts; int wrongPosts = wrong.Posts; int before = fetches;
            Check(Send(repaired, url).HttpCode == 200 && good.Posts == posts + 1 && wrong.Posts == wrongPosts && fetches == before + 1,
                "wrong nonempty system DNS recovers the SAME hostname and exactly one POST reaches a server");
            Check(redirected.Resolves[1].EndsWith(":93.184.216.34,93.184.216.35") && redirected.Results[0].ExitCode == 60,
                "all public A records are passed together in --resolve after pre-send certificate failure");
            Check(good.LastHost == "login.test:" + good.Port, "URL/Host and TLS hostname remain the original hostname");

            var invalid = new AlwaysWrongTransport(certificates.CaPath, wrong.Port);
            var invalidDns = new LoginDnsCache(fetch);
            before = fetches;
            var invalidError = Fails(() => Send(new LoginRecovery(invalid, invalidDns, logs.Add), url));
            Check(invalid.Results.Count == 2 && invalid.Results.All(r => r.ExitCode == 60) && fetches == before + 1
                && invalidError.Message.Contains("Initial: exit=60") && invalidError.Message.Contains("recovery: exit=60"),
                "wrong-domain certificate stays rejected after recovery; both failures are retained and no loop occurs");

            var cached = new LoginDnsCache((host, timeout, cancellation) => new[] { "93.184.216.36" });
            cached.Resolve("login.test", 1000, CancellationToken.None, false);
            // Stale 127.0.0.2 mapping has no listener. Refresh returns the real local
            // fake-DoH response via an indirection, preserving production caching.
            bool stale = true;
            var refreshing = new LoginDnsCache((host, timeout, cancellation) => stale ? new[] { "93.184.216.36" } : fetch(host, timeout, cancellation));
            refreshing.Resolve("login.test", 1000, CancellationToken.None, false); stale = false;
            var staleTransport = new FirstWrongTransport(certificates.CaPath, wrong.Port);
            before = fetches; posts = good.Posts;
            Check(Send(new LoginRecovery(staleTransport, refreshing, logs.Add), url).HttpCode == 200
                && staleTransport.Resolves.Count == 3 && fetches == before + 1 && good.Posts == posts + 1,
                "bad cached address is evicted and refreshed once inside the same recovery");

            foreach (string address in new[] { "0.0.0.0", "0.1.2.3", "10.0.0.1", "127.0.0.1", "169.254.1.2", "172.16.1.2", "172.31.255.255", "192.168.1.1", "224.0.0.1", "239.1.2.3", "255.255.255.255", "::1", "100.64.0.1" })
                Check(!LoginDnsCache.IsPublicIPv4(address), "reject nonpublic DoH answer " + address);
            doh.Json = "{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"127.0.0.1\"}]}";
            var privateTransport = new FirstWrongTransport(certificates.CaPath, wrong.Port);
            Fails(() => Send(new LoginRecovery(privateTransport, new LoginDnsCache(fetch), logs.Add), url));
            Check(privateTransport.Resolves.Count == 1, "private fake DoH response cannot trigger a POST attempt");

            using (var cancel = new CancellationTokenSource())
            {
                good.DelayMs = 1000;
                var slow = new NativeTransport(certificates.CaPath);
                cancel.CancelAfter(100);
                Canceled(() => Send(new LoginRecovery(slow, new LoginDnsCache(fetch), logs.Add), url, Context(cancel.Token)));
                Check(slow.Resolves.Count == 1, "cancellation stops the real curl process and never starts DoH/replay");
                good.DelayMs = 0;
            }
            using (var cancel = new CancellationTokenSource())
            {
                doh.DelayMs = 1000;
                var slowDoh = new FirstWrongTransport(certificates.CaPath, wrong.Port);
                cancel.CancelAfter(150);
                Canceled(() => Send(new LoginRecovery(slowDoh, new LoginDnsCache(fetch), logs.Add), url, Context(cancel.Token)));
                Check(slowDoh.Resolves.Count == 1, "cancellation aborts an in-flight DoH request before the retry POST");
                doh.DelayMs = 0;
            }

            int lookups = 0;
            var counting = new LoginDnsCache((host, timeout, cancellation) => { lookups++; return new[] { "93.184.216.34" }; });
            var failing = new StubTransport { Handler = resolve => Failure(7) };
            var bounded = new LoginRecovery(failing, counting, logs.Add);
            var context = Context(); Fails(() => Send(bounded, url, context)); Fails(() => Send(bounded, url, context));
            Check(lookups == 1 && failing.Calls == 3 && failing.ManagedCalls == 0,
                "one hostname has one recovery per login and network failures never enter .NET");
            Fails(() => Send(bounded, url, Context()));
            Check(lookups == 2, "a second login gets independent recovery state");

            foreach (int code in new[] { 6, 7, 35, 60, 28 })
            {
                var eligible = new StubTransport { Handler = resolve => resolve == null ? Failure(code) : new LoginCurlResult { HttpCode = 200 } };
                int queries = 0;
                var answers = new LoginDnsCache((host, timeout, cancellation) => { queries++; return new[] { "93.184.216.34" }; });
                Check(Send(new LoginRecovery(eligible, answers, logs.Add), url).HttpCode == 200
                    && eligible.Calls == 2 && queries == 1 && eligible.ManagedCalls == 0,
                    "eligible pre-send exit " + code + " recovers exactly once before failover");
            }
            foreach (int code in new[] { 52, 55, 56, 0, 1 })
                Check(!Failure(code).CanRecover, "ineligible exit " + code);
            var tcpTimeout = Failure(28); tcpTimeout.ConnectSeconds = .000001; tcpTimeout.TlsSeconds = .000001;
            Check(!tcpTimeout.CanRecover && tcpTimeout.MayHaveBeenSent,
                "timeout after TCP connect forbids replay on every endpoint, including submillisecond timings");
            foreach (int code in new[] { 52, 55, 56 })
            {
                var afterTls = Failure(code); afterTls.TlsSeconds = .000001;
                Check(afterTls.MayHaveBeenSent, "exit " + code + " after TLS forbids replay on every endpoint");
            }
            var upload = Failure(7); upload.UploadedBytes = 1;
            Check(!upload.CanRecover, "any positive upload prevents recovery");
            var tls = Failure(35); tls.TlsSeconds = .000001;
            Check(!tls.CanRecover, "completed TLS prevents recovery");
            var unknown = Failure(6); unknown.MetricsKnown = false;
            Check(!unknown.CanRecover && unknown.MayHaveBeenSent, "missing metrics fail closed");
            var redirectedMetrics = Failure(6); redirectedMetrics.Redirects = 1;
            Check(!redirectedMetrics.CanRecover, "redirect metrics cannot justify replay");

            using (var cancel = new CancellationTokenSource())
            {
                var cancelTransport = new StubTransport { Handler = resolve => { cancel.Cancel(); return Failure(6); } };
                int saved = lookups;
                Canceled(() => Send(new LoginRecovery(cancelTransport, counting, logs.Add), url, Context(cancel.Token)));
                Check(saved == lookups, "cancellation after first failure prevents DoH startup");
            }
            int calls = failing.Calls; int savedLookups = lookups;
            try { Send(bounded, url, new LoginNetworkContext(CancellationToken.None, 0)); } catch (TimeoutException) { }
            Check(failing.Calls == calls && lookups == savedLookups, "exhausted flow budget starts neither curl nor DoH");
            var expiring = new StubTransport { Handler = resolve => { Thread.Sleep(25); return Failure(6); } };
            try { Send(new LoginRecovery(expiring, counting, logs.Add), url, new LoginNetworkContext(CancellationToken.None, 10)); } catch (TimeoutException) { }
            Check(lookups == savedLookups, "budget exhaustion after the first attempt prevents DoH");
            var endpointExpired = Context(); endpointExpired.BeginEndpoint(1); Thread.Sleep(5);
            calls = failing.Calls;
            try { Send(bounded, url, endpointExpired); } catch (TimeoutException) { }
            Check(failing.Calls == calls, "endpoint deadline includes serialization/setup before the transport is called");
            var unavailable = new StubTransport { Handler = resolve => new LoginCurlResult { Unavailable = true } };
            Check(Send(new LoginRecovery(unavailable, counting, logs.Add), url).HttpCode == 200 && unavailable.ManagedCalls == 1,
                ".NET is used only when curl cannot start");
            Check(logs.All(line => !line.Contains("secret-user") && !line.Contains("password")), "event logs contain no credentials");
            Check(logs.All(IRSpeedyVPN.Common.LogPolicy.ShouldWrite), "recovery event records survive the production log filter");
            var hidden = Failure(6); hidden.PretransferSeconds = 0.001; hidden.Redirects = 1;
            Check(hidden.Summary.Contains("pretransfer=0.001") && hidden.Summary.Contains("redirects=1")
                && hidden.Summary.Contains("mayHaveBeenSent=True") && hidden.Summary.Contains("canRecover=False"),
                "diagnostics expose hidden pretransfer/redirect replay guards");
            Check(logs.Any(line => line.Contains("stage=recovery-decision") && line.Contains("lookupBudgetMs="))
                && logs.Any(line => line.Contains("stage=lookup-result") && line.Contains("addressCount="))
                && logs.Any(line => line.Contains("stage=http-return")),
                "diagnostics record recovery guards, DNS results and HTTP bypass");
            ControllerChecks.Run();
            DnsFailureChecks.Run(certificates);
            ResolverFirstChecks.Run(certificates);
            TransferDiagnosticChecks.Run(certificates);
            PlainDnsChecks.Run();
            CompletionChecks.Run(certificates);
            RegressionChecks.Run();
            PrefetchChecks.Run();
            ImprovementChecks.Run(certificates);
            PendingStopChecks.Run();
        }
    }
}

// Test-only routing translation: a public fake-DoH address maps to loopback.
// Neither certificate validation nor hostname checking is ever disabled.
class FirstWrongTransport : ILoginTransport
{
    internal readonly List<string> Resolves = new List<string>();
    internal readonly List<LoginCurlResult> Results = new List<LoginCurlResult>();
    protected readonly string ca; protected readonly int wrongPort;
    internal FirstWrongTransport(string ca, int wrongPort) { this.ca = ca; this.wrongPort = wrongPort; }
    protected virtual bool Wrong(string resolve) => resolve == null;
    public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeoutMs, CancellationToken cancellation)
    {
        Resolves.Add(resolve); var uri = new Uri(url);
        string actual = (resolve ?? uri.Host + ":" + uri.Port + ":127.0.0.1")
            .Replace("93.184.216.34", "127.0.0.1").Replace("93.184.216.35", "127.0.0.1").Replace("93.184.216.36", "127.0.0.2");
        string args = CurlHelper.BuildLoginArgs(url, method, headers, body, actual, timeoutMs - 100) + " --cacert \"" + ca + "\"";
        if (Wrong(resolve)) args += " --connect-to \"" + uri.Host + ":" + uri.Port + ":127.0.0.1:" + wrongPort + "\"";
        var result = CurlHelper.ExecuteLocal(args, timeoutMs, cancellation); Results.Add(result); return result;
    }
    public LoginCurlResult Managed(string url, string method, string headers, string body, int timeoutMs, CancellationToken cancellation)
        { throw new Exception("unexpected managed replay"); }
}
sealed class AlwaysWrongTransport : FirstWrongTransport
{
    internal AlwaysWrongTransport(string ca, int port) : base(ca, port) { }
    protected override bool Wrong(string resolve) => true;
}
