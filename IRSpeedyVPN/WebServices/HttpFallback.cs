using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    /// <summary>
    /// Sends the same request CurlHelper would, using the framework's own HTTP stack.
    /// Only used when curl.exe cannot be launched at all: Windows ships curl in
    /// System32 from Windows 10 1803 onwards, so on Windows 7 and 8.1 - and on any
    /// machine where it was removed or blocked - Process.Start fails with
    /// "The system cannot find the file specified" and login could never proceed.
    ///
    /// Deliberately not a general replacement for curl: it uses the system resolver,
    /// so the DoH address pinning that --resolve gives us is lost here. That trade is
    /// worth making only because the alternative on these machines is no login at all.
    /// </summary>
    internal static class HttpFallback
    {
        private static bool _protocolsConfigured;

        internal static CurlHelper.CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutMilliseconds)
        {
            if (timeoutMilliseconds.HasValue && timeoutMilliseconds.Value <= 0)
                throw new TimeoutException("The HTTP request has no remaining budget.");

            EnsureModernTlsEnabled();

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                ? "POST"
                : "GET";

            // curl -L
            request.AllowAutoRedirect = true;

            // curl reads neither the IE settings nor WinHTTP's, and while connected the
            // app points the system proxy at its own listener - going through
            // WebRequest's default proxy would send login into the tunnel it is trying
            // to bring up, or into a stale proxy left behind by another app.
            request.Proxy = string.IsNullOrWhiteSpace(proxy) ? null : new WebProxy(proxy);

            if (timeoutMilliseconds.HasValue && timeoutMilliseconds.Value > 0)
            {
                int ms = timeoutMilliseconds.Value;
                request.Timeout = ms;
                request.ReadWriteTimeout = ms;
            }

            ApplyHeaders(request, headers);

            // Timeout/ReadWriteTimeout cover individual operations, not the full
            // upload + headers + response body. Abort also bounds a slow/dripping body.
            int deadlineExpired = 0;
            int deadlineMs = timeoutMilliseconds.HasValue && timeoutMilliseconds.Value > 0
                ? timeoutMilliseconds.Value
                : Timeout.Infinite;
            using (var deadline = new Timer(_ =>
            {
                Interlocked.Exchange(ref deadlineExpired, 1);
                try { request.Abort(); }
                catch { }
            }, null, deadlineMs, Timeout.Infinite))
            {
                try
                {
                    if (request.Method == "POST")
                    {
                        byte[] payload = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);
                        request.ContentLength = payload.Length;
                        using (var stream = request.GetRequestStream())
                        {
                            stream.Write(payload, 0, payload.Length);
                        }
                    }

                    var response = ReadResponse(request);
                    if (Volatile.Read(ref deadlineExpired) != 0)
                        throw new TimeoutException("The HTTP request deadline expired.");
                    return response;
                }
                catch (Exception ex) when (Volatile.Read(ref deadlineExpired) != 0)
                {
                    throw new TimeoutException("The HTTP request deadline expired.", ex);
                }
            }
        }

        /// <summary>
        /// curl reports the status code through -w and returns the body either way, so a
        /// 4xx or 5xx must come back as a normal response here rather than an exception.
        /// </summary>
        private static CurlHelper.CurlResponse ReadResponse(HttpWebRequest request)
        {
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return new CurlHelper.CurlResponse(ReadBody(response), (int)response.StatusCode);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null)
                    throw;

                using (response)
                {
                    return new CurlHelper.CurlResponse(ReadBody(response), (int)response.StatusCode);
                }
            }
        }

        private static string ReadBody(HttpWebResponse response)
        {
            var stream = response.GetResponseStream();
            if (stream == null)
                return string.Empty;

            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// The header block is built as text for curl's -H, so it is split back apart
        /// here. Several names are reserved by HttpWebRequest and throw if added to the
        /// collection, so those are routed to their properties instead.
        /// </summary>
        private static void ApplyHeaders(HttpWebRequest request, string headers)
        {
            if (string.IsNullOrEmpty(headers))
                return;

            string[] lines = headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int sep = line.IndexOf(':');
                if (sep <= 0)
                    continue;

                string name = line.Substring(0, sep).Trim();
                string value = line.Substring(sep + 1).Trim();
                if (name.Length == 0)
                    continue;

                try
                {
                    if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase))
                        request.UserAgent = value;
                    else if (string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase))
                        request.Accept = value;
                    else if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        request.ContentType = value;
                    else if (string.Equals(name, "Referer", StringComparison.OrdinalIgnoreCase))
                        request.Referer = value;
                    else if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
                        request.Host = value;
                    else if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(name, "Expect", StringComparison.OrdinalIgnoreCase))
                        continue; // managed by the stack itself
                    else
                        request.Headers.Add(name, value);
                }
                catch
                {
                    // A header the stack refuses is not worth failing the request over.
                }
            }
        }

        /// <summary>
        /// Windows 7 and 8.1 negotiate SSL 3.0 / TLS 1.0 by default, which these
        /// endpoints refuse. .NET 4.8 normally defers to the OS default, so ask for
        /// TLS 1.2 explicitly. A machine without the TLS 1.2 update installed cannot be
        /// helped from here, hence the guard: requesting a protocol the platform does
        /// not know throws, and the older set is better than no request at all.
        /// </summary>
        private static void EnsureModernTlsEnabled()
        {
            if (_protocolsConfigured)
                return;

            try
            {
                // TLS 1.3 is left out on purpose: Windows 7 and 8.1 cannot negotiate it,
                // and these are the machines this path exists for.
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12
                    | SecurityProtocolType.Tls11
                    | SecurityProtocolType.Tls;
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls; }
                catch { }
            }

            _protocolsConfigured = true;
        }
    }
}
