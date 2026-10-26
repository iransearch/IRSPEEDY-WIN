using IRSpeedyVPN.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal sealed partial class CurlHelper
    {
        private static readonly LoginDohCache LoginDns = new LoginDohCache(FetchLoginAddresses);
        internal CurlResponse SendLogin(string url, string method, string headers, string body,
            int timeoutMs, LoginNetworkContext context)
        {
            var recovery = new LoginRecovery(new LoginTransport(this), LoginDns, LogHelper.WriteExLog);
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
                    if (progress != null) args += " --verbose";
                    var result = ExecuteCurl(candidate.Path, args, remainingMs, out started, cancellation,
                        progress == null ? (Action)null : progress.Start,
                        progress == null ? (Action<string>)null : progress.Observe);
                    _curlUnavailable = false;
                    return ParseLoginResult(result.Output, result.ExitCode);
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
            string seconds = (timeoutMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
            return args + " --noproxy \"*\" --connect-timeout "
                + Math.Min(3d, timeoutMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture)
                + " --max-time " + seconds
                + " -w \"\\n" + MarkerHttpCode + "%{http_code}\\n__LOGIN_CONNECT__:%{time_connect}\\n__LOGIN_TLS__:%{time_appconnect}"
                + "\\n__LOGIN_READY__:%{time_pretransfer}\\n__LOGIN_UPLOAD__:%{size_upload}"
                + "\\n__LOGIN_REDIRECTS__:%{num_redirects}\\n__LOGIN_HTTP__:%{http_code}\\n\"";
        }
        internal static LoginCurlResult ParseLoginResult(string output, int exitCode)
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
            int boundary = output.LastIndexOf(MarkerHttpCode, StringComparison.Ordinal);
            result.Body = boundary < 0 ? "" : output.Substring(0, boundary).TrimEnd('\r', '\n');
            return result;
        }
        private static bool Metric(string output, string marker, out double value)
            => double.TryParse(ParseTrailingValue(output, marker), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
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
}
