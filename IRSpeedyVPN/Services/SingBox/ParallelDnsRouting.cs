using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class ParallelDnsRouting
    {
        private const string Google = "dns-remote";
        private const string Cloudflare = "dns-remote-cloudflare";
        private const string Yandex = "dns-remote-yandex";
        private const string ResponsePrefix = "irspeedy-dns-race-";
        private static readonly string[] RemoteTags = { Google, Cloudflare, Yandex };
        private static readonly string[] RouteOptions = { "action", "server", "strategy", "disable_cache",
            "disable_optimistic_cache", "rewrite_ttl", "timeout", "client_subnet", "remove_client_subnet" };

        // Apply after process/Split Tunnel DNS rules, and after typed serialization:
        // evaluate/respond are supported by the pinned Throne sing-box fork.
        public static string Apply(string json)
        {
            var config = JObject.Parse(json);
            var dns = config["dns"] as JObject;
            var servers = dns?["servers"] as JArray;
            var remote = servers?.OfType<JObject>().FirstOrDefault(s => (string)s["tag"] == Google);
            // Only upgrade the app's VPN DoH profile. Probe and custom resolvers
            // keep their own DNS policy.
            if (remote == null || (string)remote["type"] != "https" || (string)remote["detour"] != "proxy"
                || (string)remote["path"] != "/dns-query"
                || ((string)remote["server"] != "1.1.1.1" && (string)remote["server"] != "8.8.8.8")) return json;
            var rules = dns["rules"] as JArray ?? new JArray();
            if (rules.OfType<JObject>().Any(r => (string)r["action"] == "evaluate"
                && ((string)r["tag"] ?? "").StartsWith(ResponsePrefix, System.StringComparison.Ordinal))) return json;
            if (servers.OfType<JObject>().Any(s => (string)s["tag"] == Cloudflare || (string)s["tag"] == Yandex)) return json;

            // A legacy DNS rule strategy cannot coexist with evaluate/race. The
            // app's bootstrap dialer already gets prefer_ipv4 from this resolver;
            // remove only that redundant rule option, never a custom strategy.
            var resolver = config["route"]?["default_domain_resolver"] as JObject;
            foreach (var rule in rules.Descendants().OfType<JObject>().ToArray())
            {
                var action = (string)rule["action"];
                if (action != null && action != "route" && action != "route-options"
                    && action != "predefined" && action != "reject") return json;
                if (rule.Properties().Any(p => p.Name == "match_response" || p.Name.StartsWith("response_",
                    System.StringComparison.Ordinal) || p.Name == "ip_cidr" || p.Name == "ip_is_private"
                    || p.Name == "ip_accept_any" || p.Name == "rule_set" || p.Name == "race" || p.Name == "speculative")) return json;
                var strategy = (string)rule["strategy"];
                if (!string.IsNullOrEmpty(strategy) && !(strategy == "prefer_ipv4"
                    && (string)rule["server"] == "dns-direct" && (string)resolver?["server"] == "dns-direct"
                    && (string)resolver?["strategy"] == "prefer_ipv4")) return json;
                rule.Remove("strategy");
            }

            var final = (string)dns["final"] ?? (string)servers.FirstOrDefault()?["tag"];
            remote["server"] = "8.8.8.8";
            remote["tls"] = new JObject { ["enabled"] = true, ["server_name"] = "dns.google" };
            var endpoints = new[] { new[] { Cloudflare, "1.1.1.1", "cloudflare-dns.com" },
                new[] { Yandex, "77.88.8.8", "common.dot.dns.yandex.net" } };
            int insert = servers.IndexOf(remote) + 1;
            foreach (var endpoint in endpoints)
                servers.Insert(insert++, new JObject {
                    ["type"] = "https", ["tag"] = endpoint[0], ["server"] = endpoint[1],
                    ["path"] = "/dns-query", ["detour"] = "proxy",
                    ["tls"] = new JObject { ["enabled"] = true, ["server_name"] = endpoint[2] }
                });
            var ordered = new JArray();
            int group = 0;
            foreach (var rule in rules.OfType<JObject>())
            {
                if ((string)rule["server"] == Google && ((string)rule["action"] == "route" || rule["action"] == null))
                    AddRace(ordered, rule, group++);
                else ordered.Add(rule.DeepClone());
            }
            if (final == Google)
            {
                AddRace(ordered, new JObject(), group);
                dns["final"] = Google;
            }
            dns["rules"] = ordered;
            return config.ToString(Formatting.None);
        }

        private static void AddRace(JArray rules, JObject route, int group)
        {
            var responseTags = RemoteTags.Select(server => ResponsePrefix + group + "-" + server).ToArray();
            // Launch all before any response rule. Copy query scope/options so
            // Windows shared DNS and explicit VPN rules retain their precedence.
            for (int index = 0; index < RemoteTags.Length; index++)
            {
                var evaluate = (JObject)route.DeepClone();
                evaluate["action"] = "evaluate";
                evaluate["server"] = RemoteTags[index];
                evaluate["tag"] = responseTags[index];
                rules.Add(evaluate);
            }
            // Bind responses in default rules: logical respond cannot bind a
            // named result. Missing scoped futures simply do not match.
            foreach (var tag in responseTags)
                foreach (var rcode in new[] { "NOERROR", "NXDOMAIN" })
                    rules.Add(new JObject { ["action"] = "respond", ["match_response"] = tag,
                        ["response_rcode"] = rcode, ["race"] = true });
            // The core holds this terminal action until all races fail. Keep
            // the original query scope and avoid a duplicate/sequential retry
            // or fallback to a direct resolver when all VPN resolvers fail.
            var failure = (JObject)route.DeepClone();
            foreach (var option in RouteOptions) failure.Remove(option);
            failure["action"] = "predefined";
            failure["rcode"] = "SERVFAIL";
            rules.Add(failure);
        }
    }
}
