using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class TunBrowserCompatibility
    {
        public static string Apply(string json)
        {
            var config = JObject.Parse(json);
            var tunTags = (config["inbounds"] as JArray)?.OfType<JObject>()
                .Where(i => (string)i["type"] == "tun").Select(i => (string)i["tag"]).ToArray();
            var rules = config["route"]?["rules"] as JArray;
            if (tunTags == null || tunTags.Length == 0 || rules == null) return json;

            // A reject after sniff only closes an established connection. Put the
            // existing UDP/443 policy before sniff so TUN can reply with ICMP and
            // browsers can fall back to TCP. Never silently rate-limit to drop.
            var quicRules = rules.OfType<JObject>().Where(IsGenericQuicReject).ToArray();
            if (quicRules.Length == 0) return json;
            var coreRules = rules.OfType<JObject>().Where(r =>
                (r["process_name"] != null || r["process_path"] != null)
                && r["outbound"] != null && (string)r["outbound"] != "proxy").ToArray();
            var ordered = new JArray(coreRules.Select(r => r.DeepClone()));
            ordered.Add(new JObject { ["inbound"] = new JArray(tunTags),
                ["network"] = "udp", ["port"] = 443, ["action"] = "reject",
                ["method"] = "default", ["no_drop"] = true });
            foreach (var rule in rules)
                if (!quicRules.Any(r => ReferenceEquals(r, rule)) && !coreRules.Any(r => ReferenceEquals(r, rule)))
                    ordered.Add(rule.DeepClone());
            config["route"]["rules"] = ordered;
            return config.ToString(Formatting.None);
        }

        private static bool IsGenericQuicReject(JObject rule)
        {
            if ((string)rule["action"] != "reject" || (string)rule["network"] != "udp"
                || rule["port"]?.Type != JTokenType.Integer || (int)rule["port"] != 443) return false;
            // Leave conditional domain/IP/shield rules untouched.
            return rule.Properties().All(p => p.Value.Type == JTokenType.Null
                || new[] { "action", "network", "port", "inbound", "method", "no_drop" }.Contains(p.Name));
        }
    }
}
