using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services;
using System.Linq;

namespace IRSpeedyVPN.Residential
{
    // The Android subscription links use the existing mixed-protocol country pool.
    // Keep all links, including unsuccessful probes, in the runtime pool.
    internal sealed class ResidentialService : TunnelPlusService
    {
        private readonly ResidentialCountry country;
        // Exact protocols accepted by the existing Windows Xray country pool.
        // Android also accepts other native protocols; do not invent a Windows adapter.
        internal static bool IsPoolCompatible(string link) => new[] { "vless://", "trojan://", "vmess://", "ss://", "hysteria2://", "hy2://" }
            .Any(prefix => link.StartsWith(prefix, System.StringComparison.Ordinal));
        internal string AccountScope { get; }
        internal ResidentialService(ResidentialCountry country, GlobalInfo global, string accountScope)
            : base(new ServerEx { ID = country.Id, Country = country.Code, status = 1,
                urls = country.Nodes.Where(n => IsPoolCompatible(n.Link)).Select(n => new Url { url = n.Link, status = 1,
                    extra_field_2 = new[] { "vless", "trojan", "vmess", "ss", "shadowsocks" }.Contains(n.Link.Split(':')[0].ToLowerInvariant()) ? "xray" : "singbox" }).ToList() }, global)
        {
            this.country = country;
            AccountScope = accountScope;
            Name = "Residential";
            SetSmartFastUrls(GetServerUrls().Select(n => n.url).ToArray());
            SelectedServerUrl = GetServerUrls().FirstOrDefault();
        }
        private ResidentialNode DisplayNode
        {
            get
            {
                var best = GetServerUrls().Where(u => u.latency > 0).OrderBy(u => u.latency).FirstOrDefault();
                return country.Nodes.FirstOrDefault(n => n.Link == best?.url) ?? country.Nodes[0];
            }
        }
        internal string City => DisplayNode.City;
        internal string Isp => DisplayNode.Isp;
    }
}

