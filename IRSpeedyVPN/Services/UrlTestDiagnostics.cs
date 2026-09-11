using System;
using System.Security.Cryptography;
using System.Text;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Services
{
    internal static class UrlTestDiagnostics
    {
        internal static string Describe(URLTestResp result)
        {
            if (result == null) return "result=missing";
            if (UrlTestRetryPolicy.IsSuccess(result))
                return "result=success latencyMs=" + result.LatencyMs;
            // Core errors may contain credentials or full outbound JSON. Log fixed
            // categories and a fingerprint, never the original error or config.
            return "result=failure latencyMs=" + result.LatencyMs
                + " reason=" + Classify(result.Error) + " errorId=" + Fingerprint(result.Error);
        }

        internal static string Classify(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "no-positive-latency";
            var text = error.ToLowerInvariant();
            if (text.Contains("no such host") || text.Contains("dns") || text.Contains("name resolution")) return "dns-resolution";
            if (text.Contains("certificate") || text.Contains("x509")) return "tls-certificate";
            if (text.Contains("auth") || text.Contains("password") || text.Contains("unauthorized")) return "authentication";
            if (text.Contains("tls") && (text.Contains("required") || text.Contains("missing"))) return "tls-configuration";
            if (text.Contains("quic") && text.Contains("handshake")) return "quic-handshake";
            if (text.Contains("tls") || text.Contains("crypto_error")) return "tls-handshake";
            if (text.Contains("timeout") || text.Contains("timed out") || text.Contains("deadline")) return "timeout";
            if (text.Contains("refused")) return "connection-refused";
            if (text.Contains("unreachable") || text.Contains("no route")) return "network-unreachable";
            if (text.Contains("cancel")) return "cancelled";
            if (text.Contains("quic")) return "quic-transport";
            if (text.Contains("outbound") || text.Contains("config") || text.Contains("unsupported")) return "configuration";
            if (text.Contains("reset") || text.Contains("closed") || text.Contains("eof")) return "transport-closed";
            return "unclassified-core-error";
        }

        private static string Fingerprint(string error)
        {
            if (string.IsNullOrEmpty(error)) return "none";
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(error)), 0, 6).Replace("-", "");
        }
    }
}
