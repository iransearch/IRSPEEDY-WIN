using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Residential;

namespace IRSpeedyVPN.Services
{
    // A country subscription supplies a list of share links, never a Core configuration.
    internal static class CountrySubscription
    {
        private static readonly HashSet<string> schemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "vpn", "wg", "wireguard", "vless", "vmess", "ss", "trojan", "hy2", "hysteria2", "tuic", "ssh", "anytls", "shadowsocks" };

        internal static bool Marked(ServerEx row, Url entry) => row.xui_server_id > 0 || (entry?.is_subscription ?? 0) != 0;

        private static string Wrapper(Url entry)
        {
            string value = entry?.url?.Trim();
            if (value == null) return null;
            // Only unwrap a whole encoded API link. An existing URI may carry
            // signed query components whose literal plus signs and escapes matter.
            if (value.IndexOf(':') < 0 && value.IndexOf('%') >= 0)
            {
                try { value = Uri.UnescapeDataString(value.Replace("+", " ")); }
                catch (UriFormatException) { return null; }
            }
            return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? value : null;
        }

        internal static string Source(Url entry)
        {
            string value = Wrapper(entry);
            return value != null && ResidentialConfig.ValidUrl(value) ? value : null;
        }

        internal static string[] Parse(string body)
        {
            if (body == null || body.Length > ResidentialSubscription.MaxBytes)
                throw new FormatException("subscription-size");
            string text = body.Trim().TrimStart('\uFEFF').Trim();
            if (!text.Contains("://") && !text.StartsWith("#", StringComparison.Ordinal))
            {
                string encoded = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace('-', '+').Replace('_', '/');
                text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(
                    encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))).Trim().TrimStart('\uFEFF');
            }
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool unsupported = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int separator = line.IndexOf("://", StringComparison.Ordinal);
                string identity = Identity(line);
                if (separator < 1 || identity.Length <= separator + 3 || !schemes.Contains(line.Substring(0, separator))
                    || line.Any(char.IsControl) || identity.Any(char.IsWhiteSpace))
                {
                    unsupported = true;
                    continue;
                }
                if (seen.Add(identity)) links.Add(line);
                if (links.Count > 1000) throw new FormatException("subscription-node-limit");
            }
            if (unsupported && links.Count == 0) throw new FormatException("subscription-no-supported-links");
            return links.ToArray();
        }

        private static string Identity(string link)
        {
            int hash = link.IndexOf('#');
            return hash < 0 ? link : link.Substring(0, hash);
        }

        internal static List<Group> Expand(List<Group> groups, IReadOnlyDictionary<string, string[]> nodes)
        {
            var result = new List<Group>(groups.Count);
            foreach (var group in groups)
            {
                var rows = new List<ServerEx>(group.servers.Count);
                foreach (var row in group.servers)
                {
                    // Windows also has legacy HTTPS Cisco gateways. For unmarked addresses
                    // keep the original adapter until the body positively identifies a subscription.
                    bool Replace(Url entry)
                    {
                        string uri = Wrapper(entry);
                        return uri != null && (Marked(row, entry) || (nodes != null && nodes.ContainsKey(uri)));
                    }
                    if (!row.urls.Any(Replace)) { rows.Add(row); continue; }
                    var expanded = new List<Url>();
                    var direct = row.urls.Where(u => u?.url != null && !Replace(u) && u.status == 1);
                    var seen = new HashSet<string>(direct.Select(u => Identity(u.url)), StringComparer.Ordinal);
                    foreach (var parent in row.urls)
                    {
                        if (!Replace(parent)) { expanded.Add(parent); continue; }
                        string source = Wrapper(parent);
                        if (parent.status != 1 || nodes == null || !nodes.TryGetValue(source, out var links)) continue;
                        foreach (var link in links)
                            if (seen.Add(Identity(link))) expanded.Add(parent.WithSubscriptionLink(link));
                    }
                    rows.Add(row.WithUrls(expanded));
                }
                result.Add(group.WithServers(rows));
            }
            return result;
        }
    }
}
