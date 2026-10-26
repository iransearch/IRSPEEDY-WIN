using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class TransferDiagnosticChecks
{
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static string Trailer => "__HTTP_CODE__:200\n__LOGIN_CONNECT__:0.1\n__LOGIN_TLS__:0.2\n"
        + "__LOGIN_READY__:0.21\n__LOGIN_UPLOAD__:177\n__LOGIN_REDIRECTS__:0\n__LOGIN_HTTP__:200\n"
        + "__LOGIN_REMOTE__:2001:4860:4860::8888\n";
    static string Diagnostics => "__LOGIN_FIRST_BYTE__:0.321\n__LOGIN_TOTAL__:7.8\n"
        + "__LOGIN_DOWNLOAD__:1234\n__LOGIN_VERSION__:2\n";
    internal static void Run(Certificates certificates)
    {
        string body = "secret-response\n__LOGIN_FIRST_BYTE__:99\n__LOGIN_TOTAL__:99\n"
            + "__LOGIN_DOWNLOAD__:99999\n__LOGIN_VERSION__:secret-version\n";
        var parsed = CurlHelper.ParseLoginResult(body + Trailer + Diagnostics, 28);
        Check(parsed.TransferMetricsKnown && parsed.FirstByteSeconds == .321 && parsed.TotalSeconds == 7.8
            && parsed.DownloadedBytes == 1234 && parsed.HttpVersion == "2" && parsed.Body == body.TrimEnd('\n'),
            "final transfer trailer overrides body markers without changing the captured response body");
        Check(parsed.Summary.Contains("remoteIp=2001:4860:4860::8888 remoteFamily=IPv6")
            && parsed.Summary.Contains("firstByte=0.321 total=7.8 download=1234 httpVersion=2")
            && parsed.Summary.Contains("bodyChars=" + parsed.Body.Length)
            && !parsed.Summary.Contains("secret-response") && !parsed.Summary.Contains("secret-version"),
            "transfer summary records canonical IPv6, HTTP and byte/timing scalars without response content");
        var before = CurlHelper.ParseLoginResult(Trailer, 28);
        Check(!before.TransferMetricsKnown && before.FirstByteSeconds == null
            && before.Summary.Contains("firstByte=unknown") && before.MetricsKnown == parsed.MetricsKnown
            && before.MayHaveBeenSent == parsed.MayHaveBeenSent && before.CanRecover == parsed.CanRecover,
            "missing optional transfer fields are unknown and do not alter replay decisions");
        foreach (string invalid in new[] { "NaN", "Infinity", "-1", "secret-value" })
        {
            var bad = CurlHelper.ParseLoginResult(Trailer + "__LOGIN_FIRST_BYTE__:" + invalid + "\n"
                + "__LOGIN_TOTAL__:7.8\n__LOGIN_DOWNLOAD__:" + invalid + "\n__LOGIN_VERSION__:secret-value\n", 28);
            Check(!bad.TransferMetricsKnown && bad.FirstByteSeconds == null && bad.DownloadedBytes == null
                && bad.TotalSeconds == 7.8 && bad.HttpVersion == null && !bad.Summary.Contains(invalid)
                && bad.MetricsKnown && bad.MayHaveBeenSent,
                "invalid optional transfer values remain unknown and cannot leak arbitrary text (" + invalid + ")");
        }
        var unsafeText = new LoginCurlResult { RemoteIp = "credential-must-not-be-logged", HttpVersion = "secret-version", Body = "secret-body" };
        Check(unsafeText.Summary.Contains("remoteIp=unknown remoteFamily=unknown")
            && unsafeText.Summary.Contains("httpVersion=unknown") && !unsafeText.Summary.Contains("credential")
            && !unsafeText.Summary.Contains("secret"), "diagnostic IP and protocol formatters reject arbitrary strings");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var invariant = CurlHelper.ParseLoginResult(Trailer + Diagnostics, 28);
            Check(invariant.TransferMetricsKnown && invariant.Summary.Contains("firstByte=0.321 total=7.8"),
                "transfer metrics parse and log with invariant decimal formatting");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        using (var server = new LocalServer(certificates.Correct))
        {
            string url = "https://login.test:" + server.Port + "/";
            var native = new NativeTransport(certificates.CaPath);
            var success = native.Curl(url, "POST", "", "fixture", "login.test:" + server.Port + ":93.184.216.34", 2000, CancellationToken.None);
            Check(success.ExitCode == 0 && success.TransferMetricsKnown && success.DownloadedBytes == 11
                && success.Body.Length == 11 && success.HttpVersion == "1.1" && success.FirstByteSeconds > 0
                && success.FirstByteSeconds <= success.TotalSeconds && success.Summary.Contains("remoteIp=127.0.0.1 remoteFamily=IPv4"),
                "real verified HTTPS success produces transfer timing, exact byte count, HTTP version and IPv4 peer");
            foreach (int bytes in new[] { 0, 1 })
            {
                server.BodyDelayMs = 1400; server.PartialBodyBytes = bytes;
                int posts = server.Posts;
                var logs = new List<string>();
                var transport = new StubTransport { Handler = resolve => native.Curl(url, "POST", "", "fixture", resolve, 750, CancellationToken.None) };
                LoginTransportException failure = null;
                try
                {
                    new LoginRecovery(transport, new LoginDnsCache((host, ms, token) => new[] { "93.184.216.34" }), logs.Add, order: LoginDnsOrder.ResolverFirst)
                        .Send(url, "POST", "", "fixture", 8000, new LoginNetworkContext(CancellationToken.None));
                }
                catch (LoginTransportException ex) { failure = ex; }
                var timeout = native.Results.Last();
                Check(failure != null && failure.MayHaveBeenSent && timeout.ExitCode == 28 && timeout.HttpCode == 200
                    && timeout.TransferMetricsKnown && timeout.DownloadedBytes == bytes && timeout.Body.Length == bytes
                    && timeout.FirstByteSeconds > 0 && timeout.FirstByteSeconds < timeout.TotalSeconds
                    && server.Posts == posts + 1 && transport.Calls == 1,
                    "native HTTP 200 timeout exposes " + bytes + " received body bytes without replaying the POST");
                Check(logs.Any(line => line.Contains("stage=resolver-first-result") && line.Contains("transferMetrics=True")
                    && line.Contains("download=" + bytes) && line.Contains("httpVersion=1.1") && line.Contains("bodyChars=" + bytes))
                    && !logs.Any(line => line.Contains("stage=system-fallback")),
                    "production DoH result log distinguishes header-only/partial-body timeout (bytes=" + bytes + ")");
            }
        }
    }
}
