using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class ParallelDnsRouting
    {
        private const string Remote = "dns-remote";
        private const string RaceType = "vpn-dns-race";

        // Apply after Split Tunnel and typed serialization. The updated Throne
        // core owns completion-order selection; DNS rule order cannot pick a provider.
        public static string Apply(string json)
        {
            var config = JObject.Parse(json);
            var dns = config["dns"] as JObject;
            var servers = dns?["servers"] as JArray;
            var remote = servers?.OfType<JObject>().FirstOrDefault(s => (string)s["tag"] == Remote);
            if ((string)remote?["type"] == RaceType)
            {
                ResolveProxyDestinations(config);
                return config.ToString(Formatting.None);
            }
            // Probe/custom profiles keep their own policy. A composite is already applied.
            if (remote == null || (string)remote["type"] != "https" || (string)remote["detour"] != "proxy"
                || (string)remote["path"] != "/dns-query"
                || ((string)remote["server"] != "1.1.1.1" && (string)remote["server"] != "8.8.8.8")) return json;
            if (servers.OfType<JObject>().Any(s => (string)s["tag"] == "dns-remote-cloudflare"
                || (string)s["tag"] == "dns-remote-yandex" || (string)s["tag"] == "dns-remote-additional")) return json;
            var rules = dns["rules"] as JArray ?? new JArray();
            // Do not mix this profile with an existing user-defined evaluate/respond policy.
            if (rules.Descendants().OfType<JObject>().Any(r => (string)r["action"] == "evaluate"
                || (string)r["action"] == "respond" || r["match_response"] != null || r["race"] != null)) return json;

            var final = (string)dns["final"] ?? (string)servers.FirstOrDefault()?["tag"];
            var primary = new JArray();
            foreach (var endpoint in new[] {
                new[] { "google", "8.8.8.8", "dns.google" },
                new[] { "cloudflare", "1.1.1.1", "cloudflare-dns.com" },
                new[] { "yandex", "77.88.8.8", "common.dot.dns.yandex.net" } })
                primary.Add(new JObject {
                    ["type"] = "https", ["tag"] = endpoint[0], ["server"] = endpoint[1],
                    ["path"] = "/dns-query", ["detour"] = "proxy",
                    ["tls"] = new JObject { ["enabled"] = true, ["server_name"] = endpoint[2] }
                });
            primary.Add(new JObject { ["type"] = "udp", ["tag"] = "additional",
                ["server"] = "78.186.60.77", ["server_port"] = 53, ["detour"] = "proxy" });
            remote.Replace(new JObject {
                ["type"] = RaceType, ["tag"] = Remote, ["servers"] = primary,
                ["timeout"] = "8s", ["fallback_timeout"] = "4s",
                ["fallback"] = new JObject { ["type"] = "tcp", ["tag"] = "fallback",
                    ["server"] = "1.1.1.1", ["server_port"] = 53, ["detour"] = "proxy" }
            });
            // Give the native 8s primary + 4s fallback their full budget. Keep
            // explicitly shorter per-rule timeouts and all direct DNS options.
            foreach (var rule in rules.OfType<JObject>().Where(r => (string)r["server"] == Remote
                && ((string)r["action"] == "route" || r["action"] == null) && r["timeout"] == null))
                rule["timeout"] = "12s";
            if (final == Remote)
                rules.Add(new JObject { ["action"] = "route", ["server"] = Remote, ["timeout"] = "12s" });
            dns["rules"] = rules;
            ResolveProxyDestinations(config);
            return config.ToString(Formatting.None);
        }

        private static bool IsVpnOutbound(string tag) => tag == "proxy" || tag == "ai-proxy";

        // Only explicit HTTP/SOCKS clients need destination resolution here.
        // TUN already supplies DNS packets; internal probes have no user inbound.
        private static void ResolveProxyDestinations(JObject config)
        {
            var tags = (config["inbounds"] as JArray)?.OfType<JObject>()
                .Where(i => new[] { "mixed", "http", "socks" }.Contains((string)i["type"]))
                .Select(i => (string)i["tag"]).Where(t => !string.IsNullOrEmpty(t)).ToArray();
            var route = config["route"] as JObject;
            var rules = route?["rules"] as JArray;
            if (tags == null || tags.Length == 0 || rules == null) return;

            var ordered = new JArray();
            foreach (var rule in rules)
            {
                var target = rule as JObject;
                if (target != null && IsVpnOutbound((string)target["outbound"])
                    && (string.IsNullOrEmpty((string)target["action"]) || (string)target["action"] == "route"))
                    AddResolve(ordered, ProxyResolve(tags, RouteMatcher(target)), tags);
                ordered.Add(rule.DeepClone());
            }
            if (IsVpnOutbound((string)route["final"]))
                AddResolve(ordered, ProxyResolve(tags, null), tags);
            route["rules"] = ordered;
        }

        private static JObject RouteMatcher(JObject route)
        {
            var matcher = (JObject)route.DeepClone();
            // These belong to the route action, not to the rule's conditions.
            foreach (var field in new[] { "action", "outbound", "override_address", "override_port",
                "network_strategy", "network_type", "fallback_network_type", "fallback_delay",
                "udp_disable_domain_unmapping", "udp_connect", "udp_timeout", "tls_fragment",
                "tls_fragment_fallback_delay", "tls_record_fragment", "tls_spoof", "tls_spoof_method" })
                matcher.Remove(field);
            return matcher.Properties().Any(p => p.Name != "type") ? matcher : null;
        }

        private static JObject ProxyResolve(string[] tags, JObject matcher)
        {
            var scope = new JObject { ["inbound"] = new JArray(tags) };
            var rule = matcher == null ? scope : new JObject { ["type"] = "logical", ["mode"] = "and",
                ["rules"] = new JArray(scope, matcher) };
            rule["action"] = "resolve";
            rule["timeout"] = "12s";
            // Do not force server=dns-remote: normal DNS routing preserves Shield,
            // localhost, bootstrap and scoped direct DNS. Its VPN final is the race.
            return rule;
        }

        private static JObject ProxyBlocker(JObject original, string[] tags)
        {
            var scope = new JObject { ["inbound"] = new JArray(tags) };
            if ((string)original["type"] == "logical" && (string)original["mode"] == "and"
                && original["rules"] is JArray && JToken.DeepEquals(original["rules"][0], scope))
                return (JObject)original.DeepClone();
            var matcher = RouteMatcher(original);
            matcher?.Remove("method"); matcher?.Remove("no_drop");
            if (JToken.DeepEquals(matcher, scope)) return (JObject)original.DeepClone();
            if (matcher != null && !matcher.Properties().Any(p => p.Name != "type")) matcher = null;
            var guarded = matcher == null ? scope : new JObject { ["type"] = "logical", ["mode"] = "and",
                ["rules"] = new JArray(scope, matcher) };
            // Preserve the block action and its options on the outer rule.
            foreach (var property in original.Properties().Where(p => p.Name == "action" || p.Name == "outbound"
                || p.Name == "method" || p.Name == "no_drop")) guarded[property.Name] = property.Value.DeepClone();
            return guarded;
        }

        private static void AddResolve(JArray rules, JObject rule, string[] tags)
        {
            // A domain-only request can start matching an IP/rule-set Shield
            // after resolution. Recheck preceding blockers before the VPN dial.
            // Earlier direct/bypass rules have already retained their precedence.
            var guards = rules.OfType<JObject>().Where(r => (string)r["outbound"] == "block"
                || (string)r["action"] == "reject").Select(r => ProxyBlocker(r, tags))
                .Distinct(new JTokenEqualityComparer()).ToArray();
            var bundle = new[] { rule }.Concat(guards).ToArray();
            var tail = rules.Skip(System.Math.Max(0, rules.Count - bundle.Length)).ToArray();
            if (tail.Length == bundle.Length && tail.Zip(bundle, JToken.DeepEquals).All(equal => equal)) return;
            foreach (var item in bundle) rules.Add(item.DeepClone());
        }
    }
}

