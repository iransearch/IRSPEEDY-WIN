using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class DnsFailureChecks
{
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }

    // Exact -w metrics from the bundled Windows curl 8.21.0 user report.
    static LoginCurlResult Captured()
        => CurlHelper.ParseLoginResult("__HTTP_CODE__:000\n__LOGIN_CONNECT__:0.000000\n"
            + "__LOGIN_TLS__:0.000000\n__LOGIN_READY__:0.017777\n__LOGIN_UPLOAD__:0\n"
            + "__LOGIN_REDIRECTS__:0\n__LOGIN_HTTP__:000\n__LOGIN_REMOTE__:\n", 6);
    static LoginCurlResult Send(LoginRecovery recovery, string url = "https://login.test/", LoginNetworkContext context = null)
        => recovery.Send(url, "POST", "", "credential-must-not-be-logged", 8000,
            context ?? new LoginNetworkContext(CancellationToken.None));
    static LoginTransportException Fails(Action action)
    {
        try { action(); } catch (LoginTransportException ex) { return ex; }
        throw new Exception("expected transport failure");
    }
    internal static void Run(Certificates certificates)
    {
        var captured = Captured();
        Check(captured.MetricsKnown && captured.MayHaveBeenSent && captured.DefinitelyNotSent
            && captured.CanRecover && !captured.MayHaveBeenSentFor(true) && captured.MayHaveBeenSentFor(false),
            "Windows exit 6 with positive pretransfer overrides only the effective HTTPS replay decision");
        foreach (var mutation in new Dictionary<string, Action<LoginCurlResult>>
        {
            { "unknown metrics", r => r.MetricsKnown = false },
            { "TCP connection", r => r.ConnectSeconds = .001 },
            { "TLS completion", r => r.TlsSeconds = .001 },
            { "upload", r => r.UploadedBytes = 1 },
            { "HTTP response", r => r.HttpCode = 200 },
            { "redirect", r => r.Redirects = 1 },
            { "known peer", r => r.RemoteIp = "93.184.216.34" }
        })
        {
            var result = Captured(); mutation.Value(result);
            int lookups = 0;
            var transport = new StubTransport { Handler = resolve => result };
            var recovery = new LoginRecovery(transport, new LoginDnsCache((host, ms, token) =>
            { lookups++; return new[] { "93.184.216.34" }; }), null);
            if (result.HttpCode > 0) Send(recovery);
            else Check(Fails(() => Send(recovery)).MayHaveBeenSent, "failure stays conservative for " + mutation.Key);
            Check(!result.DefinitelyNotSent && !result.CanRecover && result.MayHaveBeenSentFor(true)
                && transport.Calls == 1 && lookups == 0, "DNS override rejects " + mutation.Key);
        }
        var unavailable = Captured(); unavailable.Unavailable = true;
        Check(!unavailable.DefinitelyNotSent && !unavailable.CanRecover, "unavailable curl cannot claim DNS-stage proof");
        foreach (int code in new[] { 7, 35, 60, 28, 52, 55, 56 })
        {
            var result = Captured(); result.ExitCode = code;
            Check(!result.DefinitelyNotSent && !result.CanRecover && result.MayHaveBeenSentFor(true),
                "positive-pretransfer override is not extended to exit " + code);
        }
        var partial = CurlHelper.ParseLoginResult("__LOGIN_CONNECT__:0\n__LOGIN_TLS__:0\n", 6);
        Check(!partial.MetricsKnown && !partial.DefinitelyNotSent && !partial.CanRecover,
            "partial native trailer cannot authorize DNS recovery through default-zero fields");

        var logs = new List<string>();
        int dns = 0;
        var http = new StubTransport { Handler = resolve => Captured() };
        var httpFailure = Fails(() => Send(new LoginRecovery(http, new LoginDnsCache((host, ms, token) =>
        { dns++; return new[] { "93.184.216.34" }; }), logs.Add), "http://login.test/"));
        Check(httpFailure.MayHaveBeenSent && http.Calls == 1 && dns == 0,
            "plain HTTP retains its previous positive-pretransfer policy and never invokes DoH");

        using (var server = new LocalServer(certificates.Correct))
        {
            var native = new NativeTransport(certificates.CaPath);
            var transport = new StubTransport { Handler = resolve => resolve == null ? Captured()
                : native.Curl("https://login.test:" + server.Port + "/", "POST", "", "fixture",
                    resolve, 4000, CancellationToken.None) };
            var recovery = new LoginRecovery(transport, new LoginDnsCache((host, ms, token) =>
            { dns++; return new[] { "93.184.216.34" }; }), logs.Add);
            Check(Send(recovery, "https://login.test:" + server.Port + "/").HttpCode == 200
                && server.Posts == 1 && transport.Calls == 2 && dns == 1,
                "captured Windows DNS failure recovers with one certificate-verified native HTTPS POST");
        }

        // Exercise the real Failure() and extracted production controller together.
        foreach (bool retryCurl in new[] { false, true })
        {
            int endpoints = 0, calls = 0, lookups = 0;
            var context = new LoginNetworkContext(CancellationToken.None);
            var transport = new StubTransport { Handler = resolve =>
            { calls++; return endpoints == 1 ? Captured() : new LoginCurlResult { HttpCode = 200 }; } };
            var resolver = new LoginDnsCache((host, ms, token) =>
            { lookups++; return retryCurl ? new[] { "93.184.216.34" } : new string[0]; });
            var recovery = new LoginRecovery(transport, resolver, logs.Add);
            var response = new ControllerHarness().Run((host, timeout) =>
            {
                endpoints++; var result = Send(recovery, host.BaseAddress, context);
                return new BaseHttpResponse<object> { StatusCode = (HttpStatusCode)result.HttpCode };
            }, context);
            Check(response.StatusCode == HttpStatusCode.OK && endpoints == 2 && calls == (retryCurl ? 3 : 2) && lookups == 1,
                "controller advances once after " + (retryCurl ? "DoH credential retry also fails DNS" : "DoH has no valid answer"));
        }
        int attempts = 0;
        var posted = new StubTransport { Handler = resolve =>
        {
            attempts++; var result = Captured();
            if (resolve != null) { result.ExitCode = 28; result.TlsSeconds = .1; result.UploadedBytes = 177; }
            return result;
        } };
        Check(Fails(() => Send(new LoginRecovery(posted, new LoginDnsCache((host, ms, token) =>
            new[] { "93.184.216.34" }), logs.Add))).MayHaveBeenSent && attempts == 2,
            "an unsent initial DNS failure never overrides evidence of a sent recovery POST");
        Check(logs.Any(line => line.Contains("mayHaveBeenSent=True definitelyNotSent=True"))
            && logs.Any(line => line.Contains("effectiveMayHaveBeenSent=False"))
            && logs.All(line => !line.Contains("credential-must-not-be-logged")),
            "logs distinguish conservative metrics from effective HTTPS DNS proof without credentials");
    }
}
