using IRSpeedyVPN.Common;
using System;
using System.IO;
using System.Text;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class CurlHelper
    {
        private readonly string _preferredCurlPath;

        public CurlHelper(string curlExePath = null)
        {
            _preferredCurlPath = curlExePath;
        }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy = null,
            int? timeoutSeconds = null)
        {
            var curlPath = ResolveCurlPath();
            var config = BuildConfig(
                url,
                method,
                headers,
                body,
                proxy,
                timeoutSeconds);

            var timeoutMs = timeoutSeconds.HasValue && timeoutSeconds.Value > 0
                ? Math.Max(5000, timeoutSeconds.Value * 1000 + 5000)
                : 35000;

            var output = ShellExecute.ShellexecAndReturnStringOutputWithInput(
                curlPath,
                "--config -",
                config,
                timeoutMs);

            Parse(output, out var responseBody, out var httpCode);
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

        private string ResolveCurlPath()
        {
            if (!string.IsNullOrWhiteSpace(_preferredCurlPath)
                && File.Exists(_preferredCurlPath))
                return _preferredCurlPath;

            var runtimeRoot = Path.Combine(Path.GetTempPath(), "IRSpeedy");
            var activeVersionFile = Path.Combine(runtimeRoot, "active.version");
            try
            {
                if (File.Exists(activeVersionFile))
                {
                    var version = File.ReadAllText(activeVersionFile).Trim();
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        var versionedCurl = Path.Combine(
                            runtimeRoot,
                            "versions",
                            version,
                            "curl",
                            "curl.exe");
                        if (File.Exists(versionedCurl))
                            return versionedCurl;
                    }
                }
            }
            catch
            {
            }

            var legacyCurl = Path.Combine(runtimeRoot, "curl", "curl.exe");
            return File.Exists(legacyCurl) ? legacyCurl : "curl";
        }

        private static string BuildConfig(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds)
        {
            var config = new StringBuilder();
            config.AppendLine("silent");
            config.AppendLine("show-error");
            config.AppendLine("location");
            config.AppendLine("request = " + QuoteConfigValue(
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? "POST"
                    : "GET"));

            if (!string.IsNullOrWhiteSpace(proxy))
                config.AppendLine("proxy = " + QuoteConfigValue(proxy));

            if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0)
            {
                config.AppendLine("connect-timeout = " + timeoutSeconds.Value);
                config.AppendLine("max-time = " + timeoutSeconds.Value);
            }
            else
            {
                config.AppendLine("connect-timeout = 15");
                config.AppendLine("max-time = 30");
            }

            if (!string.IsNullOrWhiteSpace(headers))
            {
                var lines = headers.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                    config.AppendLine("header = " + QuoteConfigValue(line));
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && body != null)
                config.AppendLine("data-binary = " + QuoteConfigValue(body));

            config.AppendLine("write-out = " + QuoteConfigValue(
                "\r\n__HTTP_CODE__:%{http_code}"));
            config.AppendLine("url = " + QuoteConfigValue(url));
            return config.ToString();
        }

        private static string QuoteConfigValue(string value)
        {
            var safe = (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            return "\"" + safe + "\"";
        }

        private static void Parse(string output, out string body, out int code)
        {
            const string marker = "__HTTP_CODE__:";
            output = output ?? string.Empty;
            var index = output.LastIndexOf(marker, StringComparison.Ordinal);

            body = output;
            code = 0;
            if (index < 0)
                return;

            body = output.Substring(0, index).TrimEnd('\r', '\n');
            int.TryParse(
                output.Substring(index + marker.Length).Trim(),
                out code);
        }
    }
}
