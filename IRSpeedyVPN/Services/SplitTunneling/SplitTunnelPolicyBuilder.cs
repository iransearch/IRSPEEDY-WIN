using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace IRSpeedyVPN.Services.SplitTunneling
{
    // The app choices are compiled into the existing sing-box configuration;
    // no second engine, driver, firewall rules or live rule-file watcher is added.
    internal static class SplitTunnelPolicyBuilder
    {
        public static string Apply(string json, SplitTunnelSettings settings)
        {
            if (settings == null || !settings.Enabled) return json;
            if (!Enum.IsDefined(typeof(SplitTunnelMode), settings.Mode) || settings.Apps == null)
                throw new InvalidOperationException("Invalid split tunnel policy.");
            var config = JObject.Parse(json);
            var tunTags = (config["inbounds"] as JArray)?.OfType<JObject>()
                .Where(i => (string)i["type"] == "tun").Select(i => (string)i["tag"]).ToArray();
            // Proxy-only connections and URL tests cannot identify arbitrary apps.
            if (tunTags == null || tunTags.Length == 0) return json;
            if (tunTags.Any(string.IsNullOrEmpty)) throw new InvalidOperationException("TUN tag is missing.");
            var patterns = settings.Apps.Select(AppPathPattern.ForApp).ToArray();
            if (patterns.Any(p => p == null)) throw new InvalidOperationException("Invalid split tunnel executable path.");
            patterns = patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var route = (JObject)config["route"];
            var rules = route?["rules"] as JArray;
            var outbounds = (JArray)config["outbounds"];
            var dns = (JObject)config["dns"];
            if (route == null || rules == null || dns == null || outbounds == null)
                throw new InvalidOperationException("Split tunneling requires a complete routing configuration.");
            route["find_process"] = true;
            route["auto_detect_interface"] = true;
            // Game mode has a direct catch-all. App split mode takes precedence,
            // while the existing AI, shield, LAN and geo rules stay intact.
            if ((string)route["final"] == "direct") route["final"] = "proxy";

            // Capture IPv6 as well; leaving only IPv4 in the TUN would bypass
            // application rules on a dual-stack Windows network.
            foreach (var tun in ((JArray)config["inbounds"]).OfType<JObject>().Where(i => (string)i["type"] == "tun"))
            {
                var addresses = tun["address"] as JArray;
                if (addresses == null) throw new InvalidOperationException("Modern TUN address configuration is required.");
                if (!addresses.Values<string>().Any(a => a.Contains(":"))) addresses.Add("fdfe:dcba:9876::1/126");
            }

            var prefix = new JArray();
            // Core loop protection must precede any app rules, including when a
            // user selects a parent directory containing a bundled core binary.
            foreach (var rule in rules.OfType<JObject>().Where(r =>
                (r["process_name"] != null || r["process_path"] != null)
                && r["outbound"] != null && (string)r["outbound"] != "proxy")) prefix.Add(rule.DeepClone());
            prefix.Add(new JObject { ["inbound"] = new JArray(tunTags), ["port"] = 53, ["action"] = "hijack-dns" });

            var matcher = DirectMatcher(settings.Mode, patterns);
            if (matcher != null)
            {
                outbounds.Add(new JObject { ["tag"] = "split-direct", ["type"] = "direct",
                    ["domain_resolver"] = "split-dns-direct" });
                ((JArray)dns["servers"]).Add(new JObject { ["tag"] = "split-dns-direct", ["type"] = "udp",
                    ["server"] = "8.8.8.8", ["detour"] = "split-direct" });
                var traffic = ScopedMatcher(matcher, tunTags);
                traffic["action"] = "route";
                traffic["outbound"] = "split-direct";
                // Deliberate direct apps precede geo, shield, AI and the VPN-only
                // UDP/443 reject. All unclassified/forwarded traffic keeps VPN policy.
                prefix.Add(traffic);
                var dnsRule = ScopedMatcher(matcher, tunTags);
                dnsRule["action"] = "route";
                dnsRule["server"] = "split-dns-direct";
                var dnsRules = dns["rules"] as JArray ?? new JArray();
                if (dns["rules"] == null) dns["rules"] = dnsRules;
                dnsRules.Insert(0, dnsRule);
            }
            foreach (var rule in rules) prefix.Add(rule.DeepClone());
            route["rules"] = prefix;
            return config.ToString(Formatting.None);
        }

        private static JObject ScopedMatcher(JObject matcher, string[] tunTags)
        {
            return new JObject { ["type"] = "logical", ["mode"] = "and", ["rules"] = new JArray(
                new JObject { ["inbound"] = new JArray(tunTags) }, matcher.DeepClone()) };
        }

        private static JObject DirectMatcher(SplitTunnelMode mode, string[] patterns)
        {
            if (mode == SplitTunnelMode.ExcludeSelectedApps)
                return patterns.Length == 0 ? null : new JObject { ["process_path_regex"] = new JArray(patterns) };
            // Do not send hotspot/remote proxy clients or unknown processes direct
            // merely because Windows could not associate them with an executable.
            var knownProcess = new JObject { ["process_path_regex"] = new JArray(".+") };
            if (patterns.Length == 0) return knownProcess;
            return new JObject { ["type"] = "logical", ["mode"] = "and", ["rules"] = new JArray(
                knownProcess, new JObject { ["process_path_regex"] = new JArray(patterns), ["invert"] = true }) };
        }
    }
}
