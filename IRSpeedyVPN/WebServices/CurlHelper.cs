using IRSpeedyVPN.Common;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class CurlHelper
    {
        // Set once a launch has failed with "file not found", so the remaining requests
        // go straight to the managed stack instead of paying a failed CreateProcess each.
        private static volatile bool _curlUnavailable;

        /// <summary>
        /// True once curl has been found missing. Callers use it to skip work that only
        /// helps the curl path, such as resolving the host over DoH to pin it with
        /// --resolve, which the managed fallback cannot honour anyway.
        /// </summary>
        internal static bool IsUnavailable
        {
            get { return _curlUnavailable; }
        }

        public string CurlExePath { get; set; } = GetDefaultCurlPath();

        private static string GetDefaultCurlPath()
        {
            foreach (string candidate in CurlCandidates())
            {
                try
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }

            // Last resort: let CreateProcess search PATH. On Windows 7 and 8.1 there is
            // usually nothing to find, which is what HttpFallback is for.
            return "curl";
        }

        /// <summary>
        /// Full paths are preferred over bare "curl" because a damaged user PATH is one
        /// of the ways this went wrong in the field.
        /// </summary>
        private static string[] CurlCandidates()
        {
            string system = null;
            string windows = null;
            try
            {
                system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }
            catch
            {
            }

            return new[]
            {
                // Windows ships curl.exe from Windows 10 1803 onwards. This app builds as
                // x86, so on 64-bit Windows SpecialFolder.System is SysWOW64 and the file
                // system redirector sends a System32 path there too - Sysnative is the
                // only way a 32-bit process reaches the real System32.
                system == null ? null : Path.Combine(system, "curl.exe"),
                windows == null ? null : Path.Combine(windows, "Sysnative", "curl.exe"),

                // Kept for a build that ships its own copy; nothing populates it today.
                Path.Combine(Path.GetTempPath(), "IRSpeedy", "curl", "curl.exe"),
            };
        }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy=null)
        {
            return Send(url, method, headers, body, proxy, null, null);
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
            if (_curlUnavailable)
                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);

            string output;
            try
            {
                output = ShellExecute.ShellexecAndReturnStringOutput(
                    CurlExePath, BuildArgs(url, method, headers, body, proxy, timeoutSeconds, resolveOverride));
            }
            catch (Win32Exception ex)
            {
                // ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND: there is no curl.exe on
                // this machine, which is the normal state on Windows 7 and 8.1. Every
                // later request skips the launch entirely.
                if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
                {
                    _curlUnavailable = true;
                    LogHelper.WriteExLog(
                        "curl.exe is unavailable (" + CurlExePath + "); using the managed HTTP client instead.");
                }

                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
            }
            catch (Exception)
            {
                // Something else stopped the process from running - this once. Serve the
                // request from the managed stack without writing curl off for good.
                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
            }

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
