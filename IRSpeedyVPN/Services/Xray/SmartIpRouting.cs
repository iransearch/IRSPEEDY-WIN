using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using v2rayN.Handler;

namespace IRSpeedyVPN.Services.Xray
{
    /// <summary>
    /// SMART IP: a dedicated leastLoad balancer for AI traffic inside the smart
    /// connection config, gated by the shared VOD/AI toggle. The AI links come from
    /// the API, the same as VOD. VOD is not built here: the API serves plain VLESS on
    /// a public IP, which the core refuses as an Xray outbound, so VOD is handled by
    /// its own sing-box outbound instead.
    ///
    /// Two details differ from the Android client because the core will not accept
    /// them: it takes a single burstObservatory rather than multiObservatory, and its
    /// leastLoad strategy has no observerTag field. AI therefore shares the pool's
    /// health probe while keeping its own balancer, selector prefix and maxRTT.
    /// </summary>
    public static class SmartIpRouting
    {
        public const string SmartProxyPrefix = "smart-proxy-";
        public const string SmartBalancerTag = "smart-balancer-1";

        public const string AiProxyPrefix = "ai-proxy-";
        public const string AiBalancerTag = "ai-balancer";

        private const string SettingKey = "VGAURDVodService";

        // The smart balancer keeps the 3s ceiling from the config template;
        // VOD and AI tolerate more latency.
        private const string ServiceMaxRtt = "5s";

        public static readonly string[] AiDomains =
        {
            "labs.google",
            "antigravity.google",
            "google.com",
            "googleapis.com",
            "gstatic.com",
            "google-analytics.com",
            "googleusercontent.com",
            "generativelanguage.googleapis.com",
            "ai.google.dev",
            "bard.google.com",
            "gemini.google.com",
            "makersuite.google.com",
            "aistudio.google.com",
            "googlevideo.com",
            "ggpht.com",
            "withgoogle.com",
            "openai.com",
            "chatgpt.com",
            "apple.com",
            "icloud.com",
            "showip.net",
            "cdn-apple.com",
            "reddit.com",
            "flow.google.com",
            "accounts.google.com",
            "fonts.googleapis.com",
            "lh3.googleusercontent.com",
            "www.googletagmanager.com",
            "auditrecording-pa.googleapis.com",
            "csp.withgoogle.com",
            "flow.google",
            "flow-content.google",
            "aisandbox-pa.googleapis.com",
            "www.gstatic.com",
            "fonts.gstatic.com",
            "www.google.com",
            "ogads-pa.clients6.google.com",
            "ogs.google.com",
            "mail.google.com",
            "support.google.com"
        };

        /// <summary>Reads the shared VOD/AI toggle. Both services follow it.</summary>
        public static bool IsEnabled()
        {
            try
            {
                return RegHelper.GetSettingValue(SettingKey) == "1";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses <paramref name="links"/> into tagged outbounds. Links that fail to
        /// parse are skipped and logged; the service stays disabled when none survive.
        /// </summary>
        public static JArray BuildOutbounds(
            IEnumerable<string> links,
            string tagPrefix,
            string label,
            JsonSerializer serializer,
            out string fallbackTag)
        {
            var outbounds = new JArray();
            fallbackTag = null;

            if (links == null)
                return outbounds;

            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                var tag = tagPrefix + (outbounds.Count + 1);

                Outbound proxy = null;
                try
                {
                    string msg;
                    var item = ShareHandler.ImportFromConfigLink(link, out msg);
                    if (item != null)
                    {
                        proxy = new Outbound { tag = tag };
                        ConfigGenerator.FillOutboundForItem(proxy, item);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(ex);
                    proxy = null;
                }

                if (proxy == null || proxy.protocol == null)
                    continue;

                // The core refuses to build the whole config over one bad outbound,
                // which would take the smart connection down with it. Drop the link
                // instead and let the remaining ones carry the service.
                if (CoreRefusalReason(proxy) != null)
                    continue;

                outbounds.Add(JObject.FromObject(proxy, serializer));
                if (fallbackTag == null)
                    fallbackTag = tag;
            }

            return outbounds;
        }

        /// <summary>
        /// Mirrors the core's own outbound validation: it rejects VLESS without TLS,
        /// Reality or encryption, and Trojan without TLS, unless the server address is
        /// private. Returns the reason such an outbound would be refused, or null when
        /// the core will accept it.
        /// </summary>
        public static string CoreRefusalReason(Outbound proxy)
        {
            if (proxy == null)
                return null;

            var security = proxy.streamSettings == null ? null : proxy.streamSettings.security;
            var hasTransportSecurity = !string.IsNullOrWhiteSpace(security)
                && !string.Equals(security, "none", StringComparison.OrdinalIgnoreCase);
            if (hasTransportSecurity)
                return null;

            var address = proxy.settings == null ? null : proxy.settings.address;
            if (!RequiresTransportSecurity(address))
                return null;

            if (string.Equals(proxy.protocol, "vless", StringComparison.OrdinalIgnoreCase))
            {
                var encryption = proxy.settings == null ? null : proxy.settings.encryption;
                var hasEncryption = !string.IsNullOrWhiteSpace(encryption)
                    && !string.Equals(encryption, "none", StringComparison.OrdinalIgnoreCase);
                if (!hasEncryption)
                    return "vless without TLS, Reality or encryption";
            }
            else if (string.Equals(proxy.protocol, "trojan", StringComparison.OrdinalIgnoreCase))
            {
                return "trojan without TLS";
            }

            return null;
        }

        /// <summary>
        /// True when the address is public, so the core demands transport security.
        /// Deliberately conservative: anything not clearly private counts as public,
        /// because dropping a usable link is far cheaper than a config the core
        /// refuses to build.
        /// </summary>
        private static bool RequiresTransportSecurity(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;

            var value = address.Trim().Trim('[', ']');

            IPAddress ip;
            if (IPAddress.TryParse(value, out ip))
            {
                if (IPAddress.IsLoopback(ip))
                    return false;

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var b = ip.GetAddressBytes();
                    if (b[0] == 10) return false;                                   // 10.0.0.0/8
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;      // 172.16.0.0/12
                    if (b[0] == 192 && b[1] == 168) return false;                   // 192.168.0.0/16
                    if (b[0] == 169 && b[1] == 254) return false;                   // 169.254.0.0/16
                    return true;
                }

                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && (ip.GetAddressBytes()[0] & 0xFE) != 0xFC;

                return true;
            }

            var domain = value.ToLowerInvariant().TrimEnd('.');
            if (domain == "localhost")
                return false;
            if (domain.EndsWith(".localhost") || domain.EndsWith(".local")
                || domain.EndsWith(".localdomain") || domain.EndsWith(".home.arpa"))
                return false;

            return true;
        }

        /// <summary>
        /// Adds the AI prefix to the single burstObservatory selector so its health
        /// probe covers the AI outbounds too. The core has no multi-observer support,
        /// hence one probe shared by the pool and AI.
        /// </summary>
        public static void ExtendObservatorySelector(JObject root)
        {
            var selector = root?["burstObservatory"]?["subjectSelector"] as JArray;
            if (selector != null)
                selector.Add(AiProxyPrefix);
        }

        public static JObject LeastLoadBalancer(
            string tag,
            string selectorPrefix,
            string fallbackTag,
            string maxRtt)
        {
            return new JObject
            {
                ["selector"] = new JArray { selectorPrefix },
                ["strategy"] = new JObject
                {
                    ["settings"] = new JObject
                    {
                        ["expected"] = 5,
                        ["maxRTT"] = maxRtt,
                        ["tolerance"] = 0.2
                    },
                    ["type"] = "leastLoad"
                },
                ["tag"] = tag
            };
        }

        public static JObject AiBalancer(string fallbackTag)
        {
            return LeastLoadBalancer(AiBalancerTag, AiProxyPrefix, fallbackTag, ServiceMaxRtt);
        }

        /// <summary>Routing rule sending the given domains to a balancer.</summary>
        public static JObject DomainRule(string balancerTag, IEnumerable<string> domains)
        {
            var values = new JArray();
            foreach (var domain in domains ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(domain))
                    values.Add("domain:" + domain.Trim());
            }

            return new JObject
            {
                ["balancerTag"] = balancerTag,
                ["domain"] = values,
                ["type"] = "field"
            };
        }

        public static JObject AiRule()
        {
            return DomainRule(AiBalancerTag, AiDomains);
        }

    }
}
