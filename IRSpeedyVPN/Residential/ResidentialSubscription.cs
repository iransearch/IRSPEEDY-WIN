using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Residential
{
    internal sealed class ResidentialNode
    {
        internal string Link, Identity, Geography, Isp;
        internal string City => (Geography ?? "").Split(',')[0].Trim();
    }
    internal sealed class ResidentialCountry
    {
        internal string Code;
        internal int Id => -1000 - (Code[0] - 'A') * 26 - (Code[1] - 'A');
        internal readonly List<ResidentialNode> Nodes = new List<ResidentialNode>();
    }
    internal static class ResidentialSubscription
    {
        internal const int MaxBytes = 4 * 1024 * 1024;
        private static readonly HashSet<string> Schemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "vless", "trojan", "ss", "shadowsocks", "vmess", "hysteria2", "hy2", "tuic", "anytls", "ssh" };
        internal static ResidentialCountry[] Parse(string text)
        {
            if (text == null || Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new FormatException("Subscription size");
            if (!text.Contains("://"))
            {
                string encoded = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            var countries = new Dictionary<string, ResidentialCountry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string link = raw.Trim();
                if (link.Length == 0 || link.StartsWith("#")) continue;
                if (++count > 2048) throw new FormatException("Subscription entries");
                int colon = link.IndexOf("://", StringComparison.Ordinal), hash = link.IndexOf('#');
                if (colon <= 0 || hash < 0 || !Schemes.Contains(link.Substring(0, colon))) continue;
                string identity = ResidentialPresentation.Digest(link.Substring(0, hash));
                if (seen.Contains(identity)) continue;
                string label;
                try { label = Uri.UnescapeDataString(link.Substring(hash + 1)).Split('#')[0].Trim(); }
                catch (UriFormatException) { continue; }
                string[] meta = label.Split(new[] { '/' }, 2);
                string geo = meta[0].Trim();
                string code = geo.Split(',').Last().Trim().ToUpperInvariant();
                if (code == "UK") code = "GB";
                if (code.Length != 2 || code.Any(c => c < 'A' || c > 'Z')) continue;
                try { new RegionInfo(code); } catch (ArgumentException) { continue; }
                if (!link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) && !link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri;
                    if (!Uri.TryCreate(link, UriKind.Absolute, out uri) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port == 0) continue;
                }
                ResidentialCountry country;
                if (!countries.TryGetValue(code, out country)) countries[code] = country = new ResidentialCountry { Code = code };
                country.Nodes.Add(new ResidentialNode { Link = link, Identity = identity, Geography = geo, Isp = meta.Length > 1 ? meta[1].Trim() : "" });
                seen.Add(identity);
            }
            if (countries.Count == 0) throw new FormatException("Empty Residential subscription");
            return countries.Values.ToArray();
        }
    }
}
