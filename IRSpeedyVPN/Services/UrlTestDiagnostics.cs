using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
            // Preserve useful diagnostic words, but never emit arbitrary core data.
            return "result=failure latencyMs=" + result.LatencyMs
                + " reason=" + Classify(result.Error) + " errorId=" + Fingerprint(result.Error)
                + " errorDetail=\"" + SafeDetail(result.Error) + "\"";
        }


        // Use an allowlist rather than relying on secret-key names: core errors can
        // contain arbitrary passwords, URLs, JSON or server-provided response text.
        private static readonly HashSet<string> DiagnosticWords = new HashSet<string>(
            ("get head post http https request response status code unexpected invalid failed failure error " +
             "connect connection connecting dial tcp udp quic tls handshake certificate x509 expired " +
             "timeout timed out deadline exceeded context canceled cancelled operation aborted " +
             "no recent network activity received packets packet idle keepalive closed reset refused " +
             "unreachable route host remote local peer server client transport stream eof broken pipe " +
             "read write send receive resolve resolver dns lookup name resolution such address " +
             "authentication authenticated unauthorized forbidden password required missing unsupported " +
             "protocol version configuration config outbound inbound proxy socks hysteria hysteria2 " +
             "obfs salamander mismatch malformed bad unknown internal application crypto buffer " +
             "resource temporarily unavailable permission denied access not allowed cannot unable " +
             "to from by for with without is was has been the a an of on in during after before " +
             "establish open close socket networkidle readfrom writeto use io end file " +
             "too many requests service unavailable successful success empty returned expected " +
             "headers header body length short overflow underflow limit reached refused_stream " +
             "connection_error protocol_error internal_error handshake_failure " +
             "network_unreachable connection_refused").Split(' '),
            StringComparer.OrdinalIgnoreCase);

        internal static string SafeDetail(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "no error text";
            // Bound work independently of the size of a malformed core response.
            var text = error.Length > 4096 ? error.Substring(0, 4096) : error;
            // Discard structured payloads and quoted values before token filtering.
            int payload = text.IndexOfAny(new[] { '{', '[' });
            if (payload >= 0) text = text.Substring(0, payload) + " REDACTED";
            text = Regex.Replace(text, "\\\"[^\\\"]*(?:\\\"|$)|'[^']*(?:'|$)", " REDACTED ");
            text = Regex.Replace(text, @"\S*(?:://|@|=)\S*", " REDACTED ");
            var status = Regex.Match(text, @"\bstatus(?: code)?\s*:?\s+([1-5][0-9]{2})\b", RegexOptions.IgnoreCase);
            var output = new StringBuilder();
            bool redacted = false;
            foreach (Match token in Regex.Matches(text, @"[^\s:;,()<>]+"))
            {
                string word = token.Value.TrimEnd('.');
                bool known = DiagnosticWords.Contains(word);
                if (!known && redacted) continue;
                string value = known ? word.ToLowerInvariant() : "<redacted>";
                if (output.Length + value.Length + 1 > 384) break;
                if (output.Length > 0) output.Append(' ');
                output.Append(value);
                redacted = !known;
            }
            if (status.Success) output.Append(" httpStatus=").Append(status.Groups[1].Value);
            return output.Length == 0 ? "<redacted>" : output.ToString();
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
