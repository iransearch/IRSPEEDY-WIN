using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.WebServices;

static class PrefetchChecks
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Result(int exit) => new LoginCurlResult { ExitCode = exit, MetricsKnown = true, HttpCode = exit == 0 ? 200 : 0 };
    static LoginCurlResult Send(ObservedTransport transport, LoginDnsCache cache)
        => new LoginRecovery(transport, cache, null).Send("https://login.test/", "POST", "", "credential", 8000, new LoginNetworkContext(CancellationToken.None));
    internal static void Run()
    {
        int queries = 0;
        var cache = new LoginDnsCache((host, timeout, token) => { Interlocked.Increment(ref queries); return new[] { "93.184.216.34" }; });
        var healthy = new ObservedTransport { DelayMs = 40, First = Result(0) };
        Send(healthy, cache);
        Check(queries == 0 && healthy.Posts == 1 && healthy.MaxActive == 1, "healthy login below one second has zero DoH requests including prefetch");

        queries = 0;
        cache = new LoginDnsCache((host, timeout, token) => { Interlocked.Increment(ref queries); return new[] { "93.184.216.34" }; });
        var lateSuccess = new ObservedTransport { DelayMs = 1400, First = Result(0) };
        Send(lateSuccess, cache);
        Check(queries == 1 && lateSuccess.Posts == 1 && lateSuccess.MaxActive == 1,
            "late first success sends one POST even when speculative DNS already completed");
        cache.Resolve("login.test", 1000, CancellationToken.None, false);
        Check(queries == 2, "successful first attempt discards completed prefetch instead of storing it in the five-minute cache");

        int active = 0; queries = 0;
        cache = new LoginDnsCache((host, timeout, token) =>
        {
            Interlocked.Increment(ref queries); Interlocked.Increment(ref active);
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return null; }
            finally { Interlocked.Decrement(ref active); }
        });
        var cancelSuccess = new ObservedTransport { DelayMs = 1400, First = Result(0) };
        Send(cancelSuccess, cache);
        Check(queries == 1 && active == 0 && cancelSuccess.Posts == 1, "late successful first POST cancels and joins in-flight prefetch");
        queries = 0;
        var headersSuccess = new ObservedTransport { DelayMs = 1500, HttpAfterMs = 1150, First = Result(0), DuringBody = () =>
        { Check(Volatile.Read(ref active) == 0, "HTTP response headers cancel prefetch while the first curl still reads its body"); } };
        Send(headersSuccess, cache);
        Check(queries == 1 && active == 0 && headersSuccess.Posts == 1, "response-triggered cancellation does not create a second credential attempt");

        foreach (int exit in new[] { 6, 7 })
        {
            queries = 0;
            cache = new LoginDnsCache((host, timeout, token) =>
            {
                Interlocked.Increment(ref queries);
                return FixtureDohProviders.Resolve(host, timeout, token, (url, ms, cancel) =>
                { if (url.Contains("1.1.1.1")) { cancel.WaitHandle.WaitOne(); cancel.ThrowIfCancellationRequested(); } return new[] { "93.184.216.34" }; }, null);
            });
            var failed = new ObservedTransport { DelayMs = 1400, First = Result(exit) };
            Send(failed, cache);
            var reused = cache.Resolve("login.test", 1000, CancellationToken.None, false);
            Check(queries == 1 && failed.Calls == 2 && failed.Posts == 1 && failed.MaxActive == 1 && reused.FromCache,
                "late exit " + exit + " consumes prefetch once, commits valid cache and never overlaps credential curl attempts");
        }
        queries = 0;
        cache = new LoginDnsCache((host, timeout, token) =>
        {
            Interlocked.Increment(ref queries);
            return FixtureDohProviders.Resolve(host, timeout, token, (url, ms, cancel) => new string[0], null);
        });
        var bothFail = new ObservedTransport { DelayMs = 1400, First = Result(6) };
        try { Send(bothFail, cache); throw new Exception("expected empty DNS failure"); } catch (LoginTransportException) { }
        Check(queries == 1 && bothFail.Calls == 1 && bothFail.Posts == 0, "both speculative providers failing cause no loop and no extra POST");

        queries = 0;
        cache = new LoginDnsCache((host, timeout, token) => { Interlocked.Increment(ref queries); return new[] { "93.184.216.34" }; });
        var connected = new ObservedTransport { DelayMs = 1400, First = Result(0), ConnectImmediately = true };
        Send(connected, cache);
        Check(queries == 0 && connected.Posts == 1, "slow response after a connected/TLS phase does not start prefetch");
        foreach (string ineligible in new[] { "http://login.test/", "https://93.184.216.34/" })
        {
            var ineligibleTransport = new ObservedTransport { DelayMs = 1100, First = Result(0) };
            new LoginRecovery(ineligibleTransport, cache, null).Send(ineligible, "POST", "", "fixture", 8000,
                new LoginNetworkContext(CancellationToken.None));
            Check(queries == 0, "HTTP and IP-literal targets do not start unusable speculative DoH");
        }
        queries = 0;
        cache = new LoginDnsCache((host, timeout, token) => { Interlocked.Increment(ref queries); return new[] { "93.184.216.34" }; });
        var ineligibleFailure = new ObservedTransport { DelayMs = 1400, First = Result(55) };
        try { Send(ineligibleFailure, cache); throw new Exception("expected non-CanRecover failure"); }
        catch (LoginTransportException ex) { Check(!ex.MayHaveBeenSent, "pre-TLS exit 55 remains eligible only for next-host failover"); }
        cache.Resolve("login.test", 1000, CancellationToken.None, false);
        Check(queries == 2 && ineligibleFailure.Calls == 1 && ineligibleFailure.Posts == 0,
            "non-CanRecover failure discards speculative answer without cache commit or same-host POST");
        TestNativeLosers(true);
        TestNativeLosers(false);
        using (var login = new LocalServer())
        {
            login.DelayMs = 200;
            var simultaneous = new ConcurrentTransport("http://127.0.0.1:" + login.Port + "/");
            Task.WaitAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                new LoginRecovery(simultaneous, cache, null).Send("https://login.test/", "POST", "", "fixture", 8000,
                    new LoginNetworkContext(CancellationToken.None)))).ToArray());
            Check(login.Posts == 2 && simultaneous.MaxActive == 1,
                "concurrent foreground/background logins serialize actual credential curl processes");
        }
    }
    static int[] CurlIds()
    {
        return Process.GetProcessesByName("curl").Select(process => { using (process) return process.Id; }).ToArray();
    }
    static void TestNativeLosers(bool nativeLoser)
    {
        using (var slow = new LocalServer())
        using (var fast = new LocalServer())
        {
            slow.DelayMs = 1500; int active = 0;
            var before = CurlIds();
            var result = FixtureDohProviders.Resolve("login.test", 2000, CancellationToken.None, (provider, timeout, token) =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    bool loser = provider.Contains("8.8.8.8");
                    if (!loser)
                    {
                        var waiting = Stopwatch.StartNew();
                        while (Volatile.Read(ref slow.Gets) == 0 && waiting.ElapsedMilliseconds < 600)
                        { token.ThrowIfCancellationRequested(); Thread.Sleep(5); }
                    }
                    string url = "http://127.0.0.1:" + (loser ? slow.Port : fast.Port) + "/resolve";
                    string json;
                    if (loser && !nativeLoser)
                        json = HttpFallback.Send(url, "GET", "", null, null, timeout, token).Body;
                    else
                        json = CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs(url, "GET", "", null, null, timeout - 100), timeout, token).Body;
                    return CurlHelper.ParseLoginAddresses(json);
                }
                finally { Interlocked.Decrement(ref active); }
            }, null);
            Check(result.Length > 0 && active == 0 && !CurlIds().Except(before).Any(),
                "winner cancels and joins real " + (nativeLoser ? "curl" : ".NET") + " loser without a child process or active provider worker");
        }
    }
    sealed class ObservedTransport : ILoginConnectTransport
    {
        internal int DelayMs, HttpAfterMs, Calls, Posts, MaxActive, active;
        internal LoginCurlResult First;
        internal bool ConnectImmediately;
        internal Action DuringBody;
        private void Begin() { Calls++; MaxActive = Math.Max(MaxActive, ++active); }
        public LoginCurlResult CurlObserved(string url, string method, string headers, string body,
            int timeout, CancellationToken token, LoginConnectProgress progress)
        {
            Begin(); progress.Start();
            if (ConnectImmediately) progress.Observe("* Connected to login.test");
            try
            {
                if (HttpAfterMs > 0)
                {
                    if (token.WaitHandle.WaitOne(HttpAfterMs)) token.ThrowIfCancellationRequested();
                    progress.Observe("< HTTP/1.1 200 OK");
                }
                if (token.WaitHandle.WaitOne(DelayMs - HttpAfterMs)) token.ThrowIfCancellationRequested();
                DuringBody?.Invoke();
                if (First.HttpCode != 0) Posts++; return First;
            }
            finally { active--; }
        }
        public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int timeout, CancellationToken token)
        { Begin(); try { if (resolve == null) throw new Exception("unobserved initial call"); Posts++; return Result(0); } finally { active--; } }
        public LoginCurlResult Managed(string url, string method, string headers, string body, int timeout, CancellationToken token)
            => throw new Exception("unexpected managed POST");
    }
    sealed class ConcurrentTransport : ILoginTransport
    {
        readonly string url; int active; internal int MaxActive;
        internal ConcurrentTransport(string url) { this.url = url; }
        public LoginCurlResult Curl(string ignoredUrl, string method, string headers, string body, string resolve, int timeout, CancellationToken token)
        {
            int current = Interlocked.Increment(ref active);
            if (current > 1) Interlocked.Exchange(ref MaxActive, 2);
            else Interlocked.CompareExchange(ref MaxActive, 1, 0);
            try { return CurlHelper.ExecuteLocal(CurlHelper.BuildLoginArgs(url, method, headers, body, null, timeout - 100), timeout, token); }
            finally { Interlocked.Decrement(ref active); }
        }
        public LoginCurlResult Managed(string url, string method, string headers, string body, int timeout, CancellationToken token)
            => throw new Exception("unexpected managed fallback");
    }
}
