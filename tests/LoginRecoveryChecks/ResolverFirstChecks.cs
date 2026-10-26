using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.WebServices;

static class ResolverFirstChecks
{
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Unsent(int exit = 7) => new LoginCurlResult { ExitCode = exit, MetricsKnown = true };
    static LoginCurlResult Ok() => new LoginCurlResult { HttpCode = 200, UploadedBytes = 177, MetricsKnown = true };
    static LoginCurlResult Send(ILoginTransport transport, ILoginResolver resolver, List<string> logs = null,
        string url = "https://login.test/", LoginNetworkContext context = null)
        => new LoginRecovery(transport, resolver, logs == null ? (Action<string>)null : logs.Add, order: LoginDnsOrder.ResolverFirst)
            .Send(url, "POST", "", "credential-must-not-be-logged", 8000,
                context ?? new LoginNetworkContext(CancellationToken.None));
    static LoginTransportException Fails(Action action)
    { try { action(); } catch (LoginTransportException ex) { return ex; } throw new Exception("expected transport failure"); }
    static void Canceled(Action action)
    { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("expected cancellation"); }
    internal static void Run(Certificates certificates)
    {
        Check(CurlHelper.LoginResolutionOrder == LoginDnsOrder.ResolverFirst, "production login uses the final resolver-first policy without experiment switches");
        Native(certificates);
        Policy();
        Deadlines();
    }
    static void Native(Certificates certificates)
    {
        using (var server = new LocalServer(certificates.Correct))
        using (var google = new LocalServer())
        using (var cloudflare = new LocalServer())
        {
            string url = "https://login.test:" + server.Port + "/";
            google.Json = "{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"127.0.0.1\"}]}";
            var logs = new List<string>();
            var dns = new LoginDnsCache((host, ms, token) => FixtureDohProviders.Resolve(host, ms, token,
                (provider, budget, stop) =>
                {
                    bool isGoogle = provider.Contains("8.8.8.8");
                    if (!isGoogle && stop.WaitHandle.WaitOne(40)) stop.ThrowIfCancellationRequested();
                    var reply = HttpFallback.Send("http://127.0.0.1:" + (isGoogle ? google.Port : cloudflare.Port)
                        + "/resolve", "GET", "Accept: application/dns-json\r\n", null, null, budget, stop);
                    return CurlHelper.ParseLoginAddresses(reply.Body);
                }, logs.Add));
            var native = new NativeTransport(certificates.CaPath);
            Check(Send(native, dns, logs, url).HttpCode == 200 && server.Posts == 1
                && native.Resolves.Count == 1 && native.Resolves[0] != null
                && server.LastHost == "login.test:" + server.Port,
                "DoH-first race reaches verified native HTTPS via --resolve with one POST and original Host/SNI");
            Check(logs.Any(line => line.Contains("provider=cloudflare outcome=winner"))
                && logs.Any(line => line.Contains("stage=resolver-first-result resolve=True"))
                && !logs.Any(line => line.Contains("stage=system-fallback")),
                "DoH-first success records the valid provider winner and never starts system DNS");
            foreach (int status in new[] { 401, 406, 500 })
            {
                server.Status = status; int before = server.Posts, attempts = native.Resolves.Count;
                Check(Send(native, dns, logs, url).HttpCode == status && server.Posts == before + 1
                    && native.Resolves.Count == attempts + 1 && native.Resolves.Last() != null,
                    "DoH-first HTTP " + status + " does not replay its POST through system DNS");
            }
            server.Status = 200; server.Drop = true;
            int posted = server.Posts, calls = native.Resolves.Count;
            Check(Fails(() => Send(native, dns, logs, url)).MayHaveBeenSent
                && server.Posts == posted + 1 && native.Resolves.Count == calls + 1,
                "native connection drop after the DoH POST forbids system-DNS replay");
            server.Drop = false;

            int queries = 0; native = new NativeTransport(certificates.CaPath); posted = server.Posts;
            var wrong = new LoginDnsCache((host, ms, token) => { queries++; return new[] { "93.184.216.36" }; });
            Check(Send(native, wrong, logs, url).HttpCode == 200 && server.Posts == posted + 1
                && queries == 1 && native.Resolves.Count == 2 && native.Resolves[0] != null && native.Resolves[1] == null,
                "failed DoH peer before sending falls back once to system DNS and sends exactly one real POST");
            foreach (bool freshWorks in new[] { true, false })
            {
                queries = 0; native = new NativeTransport(certificates.CaPath); posted = server.Posts;
                var cached = new LoginDnsCache((host, ms, token) =>
                    new[] { ++queries > 1 && freshWorks ? "93.184.216.34" : "93.184.216.36" });
                cached.Resolve("login.test", 1000, CancellationToken.None, false);
                Check(Send(native, cached, logs, url).HttpCode == 200 && queries == 2 && server.Posts == posted + 1
                    && native.Resolves.Count == (freshWorks ? 2 : 3)
                    && (native.Resolves.Last() == null) == !freshWorks,
                    "DoH-first evicts a failed cached peer and refreshes once before " + (freshWorks ? "DoH success" : "system fallback"));
            }
            Check(logs.All(line => !line.Contains("credential-must-not-be-logged"))
                && logs.All(IRSpeedyVPN.Common.LogPolicy.ShouldWrite), "DoH-first diagnostics survive filtering without credentials");
        }
        using (var wrong = new LocalServer(certificates.Wrong))
        {
            var native = new NativeTransport(certificates.CaPath);
            var error = Fails(() => Send(native, new LoginDnsCache((host, ms, token) => new[] { "93.184.216.34" }),
                url: "https://login.test:" + wrong.Port + "/"));
            Check(wrong.Posts == 0 && native.Resolves.Count == 2 && native.Resolves.Last() == null
                && native.Results.All(result => result.ExitCode == 60) && error.Message.Contains("Resolver first:"),
                "wrong-domain certificates remain rejected in both DNS orders with zero POSTs and initial failure preserved");
        }
    }
    static void Policy()
    {
        foreach (bool lookupThrows in new[] { false, true })
        {
            int lookups = 0;
            var transport = new RecordingTransport { Handler = (resolve, ms, token) => Unsent(6) };
            var dns = new LoginDnsCache((host, ms, token) =>
            { lookups++; if (lookupThrows) throw new Exception("credential-must-not-be-logged"); return new string[0]; });
            var logs = new List<string>();
            Check(!Fails(() => Send(transport, dns, logs)).MayHaveBeenSent && transport.Resolves.SequenceEqual(new string[] { null })
                && lookups == 1 && !logs.Any(line => line.Contains("credential-must-not-be-logged")),
                "DoH lookup " + (lookupThrows ? "exception" : "no-answer") + " uses system once without looping back into DoH");
        }
        foreach (var posted in new[]
        {
            new LoginCurlResult { ExitCode = 28, MetricsKnown = true, TlsSeconds = .1 },
            new LoginCurlResult { ExitCode = 56, MetricsKnown = true, UploadedBytes = 1 },
            new LoginCurlResult { ExitCode = 28, MetricsKnown = true, HttpCode = 200, UploadedBytes = 177 },
            new LoginCurlResult { ExitCode = 6, MetricsKnown = false }
        })
        {
            var transport = new RecordingTransport { Handler = (resolve, ms, token) => posted };
            Check(Fails(() => Send(transport, Answers())).MayHaveBeenSent && transport.Resolves.Count == 1
                && transport.Resolves[0] != null && transport.ObservedCalls == 0,
                "DoH-first ambiguous/posted or partial HTTP failure blocks all system-DNS replay (exit " + posted.ExitCode + ")");
        }
        var systemPartial = new RecordingTransport { Handler = (resolve, ms, token) =>
            new LoginCurlResult { ExitCode = 28, HttpCode = 200, UploadedBytes = 177, MetricsKnown = true } };
        Check(Fails(() => Send(systemPartial, new LoginDnsCache((host, ms, token) => new string[0]))).MayHaveBeenSent
            && systemPartial.Resolves.Count == 1, "partial system HTTP after empty DoH also stays replay-blocking");
        foreach (bool unavailable in new[] { false, true })
        {
            var transport = new RecordingTransport { Handler = (resolve, ms, token) =>
                new LoginCurlResult { Unavailable = true } };
            var dns = new LoginDnsCache((host, ms, token) => unavailable ? new[] { "93.184.216.34" } : new string[0]);
            Check(Send(transport, dns).HttpCode == 200 && transport.ManagedCalls == 1,
                "DoH-first permits one managed POST only when curl cannot launch (answer=" + unavailable + ")");
        }
        int endpoints = 0;
        var ctx = new LoginNetworkContext(CancellationToken.None);
        var sent = new RecordingTransport { Handler = (resolve, ms, token) => throw new TimeoutException("unknown stage") };
        try
        {
            new ControllerHarness().Run((host, timeout) => { endpoints++; Send(sent, Answers(), url: host.BaseAddress, context: ctx); return null; }, ctx);
            throw new Exception("unknown timeout accepted");
        }
        catch (LoginTransportTimeoutException) { Check(endpoints == 1, "unknown DoH-first credential timeout cannot fail over to another API"); }

        var cache = Answers(); cache.Resolve("login.test", 1000, CancellationToken.None, false);
        int retries = 0;
        var unknownRefresh = new RecordingTransport { Handler = (resolve, ms, token) =>
        { if (++retries == 1) return Unsent(7); throw new TimeoutException("fixture"); } };
        try { Send(unknownRefresh, cache); throw new Exception("unknown refresh accepted"); }
        catch (LoginTransportTimeoutException ex)
        { Check(retries == 2 && ex.Message.Contains("exit=7"), "cached failure remains in the final unknown-stage refresh timeout without a system POST"); }

        endpoints = 0; int hostnameLookups = 0;
        var offline = new RecordingTransport { Handler = (resolve, ms, token) => endpoints == 1 ? Unsent(6) : Ok() };
        var noAnswers = new LoginDnsCache((host, ms, token) => { hostnameLookups++; return new string[0]; });
        var flow = new LoginNetworkContext(CancellationToken.None);
        var response = new ControllerHarness().Run((host, timeout) =>
        {
            endpoints++; var result = Send(offline, noAnswers, url: host.BaseAddress, context: flow);
            return new BaseHttpResponse<object> { StatusCode = (HttpStatusCode)result.HttpCode };
        }, flow);
        Check(response.StatusCode == HttpStatusCode.OK && endpoints == 2 && hostnameLookups == 2
            && offline.Resolves.Count == 2 && offline.Resolves.All(resolve => resolve == null),
            "DoH-first lookup and unsent system failure advance to the next API without a DNS loop");

        int active = 0, max = 0;
        var serialized = new RecordingTransport { Handler = (resolve, ms, token) =>
        {
            int count = Interlocked.Increment(ref active); max = Math.Max(max, count);
            try { Thread.Sleep(40); return Ok(); } finally { Interlocked.Decrement(ref active); }
        } };
        Task.WaitAll(Task.Run(() => Send(serialized, Answers())), Task.Run(() => Send(serialized, Answers())));
        Check(max == 1 && serialized.Resolves.Count == 2, "concurrent DoH-first credential attempts share the existing single-POST gate");
    }
    static void Deadlines()
    {
        int lookups = 0, budget = 0, postBudget = 0;
        var transport = new RecordingTransport { Handler = (resolve, ms, token) => { postBudget = ms; return Ok(); } };
        Send(transport, new LoginDnsCache((host, ms, token) => { lookups++; budget = ms; return new[] { "93.184.216.34" }; }));
        Check(budget > 0 && budget <= 5000 && postBudget >= 3000 && postBudget <= 8000 && transport.ObservedCalls == 0,
            "DoH-first keeps the five-second lookup cap, three-second POST reserve and disables speculative prefetch");
        foreach (bool expired in new[] { false, true })
        using (var cancel = new CancellationTokenSource())
        {
            lookups = 0; transport = new RecordingTransport { Handler = (resolve, ms, token) => Ok() };
            if (!expired) cancel.Cancel();
            var context = new LoginNetworkContext(cancel.Token, expired ? 0 : 30000);
            try { Send(transport, new LoginDnsCache((host, ms, token) => { lookups++; return new string[0]; }), context: context); throw new Exception("deadline ignored"); }
            catch (OperationCanceledException) { Check(!expired && lookups == 0 && transport.Resolves.Count == 0, "canceled DoH-first login starts no lookup or POST"); }
            catch (TimeoutException) { Check(expired && lookups == 0 && transport.Resolves.Count == 0, "expired DoH-first login starts no lookup or POST"); }
        }
        using (var cancel = new CancellationTokenSource(50))
        {
            transport = new RecordingTransport { Handler = (resolve, ms, token) => Ok() };
            Canceled(() => Send(transport, new LoginDnsCache((host, ms, token) =>
            { token.WaitHandle.WaitOne(1000); token.ThrowIfCancellationRequested(); return new string[0]; }),
                context: new LoginNetworkContext(cancel.Token)));
            Check(transport.Resolves.Count == 0, "cancel during the initial DoH lookup never starts a system POST");
        }
    }
    static LoginDnsCache Answers() => new LoginDnsCache((host, ms, token) => new[] { "93.184.216.34" });
    sealed class RecordingTransport : ILoginConnectTransport
    {
        internal readonly List<string> Resolves = new List<string>();
        internal Func<string, int, CancellationToken, LoginCurlResult> Handler;
        internal int ManagedCalls, ObservedCalls;
        public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve, int ms, CancellationToken token)
        { lock (Resolves) Resolves.Add(resolve); return Handler(resolve, ms, token); }
        public LoginCurlResult CurlObserved(string url, string method, string headers, string body, int ms, CancellationToken token, LoginConnectProgress progress)
        { ObservedCalls++; progress.Start(); return Curl(url, method, headers, body, null, ms, token); }
        public LoginCurlResult Managed(string url, string method, string headers, string body, int ms, CancellationToken token)
        { ManagedCalls++; return Ok(); }
    }
}
