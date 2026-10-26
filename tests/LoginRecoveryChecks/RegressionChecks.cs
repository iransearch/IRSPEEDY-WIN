using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.WebServices;

static class RegressionChecks
{
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Unsent(int code) => new LoginCurlResult { ExitCode = code, MetricsKnown = true, ConnectSeconds = .1 };
    static readonly ILoginResolver EmptyDns = new LoginDohCache((host, timeout, cancellation) => throw new Exception("unexpected DoH"));
    static LoginTransportException Fails(LoginRecovery recovery, string url)
    {
        try { recovery.Send(url, "POST", "", "credential", 8000, new LoginNetworkContext(CancellationToken.None)); }
        catch (LoginTransportException ex) { return ex; }
        throw new Exception("expected failure");
    }
    static void Failover(LoginTransportException failure, bool allowed)
    {
        int calls = 0;
        try
        {
            new ControllerHarness().Run((host, timeout) =>
            {
                if (++calls == 1) throw failure;
                return new BaseHttpResponse<object> { StatusCode = HttpStatusCode.OK };
            }, new LoginNetworkContext(CancellationToken.None));
            Check(allowed && calls == 2, "controller advances on proven-unsent failure");
        }
        catch (LoginTransportException) { Check(!allowed && calls == 1, "controller stops on possibly-sent failure"); }
    }
    internal static void Run()
    {
        foreach (int code in new[] { 28, 52, 55, 56 })
        {
            var unsent = Unsent(code);
            Check(!unsent.MayHaveBeenSent && !unsent.CanRecover, "HTTPS exit " + code + " before TLS permits failover but not same-host DoH");
            var transport = new StubTransport { Handler = resolve => unsent };
            var failure = Fails(new LoginRecovery(transport, EmptyDns, null), "https://login.test/");
            Failover(failure, true);
            unsent.TlsSeconds = .000001;
            Check(unsent.MayHaveBeenSent && !unsent.CanRecover, "HTTPS exit " + code + " after TLS remains blocked");
            Failover(Fails(new LoginRecovery(transport, EmptyDns, null), "https://login.test/"), false);
            unsent.TlsSeconds = 0;
            Check(Fails(new LoginRecovery(transport, EmptyDns, null), "http://login.test/").MayHaveBeenSent,
                "HTTP exit " + code + " retains the previous conservative policy");
        }
        foreach (var status in new[] { WebExceptionStatus.NameResolutionFailure, WebExceptionStatus.ConnectFailure })
        {
            var managed = new ManagedFailureTransport(new WebException("fixture", status));
            Failover(Fails(new LoginRecovery(managed, EmptyDns, null), "https://login.test/"), true);
            Check(managed.Calls == 1, "managed " + status + " is not treated as a sent POST");
        }
        foreach (var status in new[] { WebExceptionStatus.Timeout, WebExceptionStatus.ReceiveFailure,
            WebExceptionStatus.SendFailure, WebExceptionStatus.ConnectionClosed, WebExceptionStatus.ProtocolError })
            Failover(Fails(new LoginRecovery(new ManagedFailureTransport(new WebException("fixture", status)), EmptyDns, null), "https://login.test/"), false);
        foreach (var status in new[] { WebExceptionStatus.TrustFailure, WebExceptionStatus.SecureChannelFailure })
        {
            var error = new WebException("fixture", status);
            Check(!HttpFallback.ManagedMayHaveBeenSent(error, true, "get-request-stream"), "managed TLS rejection before body stream is proven unsent");
            Check(HttpFallback.ManagedMayHaveBeenSent(error, true, "response-headers", true), "managed TLS failure after a write stays conservative");
            Check(HttpFallback.ManagedMayHaveBeenSent(error, true), "managed TLS failure with no phase evidence stays conservative");
        }
        Check(HttpFallback.ManagedMayHaveBeenSent(new WebException("fixture", WebExceptionStatus.NameResolutionFailure), false),
            "managed HTTP retains conservative behavior");

        string args = CurlHelper.BuildLoginArgs("https://login.test/", "POST", "", " -sS -L ", null, 1500);
        // Ignore quoted payload: the switch prefix itself is checked separately.
        Check(args.StartsWith(" -sS  -X POST ") && !args.StartsWith(" -sS -L "), "login uses explicit followRedirects:false while body remains unchanged");
        string output = "body\n__LOGIN_UPLOAD__:0\n__LOGIN_HTTP__:0\n"
            + "__HTTP_CODE__:200\n__LOGIN_CONNECT__:0.1\n__LOGIN_TLS__:0.2\n__LOGIN_READY__:0.3\n"
            + "__LOGIN_UPLOAD__:42\n__LOGIN_REDIRECTS__:0\n__LOGIN_HTTP__:200\n";
        var parsed = CurlHelper.ParseLoginResult(output, 0);
        Check(parsed.MetricsKnown && parsed.UploadedBytes == 42 && parsed.HttpCode == 200 && parsed.MayHaveBeenSent,
            "body markers cannot override the final curl trailer");
        Check(!CurlHelper.BuildLoginArgs("https://login.test/", "POST", "", "fixture", null, 1500).Contains(" -L "),
            "final login arguments never enable curl redirect replay");
        int expiredCalls = 0;
        try
        {
            new ControllerHarness().Run((host, timeout) => { expiredCalls++; return null; },
                new LoginNetworkContext(CancellationToken.None, 0));
            throw new Exception("expired controller budget accepted");
        }
        catch (TimeoutException) { Check(expiredCalls == 0, "actual controller budget check outside inner try stops before starting a request"); }
        TestAvailability();
        TestProviders();
        TestNative();
    }
    static void TestAvailability()
    {
        string path = Path.Combine(Path.GetTempPath(), "login-pe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "curl"));
        var pe = new byte[512]; pe[0] = 77; pe[1] = 90; pe[60] = 128; pe[128] = 80; pe[129] = 69; pe[132] = 100; pe[133] = 134;
        File.WriteAllBytes(Path.Combine(path, "curl/curl64.exe"), pe);
        AppServices.RuntimePath = path; AppServices.RuntimeDelayMs = 180; CurlHelper.SetAvailabilityForTest(false);
        try
        {
            bool timed = false;
            try { new CurlHelper().SendLoginAttempt("https://login.test", "POST", "", "test", null, 150, CancellationToken.None); }
            catch (TimeoutException) { timed = true; }
            Check(timed && !CurlHelper.IsUnavailable, "launch deadline propagates without poisoning static curl availability");
        }
        finally { AppServices.RuntimePath = "/nonexistent-test-runtime"; AppServices.RuntimeDelayMs = 0; Directory.Delete(path, true); }
        int calls = 0;
        try { new ControllerHarness().Run((host, timeout) => { calls++; throw new LoginTransportTimeoutException(); }, null); }
        catch (TimeoutException) { }
        Check(calls == 1, "hard process timeout remains a TimeoutException and forbids ambiguous POST replay");
        try
        {
            new LoginRecovery(new StubTransport { Handler = resolve => resolve == null ? Unsent(6) : throw new LoginTransportTimeoutException() },
                new LoginDohCache((host, timeout, token) => new[] { "93.184.216.34" }), null)
                .Send("https://login.test/", "POST", "", "fixture", 8000, new LoginNetworkContext(CancellationToken.None));
            throw new Exception("unknown recovery timeout accepted");
        }
        catch (LoginTransportTimeoutException ex)
        { Check(ex.Message.Contains("Initial: exit=6") && ex.Message.Contains("recovery:"), "ambiguous recovery timeout preserves the initial failure without authorizing replay"); }
    }
    static void TestProviders()
    {
        int active = 0, finished = 0;
        var logs = new List<string>();
        var result = LoginDohProviders.Resolve("login.test", 2000, CancellationToken.None, (url, ms, token) =>
        {
            Interlocked.Increment(ref active);
            try
            {
                if (url.Contains("8.8.8.8")) { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
                Thread.Sleep(30); return new[] { "93.184.216.34" };
            }
            finally { Interlocked.Decrement(ref active); Interlocked.Increment(ref finished); }
        }, logs.Add);
        Check(result.Length == 1 && active == 0 && finished == 2 && logs.Any(line => line.Contains("provider=cloudflare") && line.Contains("outcome=winner") && line.Contains("elapsedMs=")),
            "parallel backup beats a blocked provider, logs winner/time and joins the canceled loser");
        var extendedClock = Stopwatch.StartNew();
        result = LoginDohProviders.Resolve("login.test", 8000, CancellationToken.None, (url, ms, token) =>
        {
            Check(ms > 2000 && ms <= 5000, "provider receives the shared five-second cap rather than the caller's larger budget");
            if (url.Contains("8.8.8.8")) { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            if (token.WaitHandle.WaitOne(2200)) token.ThrowIfCancellationRequested();
            return new[] { "93.184.216.34" };
        }, null);
        Check(result.Length == 1 && extendedClock.ElapsedMilliseconds >= 2100 && extendedClock.ElapsedMilliseconds < 5000,
            "valid DoH response after two seconds succeeds within the new five-second race cap");
        result = LoginDohProviders.Resolve("login.test", 2000, CancellationToken.None, (url, ms, token) =>
        {
            if (url.Contains("8.8.8.8")) return new[] { "127.0.0.1", "192.168.1.1" };
            Thread.Sleep(30); return new[] { "93.184.216.34" };
        }, null);
        Check(result.SequenceEqual(new[] { "93.184.216.34" }), "fast private-only response cannot win the provider race");
        foreach (string json in new[] { "{\"Status\":3,\"Answer\":[{\"type\":1,\"data\":\"93.184.216.35\"}]}",
            "{\"Status\":null,\"Answer\":[{\"type\":1,\"data\":\"93.184.216.35\"}]}", "{\"Status\":0,\"Answer\":[]}" })
        {
            result = LoginDohProviders.Resolve("login.test", 2000, CancellationToken.None, (url, ms, token) =>
            {
                if (url.Contains("8.8.8.8")) return CurlHelper.ParseLoginAddresses(json);
                Thread.Sleep(30); return new[] { "93.184.216.34" };
            }, null);
            Check(result.SequenceEqual(new[] { "93.184.216.34" }), "invalid/empty DNS JSON cannot beat a later valid provider");
        }
        var clock = Stopwatch.StartNew(); active = 0;
        result = LoginDohProviders.Resolve("login.test", 200, CancellationToken.None, (url, ms, token) =>
        {
            Interlocked.Increment(ref active);
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return null; }
            finally { Interlocked.Decrement(ref active); }
        }, null);
        Check(result.Length == 0 && active == 0 && clock.ElapsedMilliseconds < 700, "both blocked providers share one deadline and leave no active work");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); int calls = 0;
            try { LoginDohProviders.Resolve("login.test", 2000, cancel.Token, (url, ms, token) => { calls++; return null; }, null); }
            catch (OperationCanceledException) { }
            Check(calls == 0, "canceled lookup starts neither provider");
        }
    }
    static void TestNative()
    {
        // A tiny getaddrinfo test adapter makes .invalid deterministic/offline;
        // curl itself, -w, process execution and parsing remain the real code.
        string previous = Environment.GetEnvironmentVariable("LD_PRELOAD");
        try
        {
            Environment.SetEnvironmentVariable("LD_PRELOAD", Environment.GetEnvironmentVariable("IRSPEEDY_TEST_DNS_SHIM"));
            var dns = CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs("https://native.invalid/", "POST", "", "test", null, 1500), 2000, CancellationToken.None);
            Check(dns.ExitCode == 6 && dns.MetricsKnown && !dns.MayHaveBeenSent, "native curl exit 6 emits a complete -w trailer for .invalid");
        }
        finally { Environment.SetEnvironmentVariable("LD_PRELOAD", previous); }
        var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); int closed = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var connect = CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs("https://127.0.0.1:" + closed + "/", "POST", "", "test", null, 1500), 2000, CancellationToken.None);
        Check(connect.ExitCode == 7 && connect.MetricsKnown && !connect.MayHaveBeenSent, "native curl exit 7 emits a complete -w trailer on a closed port");
        using (var stall = new LocalServer())
        {
            var timed = CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs("https://127.0.0.1:" + stall.Port + "/", "POST", "", "test", null, 3500), 4000, CancellationToken.None);
            Check(timed.ExitCode == 28 && timed.ConnectSeconds > 0 && timed.TlsSeconds == 0 && timed.MetricsKnown
                && !timed.MayHaveBeenSent && !timed.CanRecover, "real TLS handshake stall allows only next-host failover");
            Failover(Fails(new LoginRecovery(new StubTransport { Handler = resolve => timed }, EmptyDns, null), "https://login.test/"), true);
        }
    }
    sealed class ManagedFailureTransport : ILoginTransport
    {
        readonly Exception error; internal int Calls;
        internal ManagedFailureTransport(Exception error) { this.error = error; }
        public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeout, CancellationToken token)
            => new LoginCurlResult { Unavailable = true };
        public LoginCurlResult Managed(string url, string method, string headers, string body, int timeout, CancellationToken token)
        { Calls++; throw error; }
    }
}
