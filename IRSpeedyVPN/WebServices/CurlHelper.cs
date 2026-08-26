using IRSpeedyVPN.Common;
using System;
using System.IO;
using System.Text;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class CurlHelper
    {
        public string CurlExePath { get; set; } = GetDefaultCurlPath();

        private static string GetDefaultCurlPath()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "IRSpeedy");
            string extractedCurl = Path.Combine(tempRoot, "curl", "curl.exe");
            return File.Exists(extractedCurl) ? extractedCurl : "curl";
        }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy=null)
        {
            string output = ShellExecute.ShellexecAndReturnStringOutput(CurlExePath, BuildArgs(url, method, headers, body, proxy, null, null));
            Parse(output, out string responseBody, out int httpCode);

            return new CurlResponse(responseBody, httpCode);
        }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds,
            string resolveOverride = null)
        {
            string output = ShellExecute.ShellexecAndReturnStringOutput(CurlExePath, BuildArgs(url, method, headers, body, proxy, timeoutSeconds, resolveOverride));
            Parse(output, out string responseBody, out int httpCode);
            return new CurlResponse(responseBody, httpCode);
        }

        internal sealed class CurlResponse
        {
            public CurlResponse(string body, int httpCode)
            {
                Body = body;
                HttpCode = httpCode;
            }

            public string Body { get; }
            public int HttpCode { get; }
        }

        private static string BuildArgs(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds,
            string resolveOverride)
        {
            var args = new StringBuilder();
            args.Append(" -sS -L ");

            bool isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            if (isPost)
                args.Append(" -X POST ");

            // DoH-resolved address: pin the host to this IP so a poisoned system
            // resolver is bypassed while SNI and the Host header stay intact.
            if (!string.IsNullOrWhiteSpace(resolveOverride))
            {
                args.Append(" --resolve ");
                args.Append(EscapeArg(resolveOverride));
            }

            if (!string.IsNullOrWhiteSpace(proxy))
            {
                args.Append(" --proxy ");
                args.Append(EscapeArg(proxy));
            }

            if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0)
            {
                args.Append(" --connect-timeout ");
                args.Append(timeoutSeconds.Value);
                args.Append(" --max-time ");
                args.Append(timeoutSeconds.Value);
                args.Append(" ");
            }

            // Headers
            if (!string.IsNullOrEmpty(headers))
            {
                string[] lines = headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    args.Append(" -H ");
                    args.Append(EscapeArg(line));
                }
            }

            // Body
            if (isPost && body != null)
            {
                args.Append(" --data ");
                args.Append(EscapeArg(body));
            }

            string writeOut = "\r\n__HTTP_CODE__:%{http_code}";

            args.Append(" -w ");
            args.Append(EscapeArg(writeOut));

            // URL
            args.Append(" ");
            args.Append(EscapeArg(url));

            return args.ToString();
        }

        private static void Parse(string output, out string body, out int code)
        {
            const string marker = "__HTTP_CODE__:";
            int idx = output.LastIndexOf(marker, StringComparison.Ordinal);

            body = output;
            code = 0;

            if (idx >= 0)
            {
               
                body = output.Substring(0, idx).TrimEnd('\r', '\n');
                int.TryParse(output.Substring(idx + marker.Length).Trim(), out code);
            }
        }

        private static string EscapeArg(string value)
        {
            // Quote for Windows command-line.
            // Keep it simple: escape backslashes and quotes.
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
