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
        // A modern scoped fixture must not mix legacy DNS rule-action strategy
        // with modern predefined localhost responses (sing-box rejects that mix).
        ((JArray)config["dns"]["rules"]).Add(new JObject { ["action"] = "route", ["server"] = "dns-direct",
            ["domain"] = "bootstrap.example.org", ["query_type"] = new JArray("A", "AAAA") });
        var settings = new SplitTunnelSettings { Enabled = split };
        settings.Apps.Add(new SplitTunnelApp { Path = @"C:\Apps\browser.exe" });
        return JObject.Parse(SplitTunnelPolicyBuilder.Apply(config.ToString(), settings));
    }

    private static void Main()
    {
        var output = Environment.GetEnvironmentVariable("IRSPEEDY_DNS_FIXTURES");
        foreach (bool tun in new[] { false, true })
            foreach (bool split in new[] { false, true })
            {
                var before = Build(tun, split);
                var after = JObject.Parse(ParallelDnsRouting.Apply(before.ToString()));
                var servers = (JArray)after["dns"]["servers"];
                var remote = servers.Single(s => (string)s["tag"] == "dns-remote");
                Check((string)remote["type"] == "vpn-dns-race", "one native completion-order transport");
                Check(servers.Count == ((JArray)before["dns"]["servers"]).Count, "providers remain inside the composite");
                var primary = (JArray)remote["servers"];
                Check(primary.Select(s => (string)s["server"]).SequenceEqual(new[] { "8.8.8.8", "1.1.1.1", "77.88.8.8", "78.186.60.77" }), "all four providers preserved");
                Check(primary.Take(3).All(s => (string)s["type"] == "https" && (string)s["path"] == "/dns-query"
                    && (string)s["detour"] == "proxy" && (bool)s["tls"]["enabled"] && s["tls"]["insecure"] == null), "verified DoH through VPN");
                Check(primary.Take(3).Select(s => (string)s["tls"]["server_name"]).SequenceEqual(
                    new[] { "dns.google", "cloudflare-dns.com", "common.dot.dns.yandex.net" }), "provider TLS names");
                Check((string)primary[3]["type"] == "udp" && (int)primary[3]["server_port"] == 53
                    && (string)primary[3]["detour"] == "proxy", "additional UDP resolver through VPN");
                Check((string)remote["timeout"] == "8s" && (string)remote["fallback_timeout"] == "4s", "Android stage timeouts");
                var fallback = remote["fallback"];
                Check((string)fallback["type"] == "tcp" && (string)fallback["server"] == "1.1.1.1"
                    && (int)fallback["server_port"] == 53 && (string)fallback["detour"] == "proxy", "Android TCP fallback stays inside VPN");
                var routeWithoutResolve = (JObject)after["route"].DeepClone();
                foreach (var added in ((JArray)routeWithoutResolve["rules"]).OfType<JObject>()
                    .Where(r => (string)r["action"] == "resolve").ToArray()) added.Remove();
                Check(JToken.DeepEquals(before["route"], routeWithoutResolve) && JToken.DeepEquals(before["outbounds"], after["outbounds"])
                    && JToken.DeepEquals(before["inbounds"], after["inbounds"]), "existing routing and TUN preserved around scoped Proxy resolution");
                foreach (var direct in before["dns"]["servers"].Where(s => (string)s["tag"] != "dns-remote"))
                    Check(JToken.DeepEquals(direct, servers.Single(s => (string)s["tag"] == (string)direct["tag"])), "direct DNS transport preserved");
                var rules = after["dns"]["rules"].OfType<JObject>().ToArray();
                Check(JToken.DeepEquals(rules[0], before["dns"]["rules"][0]) && JToken.DeepEquals(rules[1], before["dns"]["rules"][1]), "localhost precedence preserved");
                Check(!rules.Any(r => (string)r["action"] == "evaluate" || (string)r["action"] == "respond"), "provider choice cannot depend on DNS rule order");
                Check(JToken.DeepEquals(before["dns"]["rules"].Single(r => (string)r["domain"] == "bootstrap.example.org"),
                    rules.Single(r => (string)r["domain"] == "bootstrap.example.org")), "bootstrap DNS scope and record types preserved");
                var remoteRules = rules.Where(r => (string)r["server"] == "dns-remote").ToArray();
                Check(remoteRules.Length == (tun && split ? 2 : 1) && remoteRules.All(r => (string)r["timeout"] == "12s"),
                    "shared Windows DNS and final VPN DNS allow the complete fallback budget");
                Check((string)rules.Last()["server"] == "dns-remote" && (string)rules.Last()["action"] == "route", "final DNS uses the composite");
                if (split)
                    Check(JToken.DeepEquals(before["dns"]["rules"].Single(r => (string)r["server"] == "split-dns-direct"),
                        rules.Single(r => (string)r["server"] == "split-dns-direct")), "unselected apps retain scoped direct DNS");
                Check(JToken.DeepEquals(after, JObject.Parse(ParallelDnsRouting.Apply(after.ToString()))), "idempotent");
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
        Check(JToken.DeepEquals(scoped["dns"]["rules"].Single(r => (string)r["domain"] == "scoped.example.org"),
            expanded["dns"]["rules"].Single(r => (string)r["domain"] == "scoped.example.org")), "explicit timeout/cache/scope preserved");
        Check(ParallelDnsRouting.Apply(Samples.sg_UrlTest) == Samples.sg_UrlTest, "server-test DNS byte-identical");
        var custom = Build(false, false); custom["dns"]["servers"][0]["server"] = "9.9.9.9";
        Check(ParallelDnsRouting.Apply(custom.ToString()) == custom.ToString(), "custom resolver unchanged");
        custom = Build(false, false); custom["dns"]["rules"][2]["strategy"] = "ipv4_only";
        var upgraded = JObject.Parse(ParallelDnsRouting.Apply(custom.ToString()));
        Check((string)upgraded["dns"]["rules"][2]["strategy"] == "ipv4_only", "custom DNS strategy preserved");
        custom = Build(false, false); custom["dns"]["final"] = "dns-direct";
        upgraded = JObject.Parse(ParallelDnsRouting.Apply(custom.ToString()));
        Check(JToken.DeepEquals(custom["dns"]["rules"], upgraded["dns"]["rules"]) && (string)upgraded["dns"]["final"] == "dns-direct", "explicit direct final is preserved");
        custom = Build(false, false);
        ((JArray)custom["dns"]["servers"]).Add(new JObject { ["type"] = "udp", ["tag"] = "dns-remote-additional", ["server"] = "192.0.2.53" });
        Check(ParallelDnsRouting.Apply(custom.ToString()) == custom.ToString(), "existing custom provider untouched");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "scoped.json"), expanded.ToString());
        ProxyDnsChecks.Run(Check, Build, output);
        Console.WriteLine("PASS " + checks + " parallel DNS configuration checks");
    }
}

