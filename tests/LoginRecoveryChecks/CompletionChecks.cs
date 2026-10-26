using System;
using System.Linq;
using System.Threading;
using IRSpeedyVPN.WebServices;

static class CompletionChecks
{
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static LoginCurlResult Headers(params string[] lines)
    {
        var observer = new LoginResponseDiagnostics();
        foreach (string line in lines) observer.Observe(line);
        return CurlHelper.ParseLoginResult("{\"ok\":true}\n__HTTP_CODE__:200\n__LOGIN_CONNECT__:0.1\n"
            + "__LOGIN_TLS__:0.2\n__LOGIN_READY__:0.21\n__LOGIN_UPLOAD__:177\n__LOGIN_REDIRECTS__:0\n"
            + "__LOGIN_HTTP__:200\n__LOGIN_REMOTE__:127.0.0.1\n__LOGIN_FIRST_BYTE__:0.3\n"
            + "__LOGIN_TOTAL__:7.8\n__LOGIN_DOWNLOAD__:11\n__LOGIN_VERSION__:1.1\n", 28, observer);
    }
    internal static void Run(Certificates certificates)
    {
        string post = CurlHelper.BuildLoginArgs("https://login.test/", "POST", "", "fixture", null, 1000);
        string get = CurlHelper.BuildLoginArgs("https://8.8.8.8/resolve", "GET", "", null, null, 1000);
        Check(post.Contains(" --http1.1 -H \"Connection: close\" --verbose")
            && !post.Contains(" -L ") && !post.Contains("--insecure")
            && !get.Contains("Connection: close") && !get.Contains("--http1.1") && !get.Contains("--verbose"),
            "completion trial is scoped to native credential POSTs without changing DoH GETs or TLS/redirect checks");
        var final = Headers("< HTTP/1.1 100 Continue", "< Content-Length: 999", "< ",
            "< HTTP/1.1 200 OK", "< Content-Type: Application/Json; charset=UTF-8; secret=do-not-log",
            "< Content-Length: 11, 11", "< Connection: close", "< Set-Cookie: credential-must-not-be-logged", "< ");
        Check(final.Summary.Contains("headersKnown=True contentType=application/json contentLength=11")
            && final.Summary.Contains("responseFraming=content-length downloadVsLength=equal")
            && final.Summary.Contains("bodyKind=json-parseable") && !final.Summary.Contains("999")
            && !final.Summary.Contains("do-not-log") && !final.Summary.Contains("credential"),
            "only sanitized final response headers survive interim 100 and secret cookie/type parameters");
        foreach (string value in new[] { "-1", "9223372036854775808", "secret-value", "11,12" })
        {
            var bad = Headers("< HTTP/1.1 200 OK", "< Content-Length: " + value,
                "< Content-Type: secret-value", "< Connection: secret-value", "< Transfer-Encoding: secret-value", "< ");
            Check(bad.Summary.Contains("contentLength=invalid-or-conflicting") && !bad.Summary.Contains(value)
                && bad.MetricsKnown && bad.MayHaveBeenSent && !bad.CanRecover,
                "malformed response metadata is redacted and cannot affect replay evidence (" + value + ")");
        }
        var conflict = Headers("< HTTP/1.1 200 OK", "< Content-Length: 11", "< Content-Length: 12", "< ");
        Check(conflict.Summary.Contains("responseFraming=invalid-length"), "conflicting content lengths are diagnostic errors");
        var chunk = Headers("< HTTP/1.1 200 OK", "< Transfer-Encoding: gzip, chunked", "< Content-Length: 11", "< ");
        Check(chunk.Summary.Contains("responseFraming=chunked downloadVsLength=unknown framingConflict=True"),
            "transfer coding overrides length comparison while the conflicting framing headers remain visible");
        var incomplete = Headers("< HTTP/1.1 200 OK", "< Content-Length: 11");
        Check(incomplete.Summary.Contains("headersKnown=False"), "an incomplete header block is not reported as known framing");
        Check(LoginResponseDiagnostics.ClassifyBody("\uFEFF \r\n<!DOCTYPE html><html>secret</html>") == "html-prefix"
            && LoginResponseDiagnostics.ClassifyBody("{\"secret\":") == "json-unparseable"
            && LoginResponseDiagnostics.ClassifyBody("opaque-encrypted-response") == "other"
            && LoginResponseDiagnostics.ClassifyBody(new string(' ', 262145)) == "over-limit"
            && LoginResponseDiagnostics.ClassifyBody(new string('[', 65) + new string(']', 65)) == "json-unparseable",
            "body classification is bounded and distinguishes HTML, incomplete JSON and opaque encrypted content without logging it");
        Native(certificates);
    }
    static void Native(Certificates certificates)
    {
        using (var server = new LocalServer(certificates.Correct))
        {
            server.CloseDelimited = true; server.HonorClose = true; server.HoldOpenMs = 1400;
            string url = "https://login.test:" + server.Port + "/";
            string args = CurlHelper.BuildLoginArgs(url, "POST", "", "fixture", "login.test:" + server.Port + ":127.0.0.1", 650)
                .Replace(" -H \"Connection: close\"", "") + " --cacert \"" + certificates.CaPath + "\"";
            // A separate control action reproduces a complete JSON body whose
            // HTTP close-delimited message has not ended. Never an automatic retry.
            var control = CurlHelper.ExecuteLocal(args, 900, CancellationToken.None);
            Check(control.ExitCode == 28 && control.HttpCode == 200 && control.DownloadedBytes == 11
                && control.Summary.Contains("responseFraming=close-delimited") && control.BodyKind == "json-parseable"
                && control.MayHaveBeenSent && server.Posts == 1,
                "real keep-alive close-delimited control times out after a complete JSON body and remains replay-blocking");
            var native = new NativeTransport(certificates.CaPath);
            var success = new LoginRecovery(native, new LoginDohCache((host, ms, token) => new[] { "93.184.216.34" }), null, dohFirst: true)
                .Send(url, "POST", "", "fixture", 8000, new LoginNetworkContext(CancellationToken.None));
            Check(success.ExitCode == 0 && success.HttpCode == 200 && server.LastConnection == "close"
                && server.LastProtocol == "HTTP/1.1" && native.Resolves.Count == 1 && native.Resolves[0] != null
                && success.Summary.Contains("connection=close responseFraming=close-delimited") && server.Posts == 2,
                "connection-close DoH-first trial completes the same verified HTTPS response with exactly one new POST");
        }
        foreach (bool missingChunk in new[] { false, true })
        using (var server = new LocalServer(certificates.Correct))
        {
            server.Chunked = missingChunk; server.MissingLastChunk = missingChunk;
            server.DeclaredLengthDelta = missingChunk ? 0 : 5;
            var native = new NativeTransport(certificates.CaPath);
            LoginTransportException failure = null;
            try
            {
                new LoginRecovery(native, new LoginDohCache((host, ms, token) => new[] { "93.184.216.34" }), null, dohFirst: true)
                    .Send("https://login.test:" + server.Port + "/", "POST", "", "fixture", 8000, new LoginNetworkContext(CancellationToken.None));
            }
            catch (LoginTransportException ex) { failure = ex; }
            var result = native.Results.Last();
            Check(failure != null && failure.MayHaveBeenSent && result.ExitCode != 0 && result.HttpCode == 200
                && result.BodyKind == "json-parseable" && native.Resolves.Count == 1 && server.Posts == 1
                && result.Summary.Contains(missingChunk ? "responseFraming=chunked" : "downloadVsLength=below"),
                "connection-close never accepts or replays complete JSON with " + (missingChunk ? "a missing terminal chunk" : "a mismatched Content-Length"));
        }
    }
}
