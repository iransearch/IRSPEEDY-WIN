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
        public static string Apply(string json, SplitTunnelSettings settings, string clientExecutablePath = null)
        {
            if (settings == null || !settings.Enabled) return json;
            if (settings.Version != 2 || settings.Apps == null)
                throw new InvalidOperationException("Invalid split tunnel policy.");
            var config = JObject.Parse(json);
            var inbounds = config["inbounds"] as JArray;
            var tunTags = Tags(inbounds, "tun");
            var proxyTags = Tags(inbounds, "mixed", "http", "socks");
            if (tunTags.Length == 0 && proxyTags.Length == 0) return json;
            if (tunTags.Concat(proxyTags).Any(string.IsNullOrEmpty))
                throw new InvalidOperationException("Split tunnel inbound tag is missing.");
            var scope = ProcessScope(tunTags, proxyTags, clientExecutablePath);
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
            if (tunTags.Length > 0)
                prefix.Add(new JObject { ["inbound"] = new JArray(tunTags), ["port"] = 53, ["action"] = "hijack-dns" });

            var matcher = DirectMatcher(patterns);
            if (matcher != null)
            {
                outbounds.Add(new JObject { ["tag"] = "split-direct", ["type"] = "direct",
                    ["domain_resolver"] = "split-dns-direct" });
                ((JArray)dns["servers"]).Add(new JObject { ["tag"] = "split-dns-direct", ["type"] = "udp",
                    ["server"] = "8.8.8.8", ["detour"] = "split-direct" });
                var traffic = ScopedMatcher(matcher, scope);
                traffic["action"] = "route";
                traffic["outbound"] = "split-direct";
                // Deliberate direct apps precede geo, shield, AI and the VPN-only
                // UDP/443 reject. All unclassified/forwarded traffic keeps VPN policy.
                prefix.Add(traffic);
                var dnsRule = ScopedMatcher(matcher, scope);
                dnsRule["action"] = "route";
                dnsRule["server"] = "split-dns-direct";
                // Do not let a direct resolver answer populate the VPN DNS cache
                // on older cores which share a cache between transports.
                dnsRule["disable_cache"] = true;
                var dnsRules = dns["rules"] as JArray ?? new JArray();
                if (!((JArray)dns["servers"]).OfType<JObject>().Any(s => (string)s["tag"] == "dns-remote"))
                    throw new InvalidOperationException("VPN DNS resolver is missing.");
                var orderedDns = new JArray(dnsRules.OfType<JObject>()
                    .Where(r => (string)r["action"] == "predefined").Select(r => r.DeepClone()));
                if (tunTags.Length > 0)
                {
                    // Only TUN captures the Windows DNS Client. Proxy mode cannot
                    // intercept system DNS or traffic that bypasses the proxy.
                    var sharedDns = ScopedMatcher(new JObject
                        { ["process_name"] = new JArray("svchost.exe", "svchost") },
                        new JObject { ["inbound"] = new JArray(tunTags) });
                    sharedDns["action"] = "route";
                    sharedDns["server"] = "dns-remote";
                    orderedDns.Add(sharedDns);
                }
                orderedDns.Add(dnsRule);
                foreach (var rule in dnsRules.OfType<JObject>().Where(r => (string)r["action"] != "predefined"))
                    orderedDns.Add(rule.DeepClone());
                dns["rules"] = orderedDns;
                dns["final"] = "dns-remote";
            }
            foreach (var rule in rules) prefix.Add(rule.DeepClone());
            route["rules"] = prefix;
            return config.ToString(Formatting.None);
        }

        private static string[] Tags(JArray inbounds, params string[] types)
            => inbounds?.OfType<JObject>().Where(i => types.Contains((string)i["type"]))
                .Select(i => (string)i["tag"]).ToArray() ?? new string[0];

        private static JObject ProcessScope(string[] tunTags, string[] proxyTags, string clientExecutablePath)
        {
            var scopes = new JArray();
            if (tunTags.Length > 0) scopes.Add(new JObject { ["inbound"] = new JArray(tunTags) });
            if (proxyTags.Length > 0)
            {
                // Restrict app ownership to local proxy clients. Remote Share VPN
                // users must not inherit the desktop's application selection.
                var localProxy = new JObject { ["inbound"] = new JArray(proxyTags),
                    ["source_ip_cidr"] = new JArray("127.0.0.0/8", "::1/128") };
                if (!string.IsNullOrEmpty(clientExecutablePath))
                {
                    if (!AppPathPattern.IsLocalPath(clientExecutablePath))
                        throw new InvalidOperationException("Invalid client executable path.");
                    // The client's explicit proxy request measures VPN egress IP.
                    // Keep it on VPN even when the client is absent from the app list.
                    localProxy = ScopedMatcher(new JObject { ["process_path_regex"] =
                        new JArray("(?i)^" + AppPathPattern.Escape(clientExecutablePath) + "$"),
                        ["invert"] = true }, localProxy);
                }
                scopes.Add(localProxy);
            }
            return scopes.Count == 1 ? (JObject)scopes[0].DeepClone()
                : new JObject { ["type"] = "logical", ["mode"] = "or", ["rules"] = scopes };
        }

        private static JObject ScopedMatcher(JObject matcher, JObject scope)
            => new JObject { ["type"] = "logical", ["mode"] = "and",
                ["rules"] = new JArray(scope.DeepClone(), matcher.DeepClone()) };

        private static JObject DirectMatcher(string[] patterns)
        {
            // Do not send hotspot/remote proxy clients or unknown processes direct
            // merely because Windows could not associate them with an executable.
            var knownProcess = new JObject { ["process_path_regex"] = new JArray(".+") };
            if (patterns.Length == 0) return knownProcess;
            return new JObject { ["type"] = "logical", ["mode"] = "and", ["rules"] = new JArray(
                knownProcess, new JObject { ["process_path_regex"] = new JArray(patterns), ["invert"] = true }) };
        }
    }
}
