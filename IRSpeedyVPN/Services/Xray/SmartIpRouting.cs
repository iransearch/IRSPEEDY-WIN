using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using v2rayN.Handler;

namespace IRSpeedyVPN.Services.Xray
{
    /// <summary>
    /// SMART IP: dedicated leastLoad balancers for VOD and AI traffic inside the
    /// smart connection config. Both services are gated by the single VOD/AI toggle.
    ///
    /// The Throne core only accepts a single burstObservatory and its leastLoad
    /// strategy has no observerTag field, so all three services share one health
    /// probe. Their selection stays independent: each keeps its own balancer,
    /// selector prefix and maxRTT.
    /// </summary>
    public static class SmartIpRouting
    {
        public const string SmartProxyPrefix = "smart-proxy-";
        public const string SmartBalancerTag = "smart-balancer-1";

        public const string VodProxyPrefix = "vod-proxy-";
        public const string VodBalancerTag = "vod-balancer";

        public const string AiProxyPrefix = "ai-proxy-";
        public const string AiBalancerTag = "ai-balancer";

        private const string SettingKey = "VGAURDVodService";

        // The smart balancer keeps the 3s ceiling from the config template;
        // VOD and AI tolerate more latency.
        private const string ServiceMaxRtt = "5s";

        // AI links are hard-coded on purpose: unlike VOD they are not served by
        // the API, so rotating them requires an application update.
        private const string AiHy2Link =
            "hy2://52f2ef24d332c7096c13eafb6e39b5e6@hy2us103.hy2any.info:900"
            + "?sni=www.google.com&insecure=1&obfs=gecko"
            + "&obfs-password=pmn7JaYD1PI1l1ciS6vuDkq#HY2-GECKO";

        private const string AiVlessRealityLink =
            "vless://0b663d89-0549-475d-baa7-39a892f30f78@ca15692.fillmoo.info:443"
            + "?encryption=mlkem768x25519plus.native.0rtt"
            + ".aRCWVpiFbPuY2_GgdmuiYQRiYnu8cneJlSByPK5IKiU"
            + "&security=reality&sni=yahoo.com&fp=firefox"
            + "&pbk=CfFlJvCfG5eb6Fc62bz2QImo0VsGYtFJOqWWmK4gwE8"
            + "&sid=bd33ac26a46f19b3&type=tcp#tcp-443-default";

        public static string[] AiLinks
        {
            get { return new[] { AiHy2Link, AiVlessRealityLink }; }
        }

        public static readonly string[] AiDomains =
        {
            "labs.google",
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
            "openai.com",
            "chatgpt.com",
            "apple.com",
            "icloud.com",
            "showip.net",
            "cdn-apple.com"
        };

        public static readonly string[] VodDomains =
        {
            "filimo.com",
            "namava.ir",
            "tamashakhoneh.ir",
            "tmk.ir",
            "gapfilm.ir",
            "digitoon.tv",
            "filmnet.ir"
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

            int requested = 0;
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                requested++;
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
                {
                    LogHelper.WriteExLog($"SMART IP {label} outbound rejected: {tag}");
                    continue;
                }

                outbounds.Add(JObject.FromObject(proxy, serializer));
                if (fallbackTag == null)
                    fallbackTag = tag;
            }

            LogHelper.WriteExLog(
                $"SMART IP {label} routing: requested={requested}"
                + $", accepted={outbounds.Count}"
                + $", mode={(outbounds.Count > 0 ? "leastLoad" : "disabled")}");

            return outbounds;
        }

        /// <summary>
        /// Adds the active service prefixes to the single burstObservatory selector,
        /// so its health probe covers the VOD and AI outbounds too. The Throne core
        /// has no multi-observer support, hence one probe for all three services.
        /// </summary>
        public static void ExtendObservatorySelector(JObject root, bool vodActive, bool aiActive)
        {
            var selector = root?["burstObservatory"]?["subjectSelector"] as JArray;
            if (selector == null)
                return;

            if (vodActive)
                selector.Add(VodProxyPrefix);
            if (aiActive)
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
                ["fallbackTag"] = fallbackTag,
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

        public static JObject VodBalancer(string fallbackTag)
        {
            return LeastLoadBalancer(VodBalancerTag, VodProxyPrefix, fallbackTag, ServiceMaxRtt);
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

        public static JObject VodRule()
        {
            return DomainRule(VodBalancerTag, VodDomains);
        }
    }
}
