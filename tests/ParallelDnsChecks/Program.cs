using IRSpeedyVPN.Services.SingBox;
using IRSpeedyVPN.Services.SplitTunneling;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }

    private static JObject Build(bool tun, bool split)
    {
        var config = JObject.Parse(Samples.sg_clientSample);
        config["inbounds"] = new JArray(JObject.Parse(Samples.sg_mixedInbound));
        if (tun) ((JArray)config["inbounds"]).Add(JObject.Parse(Samples.sg_vpnInbound));
        ((JArray)config["dns"]["rules"]).Add(new JObject { ["action"] = "route", ["server"] = "dns-direct",
            ["domain"] = "bootstrap.example.org", ["strategy"] = "prefer_ipv4" });
        var settings = new SplitTunnelSettings { Enabled = split };
        settings.Apps.Add(new SplitTunnelApp { Path = @"C:\Apps\browser.exe" });
        return JObject.Parse(SplitTunnelPolicyBuilder.Apply(config.ToString(), settings));
    }

    private static void Main(string[] args)
    {
        var output = Environment.GetEnvironmentVariable("IRSPEEDY_DNS_FIXTURES");
        foreach (bool tun in new[] { false, true })
            foreach (bool split in new[] { false, true })
            {
                var before = Build(tun, split);
                var after = JObject.Parse(ParallelDnsRouting.Apply(before.ToString()));
                var servers = (JArray)after["dns"]["servers"];
                Check(servers.Take(4).Select(s => (string)s["server"]).SequenceEqual(new[] { "8.8.8.8", "1.1.1.1", "77.88.8.8", "78.186.60.77" }), "existing providers retain their order followed by the additional resolver");
                Check(servers.Take(3).All(s => (string)s["type"] == "https" && (string)s["path"] == "/dns-query"
                    && (string)s["detour"] == "proxy" && (bool)s["tls"]["enabled"] && s["tls"]["insecure"] == null), "all DoH queries use verified TLS through VPN");
                Check(servers.Take(3).Select(s => (string)s["tls"]["server_name"]).SequenceEqual(
                    new[] { "dns.google", "cloudflare-dns.com", "common.dot.dns.yandex.net" }), "correct provider TLS names");
                var additional = servers.Single(s => (string)s["tag"] == "dns-remote-additional");
                Check((string)additional["type"] == "udp" && (int)additional["server_port"] == 53
                    && (string)additional["detour"] == "proxy" && additional["tls"] == null && additional["path"] == null,
                    "additional ordinary DNS resolver uses UDP/53 through VPN");
                Check(JToken.DeepEquals(before["route"], after["route"]) && JToken.DeepEquals(before["outbounds"], after["outbounds"])
                    && JToken.DeepEquals(before["inbounds"], after["inbounds"]), "traffic/AI/core/TUN routing preserved");
                foreach (var direct in before["dns"]["servers"].Where(s => (string)s["tag"] != "dns-remote"))
                    Check(JToken.DeepEquals(direct, servers.Single(s => (string)s["tag"] == (string)direct["tag"])), "direct DNS transport preserved");
                var rules = after["dns"]["rules"].OfType<JObject>().ToArray();
                Check(JToken.DeepEquals(rules[0], before["dns"]["rules"][0]) && JToken.DeepEquals(rules[1], before["dns"]["rules"][1]), "localhost precedence preserved");
                Check(!rules.SelectMany(r => r.DescendantsAndSelf().OfType<JObject>()).Any(r => !string.IsNullOrEmpty((string)r["strategy"])), "no legacy DNS strategy with race");
                Check((string)rules.Single(r => (string)r["server"] == "dns-direct")["domain"] == "bootstrap.example.org"
                    && (string)after["route"]["default_domain_resolver"]["strategy"] == "prefer_ipv4", "bootstrap DNS and dialer IPv4 preference preserved");
                var groups = rules.Where(r => (string)r["action"] == "evaluate").ToArray();
                Check(groups.Length == (tun && split ? 8 : 4), "shared Windows DNS also races in TUN split mode");
                for (int group = 0; group < groups.Length; group += 4)
                {
                    Check(groups.Skip(group).Take(4).Select(r => (string)r["server"]).SequenceEqual(
                        new[] { "dns-remote", "dns-remote-cloudflare", "dns-remote-yandex", "dns-remote-additional" }), "all providers launched before response matching");
                    foreach (var evaluate in groups.Skip(group).Take(4))
                    {
                        var responses = rules.Where(r => (string)r["match_response"] == (string)evaluate["tag"]).ToArray();
                        Check(responses.Length == 2 && responses.All(r => (string)r["action"] == "respond" && (bool)r["race"])
                            && responses.Select(r => (string)r["response_rcode"]).SequenceEqual(new[] { "NOERROR", "NXDOMAIN" }), "valid positive/negative answers race; server errors excluded");
                    }
                }
                Check((string)rules.Last()["action"] == "predefined" && (string)rules.Last()["rcode"] == "SERVFAIL", "all providers failing cannot retry sequentially or leak to direct DNS");
                if (split)
                    Check(JToken.DeepEquals(before["dns"]["rules"].Single(r => (string)r["server"] == "split-dns-direct"),
                        rules.Single(r => (string)r["server"] == "split-dns-direct")), "unselected apps retain scoped direct DNS");
                Check(JToken.DeepEquals(after, JObject.Parse(ParallelDnsRouting.Apply(after.ToString()))), "reapplying does not duplicate races");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, (split ? "split-" : "") + (tun ? "tun" : "proxy") + ".json"), after.ToString());
                }
            }
        var scoped = Build(false, false);
        ((JArray)scoped["dns"]["rules"]).Insert(2, new JObject { ["action"] = "route", ["server"] = "dns-remote",
            ["domain"] = "scoped.example.org", ["disable_cache"] = true, ["timeout"] = "2s" });
        var expanded = JObject.Parse(ParallelDnsRouting.Apply(scoped.ToString()));
        var scopedRules = expanded["dns"]["rules"].OfType<JObject>().Where(r => (string)r["domain"] == "scoped.example.org").ToArray();
        Check(scopedRules.Length == 5 && scopedRules.Take(4).All(r => (bool)r["disable_cache"] && (string)r["timeout"] == "2s")
            && (string)scopedRules.Last()["rcode"] == "SERVFAIL" && scopedRules.Last()["timeout"] == null, "explicit remote route preserves scope and exchange options");
        Check(ParallelDnsRouting.Apply(Samples.sg_UrlTest) == Samples.sg_UrlTest, "server-test DNS remains byte-identical");
        var custom = Build(false, false); custom["dns"]["servers"][0]["server"] = "9.9.9.9";
        Check(ParallelDnsRouting.Apply(custom.ToString()) == custom.ToString(), "custom resolver unchanged");
        custom = Build(false, false); custom["dns"]["rules"][2]["strategy"] = "ipv4_only";
        Check(ParallelDnsRouting.Apply(custom.ToString()) == custom.ToString(), "custom DNS strategy never silently removed");
        custom = Build(false, false);
        ((JArray)custom["dns"]["servers"]).Add(new JObject { ["type"] = "udp", ["tag"] = "dns-remote-additional", ["server"] = "192.0.2.53" });
        Check(ParallelDnsRouting.Apply(custom.ToString()) == custom.ToString(), "an existing additional resolver tag cannot be duplicated or overwritten");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "scoped.json"), expanded.ToString());
        Console.WriteLine("PASS " + checks + " parallel DNS configuration checks");
    }
}
