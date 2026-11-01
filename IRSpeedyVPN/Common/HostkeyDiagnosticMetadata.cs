using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    // Separate allowlist: Hostkey diagnostics never relax the general Core filter.
    internal static class HostkeyDiagnosticMetadata
    {
        internal static bool TryParse(string line, out string safe)
        {
            safe = null;
            const string prefix = "[CoreDiagnostic] ";
            if (line == null || line.Length > 1024 || !line.StartsWith(prefix, StringComparison.Ordinal)
                || line.IndexOf(" event=hostkey-", StringComparison.Ordinal) < 0
                || line.Any(c => char.IsControl(c))) return false;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in line.Substring(prefix.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int split = part.IndexOf('=');
                if (split <= 0) return false;
                string key = part.Substring(0, split), value = part.Substring(split + 1);
                if (fields.ContainsKey(key)) return false;
                bool valid;
                switch (key)
                {
                    case "schema": valid = value == "core-network-v2"; break;
                    case "target": valid = value == "hostkey"; break;
                    case "event": valid = new[] { "hostkey-begin", "hostkey-end", "hostkey-limit",
                        "hostkey-route", "hostkey-route-address", "hostkey-pool-dial", "hostkey-dial-address",
                        "hostkey-dns-begin", "hostkey-dns-end", "hostkey-dns-address", "hostkey-dns-winner" }.Contains(value); break;
                    case "route": valid = new[] { "main", "ai", "direct", "block", "other" }.Contains(value); break;
                    case "network": valid = new[] { "tcp", "udp", "icmp", "other" }.Contains(value); break;
                    case "inboundKind": valid = new[] { "mixed", "http", "socks", "tun", "other" }.Contains(value); break;
                    case "source": valid = new[] { "raw-network", "ip-network", "ip-cache" }.Contains(value); break;
                    case "error": valid = new[] { "none", "eof", "rejected", "reset", "refused", "pipe", "no-member",
                        "dns", "tls", "network-changed", "canceled", "timeout", "closed", "other" }.Contains(value); break;
                    case "tag": case "memberTag": valid = Regex.IsMatch(value, @"\A[0-9a-f]{16}\z"); break;
                    case "peer":
                        IPAddress address;
                        valid = Regex.IsMatch(value, @"\A[0-9a-fA-F:.]{2,45}\z") && IPAddress.TryParse(value, out address);
                        break;
                    case "cacheHit": case "public": valid = value == "true" || value == "false"; break;
                    case "family": valid = value == "0" || value == "4" || value == "6"; break;
                    case "pid": case "seq": case "monoMs": case "box": case "dropped": case "trace": case "request":
                    case "query": case "queryType": case "strategy": case "a": case "aaaa": case "rcode":
                    case "elapsedMs": case "errno": case "index": case "attempt": case "count":
                        long number;
                        valid = long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
                        if (valid) value = number.ToString(CultureInfo.InvariantCulture);
                        break;
                    default: return false;
                }
                if (!valid) return false;
                fields.Add(key, value);
            }
            if (!new[] { "schema", "event", "pid", "seq", "box", "target", "trace", "request" }.All(fields.ContainsKey)
                || long.Parse(fields["trace"], CultureInfo.InvariantCulture) <= 0) return false;
            if (fields.ContainsKey("peer") && !new[] { "hostkey-dns-address", "hostkey-route-address", "hostkey-dial-address" }.Contains(fields["event"])) return false;
            safe = string.Join(" ", fields.Select(p => p.Key + "=" + p.Value));
            return true;
        }
    }
}
