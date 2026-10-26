using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class ImprovementChecks
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Failed(int exit, string peer = "93.184.216.34")
        => new LoginCurlResult { ExitCode = exit, MetricsKnown = true, ConnectSeconds = .1, RemoteIp = peer };
    static LoginCurlResult Send(ILoginTransport transport, ILoginResolver resolver, string url = "https://login.test/")
        => new LoginRecovery(transport, resolver, null).Send(url, "POST", "", "fixture", 8000, new LoginNetworkContext(CancellationToken.None));
    static LoginTransportException Fails(Action action)
    { try { action(); } catch (LoginTransportException ex) { return ex; } throw new Exception("expected failure"); }
    internal static void Run(Certificates certificates)
    {
        TestReserve(certificates);
        TestPeers(certificates);
        TestProcessCleanup();
    }
    static void TestReserve(Certificates certificates)
    {
        using (var server = new LocalServer(certificates.Correct))
        {
            server.DelayMs = 2300;
            string url = "https://login.test:" + server.Port + "/";
            var transport = new ReserveTransport(certificates.CaPath);
            int lookupMs = 0;
            var dns = new LoginDnsCache((host, timeout, token) =>
            {
                lookupMs = timeout;
                if (token.WaitHandle.WaitOne(Math.Max(0, timeout - 180))) token.ThrowIfCancellationRequested();
                return new[] { "93.184.216.34" };
            });
            var clock = Stopwatch.StartNew();
            Check(Send(transport, dns, url).HttpCode == 200 && server.Posts == 1 && clock.ElapsedMilliseconds < 8200,
                "three-second first failure plus near-budget DoH still allows a slow real HTTPS POST inside eight seconds");
            Check(lookupMs > 0 && lookupMs <= 2000 && transport.RetryMs >= 3000,
                "non-prefetch lookup yields its time to a three-second POST reserve");
        }
        int speculativeMs = 0;
        var cache = new LoginDnsCache((host, timeout, token) => { speculativeMs = timeout; return new[] { "93.184.216.34" }; });
        var progress = new LoginConnectProgress();
        var context = new LoginNetworkContext(CancellationToken.None);
        using (var prefetch = new LoginPrefetch(cache, "login.test", new LoginDeadline(context, 8000), context, progress))
        {
            progress.Start(); Thread.Sleep(1250); progress.Complete();
            bool attempted; prefetch.Take(out attempted);
            Check(attempted && speculativeMs > 0 && speculativeMs <= 4000,
                "one-second prefetch also reserves three seconds inside the host deadline");
        }
    }
    static void TestPeers(Certificates certificates)
    {
        const string trailer = "\n__HTTP_CODE__:0\n__LOGIN_CONNECT__:0.1\n__LOGIN_TLS__:0\n__LOGIN_READY__:0\n__LOGIN_UPLOAD__:0\n__LOGIN_REDIRECTS__:0\n__LOGIN_HTTP__:0\n__LOGIN_REMOTE__:";
        var parsed = CurlHelper.ParseLoginResult("__LOGIN_REMOTE__:93.184.216.34" + trailer + "93.184.216.35\n", 28);
        Check(parsed.RemoteIp == "93.184.216.35" && parsed.CanResolveAlternatePeer && !parsed.CanRecover,
            "last validated remote_ip permits only a conditional alternate-peer lookup");
        Check(CurlHelper.ParseLoginResult("__LOGIN_REMOTE__:93.184.216.34" + trailer + "\n", 28).RemoteIp == null,
            "empty remote_ip trailer cannot inherit a spoofed response-body peer");
        Check(CurlHelper.ParseLoginResult(trailer + "not-an-ip\n", 28).RemoteIp == null,
            "invalid remote_ip never becomes alternate-peer evidence");
        foreach (int exit in new[] { 28, 35, 60 })
        {
            int lookups = 0;
            var same = new StubTransport { Handler = resolve => Failed(exit) };
            var dns = new LoginDnsCache((host, ms, token) => { lookups++; return new[] { "93.184.216.34" }; });
            var failure = Fails(() => Send(same, dns));
            Check(!failure.MayHaveBeenSent && same.Calls == 1 && lookups == 1,
                "exit " + exit + " with only the failed peer in DoH never retries that address");
            string actualResolve = null;
            var changed = new StubTransport { Handler = resolve =>
            {
                if (resolve == null) return Failed(exit);
                actualResolve = resolve; return new LoginCurlResult { HttpCode = 200 };
            } };
            dns = new LoginDnsCache((host, ms, token) => new[] { "93.184.216.34", "93.184.216.35" });
            Check(Send(changed, dns).HttpCode == 200 && changed.Calls == 2 && actualResolve == "login.test:443:93.184.216.35",
                "exit " + exit + " retries only the different public address");
        }
        foreach (var forbidden in new[] {
            new LoginCurlResult { ExitCode=28,MetricsKnown=true,ConnectSeconds=.1,TlsSeconds=.000001,RemoteIp="93.184.216.34" },
            new LoginCurlResult { ExitCode=28,MetricsKnown=true,ConnectSeconds=.1,UploadedBytes=1,RemoteIp="93.184.216.34" },
            new LoginCurlResult { ExitCode=28,MetricsKnown=false,ConnectSeconds=.1,RemoteIp="93.184.216.34" } })
        {
            int lookups = 0;
            var t = new StubTransport { Handler = resolve => forbidden };
            var dns = new LoginDnsCache((host, ms, token) => { lookups++; return new[] { "93.184.216.35" }; });
            Check(Fails(() => Send(t, dns)).MayHaveBeenSent && t.Calls == 1 && lookups == 0,
                "remote_ip cannot bypass TLS/upload/unknown-metric replay prohibition");
        }
        int httpLookups = 0;
        var httpDns = new LoginDnsCache((host, ms, token) => { httpLookups++; return new[] { "93.184.216.35" }; });
        Check(Fails(() => Send(new StubTransport { Handler = resolve => Failed(28) }, httpDns, "http://login.test/")).MayHaveBeenSent && httpLookups == 0,
            "known remote_ip does not relax conservative HTTP behavior");
        bool warm = true; int fetches = 0;
        var refresh = new LoginDnsCache((host, ms, token) => { fetches++; return new[] { warm ? "93.184.216.34" : "93.184.216.35" }; });
        refresh.Resolve("login.test", 1000, CancellationToken.None, false); warm = false;
        var cached = new StubTransport { Handler = resolve => resolve == null ? Failed(60) : new LoginCurlResult { HttpCode = 200 } };
        Check(Send(cached, refresh).HttpCode == 200 && cached.Calls == 2 && fetches == 2,
            "cached same-peer answer is evicted and fetched once before the only real POST");
        using (var stall = new LocalServer())
        using (var good = new LocalServer(certificates.Correct))
        {
            string badUrl = "https://login.test:" + stall.Port + "/";
            var timed = CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs(badUrl, "POST", "", "fixture", "login.test:" + stall.Port + ":127.0.0.1", 1100), 1500, CancellationToken.None);
            var native = new NativeTransport(certificates.CaPath);
            string goodUrl = "https://login.test:" + good.Port + "/";
            var firstStalls = new StubTransport { Handler = resolve => resolve == null ? timed : native.Curl(goodUrl, "POST", "", "fixture", resolve, 3000, CancellationToken.None) };
            Check(timed.ExitCode == 28 && timed.RemoteIp == "127.0.0.1" && !timed.MayHaveBeenSent
                && (timed.CanRecover || timed.CanResolveAlternatePeer),
                "native stall supplies validated pre-send peer evidence (curl may report zero time_connect)");
            Check(Send(firstStalls, new LoginDnsCache((host, ms, token) => new[] { "93.184.216.34" }), goodUrl).HttpCode == 200
                && stall.Posts == 0 && good.Posts == 1,
                "native TCP-accepted TLS stall recovers through a different DoH peer with exactly one verified HTTPS POST");
        }
    }
    static void TestProcessCleanup()
    {
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(100); var clock = Stopwatch.StartNew();
            try { CurlHelper.ExecuteCommandForTest("/bin/sleep", "30", 1000, cancel.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { Check(clock.ElapsedMilliseconds < 2300, "real process kill/cancel returns inside the bounded cleanup grace"); }
        }
        string pidFile = Path.Combine(Path.GetTempPath(), "login-child-" + Guid.NewGuid().ToString("N"));
        try
        {
            string script = "import subprocess; p=subprocess.Popen(['/bin/sleep','30']); open('" + pidFile + "','w').write(str(p.pid))";
            var clock = Stopwatch.StartNew();
            try { CurlHelper.ExecuteCommandForTest("/usr/bin/python3", "-c \"" + script + "\"", 600, CancellationToken.None); throw new Exception("expected pipe timeout"); }
            catch (TimeoutException) { Check(clock.ElapsedMilliseconds < 2600, "inherited stdout/stderr handles cannot keep the login worker in unbounded WaitForExit"); }
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                using (var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))))
                { child.Kill(); child.WaitForExit(2000); }
                File.Delete(pidFile);
            }
        }
        using (var pending = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false }))
        using (var monitor = Process.GetProcessById(pending.Id))
        {
            CurlHelper.RememberPendingForTest(pending);
            var t = new StubTransport { Handler = resolve => new LoginCurlResult { HttpCode = 200 } };
            var dns = new LoginDnsCache((host, ms, token) => new string[0]);
            try
            {
                try { Send(t, dns); throw new Exception("uncertain curl allowed a later POST"); }
                catch (LoginTransportTimeoutException) { Check(t.Calls == 0, "unconfirmed curl termination blocks later credential attempts instead of releasing a second POST"); }
            }
            finally { monitor.Kill(); monitor.WaitForExit(2000); CurlHelper.EnsureCredentialCurlStopped(); }
            Check(Send(t, dns).HttpCode == 200 && t.Calls == 1,
                "credentials resume only after the retained process handle confirms exit");
        }
    }
    sealed class ReserveTransport : ILoginTransport
    {
        readonly NativeTransport native; internal int RetryMs;
        internal ReserveTransport(string ca) { native = new NativeTransport(ca); }
        public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeout, CancellationToken token)
        {
            if (resolve == null) { Thread.Sleep(3000); return new LoginCurlResult { ExitCode = 6, MetricsKnown = true }; }
            RetryMs = timeout; return native.Curl(url, method, headers, body, resolve, timeout, token);
        }
        public LoginCurlResult Managed(string url, string method, string headers, string body, int timeout, CancellationToken token)
            => throw new Exception("unexpected managed fallback");
    }
}
