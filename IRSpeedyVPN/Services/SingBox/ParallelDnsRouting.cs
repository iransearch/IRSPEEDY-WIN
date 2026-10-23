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
            return config.ToString(Formatting.None);
        }
    }
}
