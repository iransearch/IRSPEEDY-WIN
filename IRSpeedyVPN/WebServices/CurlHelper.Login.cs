using IRSpeedyVPN.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal sealed partial class CurlHelper
    {
        private static readonly LoginDohCache LoginDns = new LoginDohCache(FetchLoginAddresses);
        // Temporary verification build: resolve via DoH before using system DNS.
        internal const bool LoginDohFirstVerification = true;
        internal CurlResponse SendLogin(string url, string method, string headers, string body,
            int timeoutMs, LoginNetworkContext context)
        {
            var recovery = new LoginRecovery(new LoginTransport(this), LoginDns, LogHelper.WriteExLog,
                dohFirst: LoginDohFirstVerification);
            var result = recovery.Send(url, method, headers, body, timeoutMs, context);
            return new CurlResponse(result.Body, result.HttpCode);
        }
        private sealed class LoginTransport : ILoginConnectTransport
        {
            private readonly CurlHelper owner;
            internal LoginTransport(CurlHelper owner) { this.owner = owner; }
            public LoginCurlResult Curl(string url, string method, string headers, string body,
                string resolve, int timeoutMs, CancellationToken cancellation)
                => owner.SendLoginAttempt(url, method, headers, body, resolve, timeoutMs, cancellation);
            public LoginCurlResult CurlObserved(string url, string method, string headers, string body,
                int timeoutMs, CancellationToken cancellation, LoginConnectProgress progress)
                => owner.SendLoginAttempt(url, method, headers, body, null, timeoutMs, cancellation, progress);
            public LoginCurlResult Managed(string url, string method, string headers, string body,
                int timeoutMs, CancellationToken cancellation)
            {
                var response = HttpFallback.Send(url, method, headers, body, null, timeoutMs, cancellation, loginRequest: true);
                return new LoginCurlResult { Body = response.Body, HttpCode = response.HttpCode };
            }
        }
        internal LoginCurlResult SendLoginAttempt(string url, string method, string headers, string body,
            string resolve, int timeoutMs, CancellationToken cancellation, LoginConnectProgress progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) EnsureCredentialCurlStopped();
            if (timeoutMs <= 100) throw new TimeoutException("Login attempt has no remaining budget.");
            var elapsed = Stopwatch.StartNew();
            var diagnostic = SafeCaptureDiagnostics(url, method);
            var candidates = ResolveOrderedCandidates(diagnostic);
            foreach (var candidate in candidates)
            {
                ushort machine;
                string reason;
                if (!IsExistingFile(candidate.Path) || !TryReadPeMachine(candidate.Path, out machine, out reason)
                    || machine != candidate.ExpectedMachine) continue;
                bool started = false;
                try
                {
                    // Native max-time leaves a small stdout-flush margin inside the
                    // hard process deadline. A killed process has unknown metrics.
                    int remainingMs = timeoutMs - (int)Math.Min(int.MaxValue, elapsed.ElapsedMilliseconds);
                    if (remainingMs <= 100) throw new TimeoutException("Login curl launch deadline expired.");
                    string args = BuildLoginArgs(url, method, headers, body, resolve, remainingMs - 100);
                    if (progress != null && !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) args += " --verbose";
                    var responseHeaders = new LoginResponseDiagnostics();
                    var outputProgress = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                        ? new LoginOutputProgress() : null;
                    string attemptId = outputProgress == null ? null : Guid.NewGuid().ToString("N");
                    Action<string> outputLog = outputProgress == null ? (Action<string>)null : (sample =>
                        LogHelper.WriteExLog("[LoginRecovery] stage=credential-output attempt=" + attemptId
                            + " host=" + new Uri(url).Host + " " + sample));
                    var result = ExecuteCurl(candidate.Path, args, remainingMs, out started, cancellation,
                        progress == null ? (Action)null : progress.Start,
                        line => { progress?.Observe(line); responseHeaders.Observe(line); },
                        outputProgress, outputLog);
                    _curlUnavailable = false;
                    return ParseLoginResult(result.Output, result.ExitCode, responseHeaders);
                }
                catch (OperationCanceledException) { if (started) _curlUnavailable = false; throw; }
                catch (TimeoutException)
                {
                    if (!started) throw;
                    _curlUnavailable = false;
                    throw new LoginTransportTimeoutException();
                }
                catch
                {
                    if (started)
                    {
                        _curlUnavailable = false;
                        return new LoginCurlResult { ExitCode = -1, MetricsKnown = false };
                    }
                }
            }
            _curlUnavailable = true;
            return new LoginCurlResult { Unavailable = true, ExitCode = -1 };
        }
        internal static string BuildLoginArgs(string url, string method, string headers, string body,
            string resolve, int timeoutMs)
        {
            // BuildArgs retains the original URL, Host/SNI, certificate validation
            // and explicit --resolve. Login must not silently follow redirects:
            // metrics from a later connection cannot prove the first POST was unsent.
            string args = BuildArgs(url, method, headers, body, null, null, resolve, followRedirects: false);
            // Temporary completion trial for credential POSTs only. DoH GETs and
            // ordinary curl callers retain their previous protocol/connection policy.
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                args += " --http1.1 -H \"Connection: close\" --verbose --no-buffer";
            string seconds = (timeoutMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
            return args + " --noproxy \"*\" --connect-timeout "
                + Math.Min(3d, timeoutMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture)
                + " --max-time " + seconds
                + " -w \"\\n" + MarkerHttpCode + "%{http_code}\\n__LOGIN_CONNECT__:%{time_connect}\\n__LOGIN_TLS__:%{time_appconnect}"
                + "\\n__LOGIN_READY__:%{time_pretransfer}\\n__LOGIN_UPLOAD__:%{size_upload}"
                + "\\n__LOGIN_REDIRECTS__:%{num_redirects}\\n__LOGIN_HTTP__:%{http_code}\\n__LOGIN_REMOTE__:%{remote_ip}"
                + "\\n__LOGIN_FIRST_BYTE__:%{time_starttransfer}\\n__LOGIN_TOTAL__:%{time_total}"
                + "\\n__LOGIN_DOWNLOAD__:%{size_download}\\n__LOGIN_VERSION__:%{http_version}\\n\"";
        }
        internal static LoginCurlResult ParseLoginResult(string output, int exitCode, LoginResponseDiagnostics responseHeaders = null)
        {
            var result = new LoginCurlResult { ExitCode = exitCode };
            double connect, tls, ready, upload;
            int redirects, http;
            result.MetricsKnown = Metric(output, "__LOGIN_CONNECT__:", out connect)
                & Metric(output, "__LOGIN_TLS__:", out tls)
                & Metric(output, "__LOGIN_READY__:", out ready)
                & Metric(output, "__LOGIN_UPLOAD__:", out upload);
            result.MetricsKnown &= int.TryParse(ParseTrailingValue(output, "__LOGIN_REDIRECTS__:"), out redirects) && redirects >= 0;
            result.MetricsKnown &= int.TryParse(ParseTrailingValue(output, "__LOGIN_HTTP__:"), out http) && http >= 0;
            result.ConnectSeconds = connect; result.TlsSeconds = tls;
            result.PretransferSeconds = ready; result.UploadedBytes = upload;
            result.Redirects = redirects; result.HttpCode = http;
            IPAddress remote;
            if (IPAddress.TryParse(ParseTrailingValue(output, "__LOGIN_REMOTE__:"), out remote))
                result.RemoteIp = remote.ToString();
            result.FirstByteSeconds = OptionalMetric(output, "__LOGIN_FIRST_BYTE__:");
            result.TotalSeconds = OptionalMetric(output, "__LOGIN_TOTAL__:");
            result.DownloadedBytes = OptionalMetric(output, "__LOGIN_DOWNLOAD__:");
            string version = ParseTrailingValue(output, "__LOGIN_VERSION__:");
            if (LoginCurlResult.IsKnownHttpVersion(version)) result.HttpVersion = version;
            int boundary = output.LastIndexOf(MarkerHttpCode, StringComparison.Ordinal);
            result.Body = boundary < 0 ? "" : output.Substring(0, boundary).TrimEnd('\r', '\n');
            result.BodyKind = LoginResponseDiagnostics.ClassifyBody(result.Body);
            responseHeaders?.Apply(result);
            return result;
        }
        private static bool Metric(string output, string marker, out double value)
            => double.TryParse(ParseTrailingValue(output, marker), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        private static double? OptionalMetric(string output, string marker)
            => Metric(output, marker, out var value) ? (double?)value : null;
        private static string[] FetchLoginAddresses(string host, int timeoutMs, CancellationToken cancellation)
            => LoginDohProviders.Resolve(host, timeoutMs, cancellation, FetchLoginProvider, LogHelper.WriteExLog);

        private static string[] FetchLoginProvider(string url, int timeoutMs, CancellationToken cancellation)
        {
            var elapsed = Stopwatch.StartNew();
            var response = new CurlHelper().SendLoginAttempt(
                url,
                "GET", "Accept: application/dns-json\r\n", null, null, timeoutMs, cancellation);
            // This GET contains no credentials. Managed fallback is still restricted
            // to curl launch failures, with the same caller-supplied deadline.
            if (response.Unavailable)
            {
                int remainingMs = timeoutMs - (int)Math.Min(int.MaxValue, elapsed.ElapsedMilliseconds);
                if (remainingMs <= 0) throw new TimeoutException("DoH provider deadline expired.");
                var fallback = HttpFallback.Send(url,
                    "GET", "Accept: application/dns-json\r\n", null, null, remainingMs, cancellation);
                response.HttpCode = fallback.HttpCode; response.Body = fallback.Body;
            }
            if (response.HttpCode != 200) return new string[0];
            return ParseLoginAddresses(response.Body);
        }
        internal static string[] ParseLoginAddresses(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object status, answers;
            if (root == null || !root.TryGetValue("Status", out status) || status == null || Convert.ToString(status, CultureInfo.InvariantCulture) != "0"
                || !root.TryGetValue("Answer", out answers)) return new string[0];
            var records = answers as IEnumerable;
            var values = new List<string>();
            if (records != null) foreach (var record in records)
            {
                var item = record as Dictionary<string, object>;
                object type, data;
                if (item != null && item.TryGetValue("type", out type) && Convert.ToString(type) == "1"
                    && item.TryGetValue("data", out data) && data != null && LoginDohCache.IsPublicIPv4(data.ToString().Trim()))
                    values.Add(data.ToString().Trim());
            }
            return values.ToArray();
        }
    }

    // Only numeric transfer activity is kept. No response bytes, credentials,
    // headers or URLs enter a progress sample. The curl -w trailer may add a
    // final stdout chunk, so periodic samples distinguish it from body progress.
    internal sealed class LoginOutputProgress
    {
        private long characters, firstAtMs = -1, lastAtMs = -1;
        internal void Observe(int count, long elapsedMs)
        {
            if (count <= 0) return;
            Interlocked.CompareExchange(ref firstAtMs, elapsedMs, -1);
            Interlocked.Add(ref characters, count);
            Interlocked.Exchange(ref lastAtMs, elapsedMs);
        }
        internal string Snapshot(long elapsedMs)
        {
            long first = Interlocked.Read(ref firstAtMs), last = Interlocked.Read(ref lastAtMs);
            return "elapsedMs=" + elapsedMs + " stdoutChars=" + Interlocked.Read(ref characters)
                + " firstChunkMs=" + first + " lastChunkMs=" + last
                + " idleMs=" + (last < 0 ? -1 : Math.Max(0, elapsedMs - last));
        }
    }

    // Receives transient curl verbose lines on stderr. Only fixed categories and
    // a numeric length survive; no raw line/header value is stored or logged.
    internal sealed class LoginResponseDiagnostics
    {
        private readonly object gate = new object();
        private bool inHeaders, complete, sawLength;
        private int responseStatus;
        private long? length;
        private string lengthState = "absent", type = "absent", transfer = "absent", connection = "absent";
        internal void Observe(string line)
        {
            if (line == null || !line.StartsWith("< ", StringComparison.Ordinal)) return;
            lock (gate)
            {
                if (line.StartsWith("< HTTP/", StringComparison.Ordinal))
                {
                    inHeaders = false;
                    int start = line.IndexOf(' ', 2), status;
                    if (start < 0 || line.Length < start + 4
                        || !int.TryParse(line.Substring(start + 1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out status)
                        || status < 200 || status > 599) return;
                    complete = false; inHeaders = true; sawLength = false; length = null;
                    responseStatus = status;
                    lengthState = type = transfer = connection = "absent";
                    return;
                }
                if (!inHeaders) return;
                if (line.Trim() == "<") { complete = true; inHeaders = false; return; }
                int colon = line.IndexOf(':', 2);
                if (colon < 0 || colon > 64) return;
                string name = line.Substring(2, colon - 2).Trim().ToLowerInvariant();
                if (name != "content-type" && name != "content-length" && name != "transfer-encoding" && name != "connection") return;
                string value = line.Length <= 4096 ? line.Substring(colon + 1).Trim().ToLowerInvariant() : "";
                if (name == "content-type")
                { string media = ContentType(value); type = type == "absent" || type == media ? media : "other"; }
                else if (name == "content-length")
                {
                    long parsed;
                    bool valid = SameLength(value, out parsed);
                    if (!valid || (sawLength && (lengthState != "valid" || length != parsed)))
                    { lengthState = "invalid-or-conflicting"; length = null; }
                    else { lengthState = "valid"; length = parsed; }
                    sawLength = true;
                }
                else if (name == "transfer-encoding")
                {
                    string tokens = SafeTokens(value, new[] { "chunked", "gzip", "deflate", "compress", "identity" });
                    transfer = transfer == "absent" ? tokens : transfer == "other" || tokens == "other" || transfer.Length + tokens.Length > 128
                        ? "other" : transfer + "," + tokens;
                }
                else
                {
                    string tokens = SafeTokens(value, new[] { "close", "keep-alive", "upgrade" });
                    connection = connection == "close" ? "close" : tokens == "other" ? "other" : ("," + tokens + ",").Contains(",close,") ? "close"
                        : ("," + tokens + ",").Contains(",keep-alive,") ? "keep-alive" : "upgrade";
                }
            }
        }
        private static bool SameLength(string value, out long length)
        {
            length = 0;
            var values = value.Split(',');
            if (values.Length > 8) return false;
            for (int i = 0; i < values.Length; i++)
            {
                long parsed;
                if (!long.TryParse(values[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                    || (i > 0 && parsed != length)) return false;
                length = parsed;
            }
            return true;
        }
        private static string SafeTokens(string value, string[] allowed)
        {
            var tokens = value.Split(',');
            if (tokens.Length == 0 || tokens.Length > 8) return "other";
            for (int i = 0; i < tokens.Length; i++)
            { tokens[i] = tokens[i].Trim(); if (Array.IndexOf(allowed, tokens[i]) < 0) return "other"; }
            return string.Join(",", tokens);
        }
        private static string ContentType(string value)
        {
            string media = value.Split(';')[0].Trim();
            switch (media)
            {
                case "application/json": case "text/json": case "text/html":
                case "text/plain": case "application/octet-stream": case "application/problem+json": return media;
                default: return media.StartsWith("application/", StringComparison.Ordinal) && media.EndsWith("+json", StringComparison.Ordinal)
                    ? "other-json" : "other";
            }
        }
        internal void Apply(LoginCurlResult result)
        {
            lock (gate)
            {
                if (!complete) { result.ResponseHeadersSummary = "headersKnown=False"; return; }
                bool noBody = responseStatus == 204 || responseStatus == 304;
                string framing = noBody ? "no-body" : transfer != "absent" ? transfer == "chunked" || transfer.EndsWith(",chunked", StringComparison.Ordinal)
                    ? "chunked" : "transfer-coded-or-unknown" : lengthState == "valid" ? "content-length"
                    : lengthState == "absent" ? "close-delimited" : "invalid-length";
                string comparison = "unknown";
                if (!noBody && transfer == "absent" && length.HasValue && result.DownloadedBytes.HasValue)
                    comparison = result.DownloadedBytes.Value < length.Value ? "below" : result.DownloadedBytes.Value == length.Value ? "equal" : "above";
                result.ResponseHeadersSummary = "headersKnown=True contentType=" + type
                    + " contentLength=" + (length.HasValue ? length.Value.ToString(CultureInfo.InvariantCulture) : lengthState)
                    + " transferEncoding=" + transfer + " connection=" + connection
                    + " responseFraming=" + framing + " downloadVsLength=" + comparison
                    + " framingConflict=" + (transfer != "absent" && sawLength);
            }
        }
        internal static string ClassifyBody(string body)
        {
            if (body == null) return "unknown";
            if (body.Length > 262144) return "over-limit";
            string text = body.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (text.Length == 0) return "empty";
            if (text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "html-prefix";
            if (text[0] != '{' && text[0] != '[') return "other"; // e.g. an opaque/encrypted body
            try
            {
                new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 64 }.Deserialize<object>(text);
                return "json-parseable";
            }
            catch { return "json-unparseable"; } // Exception text can contain response data: never log it.
        }
    }
}
