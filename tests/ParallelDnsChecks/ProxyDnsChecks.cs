using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using IRSpeedyVPN.Services.SingBox;

internal static class ProxyDnsChecks
{
    internal static void Run(Action<bool, string> check, Func<bool, bool, JObject> build, string output)
    {
        var config = build(true, true);
        ((JArray)config["inbounds"]).Add(new JObject { ["type"] = "http", ["tag"] = "http-in" });
        ((JArray)config["inbounds"]).Add(new JObject { ["type"] = "socks", ["tag"] = "socks-in" });
        var rules = (JArray)config["route"]["rules"];
        var domainShield = new JObject { ["domain_suffix"] = new JArray("blocked.example"), ["action"] = "reject" };
        var ipShield = new JObject { ["ip_cidr"] = new JArray("192.0.2.90/32"), ["outbound"] = "block" };
        var setShield = new JObject { ["rule_set"] = new JArray("shield"), ["action"] = "route", ["outbound"] = "block" };
        rules.Add(domainShield); rules.Add(ipShield); rules.Add(setShield);
        var ai = new JObject { ["inbound"] = new JArray("mixed-in", "tun-in"),
            ["domain_suffix"] = new JArray("ai.example"), ["action"] = "route", ["outbound"] = "ai-proxy" };
        rules.Add(ai);
        var explicitMain = new JObject { ["type"] = "logical", ["mode"] = "and", ["rules"] = new JArray(
            new JObject { ["domain_suffix"] = new JArray("main.example") },
            new JObject { ["network"] = "tcp" }), ["action"] = "route", ["outbound"] = "proxy",
            ["udp_timeout"] = "30s", ["tls_record_fragment"] = true };
        rules.Add(explicitMain);

        var after = JObject.Parse(ParallelDnsRouting.Apply(config.ToString()));
        var routes = (JArray)after["route"]["rules"];
        var resolves = routes.OfType<JObject>().Where(r => (string)r["action"] == "resolve").ToArray();
        check(resolves.Length == 3, "AI, explicit main and final VPN destinations resolve");
        check(resolves.All(r => r["server"] == null && (string)r["timeout"] == "12s"), "resolve retains Shield/bootstrap/scoped DNS routing and complete race budget");
        foreach (var resolve in resolves)
        {
            var scope = (string)resolve["type"] == "logical" ? resolve["rules"][0] : resolve;
            check(scope["inbound"].Values<string>().SequenceEqual(new[] { "mixed-in", "http-in", "socks-in" }), "only HTTP/SOCKS user inbounds; never TUN/probes");
            int index = routes.IndexOf(resolve);
            var guards = routes.Skip(index + 1).Take(3).ToArray();
            check(guards.All(g => g["rules"][0]["inbound"].Values<string>()
                .SequenceEqual(new[] { "mixed-in", "http-in", "socks-in" }))
                && (string)guards[0]["action"] == "reject" && (string)guards[1]["outbound"] == "block"
                && JToken.DeepEquals(guards[2]["rules"][1]["rule_set"], setShield["rule_set"]),
                "Shield rechecks resolved addresses only for proxy traffic");
        }
        check(routes.IndexOf(routes.First(r => JToken.DeepEquals(r, domainShield))) < routes.IndexOf(resolves[0]), "known blocked domain is denied before DNS lookup");
        check(JToken.DeepEquals(resolves[0]["rules"][1]["domain_suffix"], ai["domain_suffix"])
            && (string)routes[routes.IndexOf(resolves[0]) + 4]["outbound"] == "ai-proxy", "AI matches original domain after resolution");
        check(resolves[1]["rules"][1]["udp_timeout"] == null && resolves[1]["rules"][1]["tls_record_fragment"] == null,
            "action-specific fields do not contaminate resolve conditions");
        check(routes.Any(r => JToken.DeepEquals(r, explicitMain)), "original routing action options preserved");
        check(JToken.DeepEquals(config["dns"]["rules"].Single(r => (string)r["domain"] == "bootstrap.example.org"),
            after["dns"]["rules"].Single(r => (string)r["domain"] == "bootstrap.example.org")), "bootstrap direct DNS remains unchanged");
        check(JToken.DeepEquals(after, JObject.Parse(ParallelDnsRouting.Apply(after.ToString()))), "Proxy and Shield rule bundles are idempotent");
        check(JToken.DeepEquals(config["inbounds"], after["inbounds"]) && JToken.DeepEquals(config["outbounds"], after["outbounds"]), "inbounds and Pool members are unchanged");
        var splitRule = routes.OfType<JObject>().First(r => (string)r["outbound"] == "split-direct");
        check(routes.IndexOf(splitRule) < routes.IndexOf(resolves[0]), "direct app bypass retains precedence");

        var tunOnly = build(true, false);
        ((JArray)tunOnly["inbounds"]).RemoveAt(0);
        var tunAfter = JObject.Parse(ParallelDnsRouting.Apply(tunOnly.ToString()));
        check(JToken.DeepEquals(tunOnly["route"], tunAfter["route"]), "TUN-only routing is byte-equivalent as JSON");
        var direct = build(false, false); direct["route"]["final"] = "direct";
        var directAfter = JObject.Parse(ParallelDnsRouting.Apply(direct.ToString()));
        check(JToken.DeepEquals(direct["route"], directAfter["route"]), "direct catch-all does not gain VPN resolution");
        var already = build(false, false);
        already = JObject.Parse(ParallelDnsRouting.Apply(already.ToString()));
        ((JArray)already["route"]["rules"]).Add(new JObject { ["domain_suffix"] = "later.ai.example", ["outbound"] = "ai-proxy" });
        var updated = JObject.Parse(ParallelDnsRouting.Apply(already.ToString()));
        check(updated["route"]["rules"].OfType<JObject>().Count(r => (string)r["action"] == "resolve") == 3,
            "existing native race profiles also receive subsequently added VPN rules");
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllText(Path.Combine(output, "proxy-shield-ai.json"), after.ToString());
            var behavior = build(false, false);
            behavior["route"]["rules"] = new JArray(
                new JObject { ["inbound"] = "mixed-in", ["action"] = "sniff" },
                new JObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                new JObject { ["domain"] = "direct.example", ["outbound"] = "split-direct" },
                new JObject { ["domain"] = "blocked.example", ["action"] = "reject" },
                new JObject { ["ip_cidr"] = new JArray("192.0.2.90/32"), ["outbound"] = "block" },
                new JObject { ["rule_set"] = "shield", ["outbound"] = "block" },
                new JObject { ["domain_suffix"] = "ai.example", ["outbound"] = "ai-proxy" });
            behavior["route"]["rule_set"] = new JArray(new JObject { ["type"] = "inline", ["tag"] = "shield",
                ["rules"] = new JArray(new JObject { ["domain"] = "shield-set.example" },
                    new JObject { ["ip_cidr"] = new JArray("192.0.2.91/32") }) });
            ((JArray)behavior["dns"]["rules"]).Insert(2, new JObject { ["domain"] = "dns-blocked.example",
                ["action"] = "predefined", ["rcode"] = "NXDOMAIN" });
            File.WriteAllText(Path.Combine(output, "proxy-behavior.json"), ParallelDnsRouting.Apply(behavior.ToString()));
        }
    }
}
