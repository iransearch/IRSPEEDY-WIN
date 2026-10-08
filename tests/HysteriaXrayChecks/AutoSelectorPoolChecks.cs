using System;
using System.Collections.Generic;
using System.Linq;
using IRSpeedyVPN.Services.Xray;
using Newtonsoft.Json.Linq;

internal static class AutoSelectorPoolChecks
{
    private static int passed;
    private static void Check(bool valid, string name)
    {
        if (!valid) throw new Exception(name);
        Console.WriteLine("PASS " + name); passed++;
    }

    internal static void Run()
    {
        int next = 24000;
        var released = new List<int>();
        var links = new[] { "hy2://fixture", "vless://reality", "missing://invalid" };
        using (var plan = AutoSelectorPoolPlan.Create(links, new[] { "hy2://fixture" }, true,
            () => next++, released.Add))
        {
            Check(plan.MainMembers == 2 && plan.AiMembers == 1 && plan.HysteriaMembers == 1,
                "valid main/AI membership and Hysteria transport preserved");
            var xray = JObject.Parse(plan.XrayConfig);
            var ins = ((JArray)xray["inbounds"]).OfType<JObject>().ToArray();
            var routes = ((JArray)xray["routing"]["rules"]).OfType<JObject>().ToArray();
            Check(ins.Length == 3 && ins.Select(i => (int)i["port"]).Distinct().Count() == 3
                && ins.All(i => (string)i["listen"] == "127.0.0.1"
                    && (string)i["settings"]["accounts"][0]["user"] == plan.User
                    && (string)i["settings"]["accounts"][0]["pass"] == plan.Password),
                "every member has its own authenticated loopback SOCKS listener");
            Check(xray["burstObservatory"] == null && xray["routing"]["balancers"] == null,
                "nested Xray health checks and balancers removed");
            Check(routes.Length == ins.Length && ins.All(i => routes.Count(r =>
                (string)r["inboundTag"][0] == (string)i["tag"]
                && (string)i["tag"] == "bridge-" + (string)r["outboundTag"]) == 1),
                "each bridge routes directly to exactly its own Xray member");
            Check(!xray.SelectTokens("$..sockopt.domainStrategy").Any(), "members inherit the Core direct DNS resolver");
            foreach (bool tun in new[] { false, true })
            {
                var baseConfig = BaseConfig(tun);
                var original = (JObject)baseConfig.DeepClone();
                var config = JObject.Parse(plan.Apply(baseConfig.ToString()));
                var outbounds = ((JArray)config["outbounds"]).OfType<JObject>().ToArray();
                var main = outbounds.Single(o => (string)o["tag"] == "proxy");
                var ai = outbounds.Single(o => (string)o["tag"] == "ai-proxy");
                Check((string)main["type"] == "auto-selector-round-robin" && (string)ai["type"] == "auto-selector"
                    && main["outbounds"].Values<string>().All(t => t.StartsWith("smart-proxy-"))
                    && ai["outbounds"].Values<string>().All(t => t.StartsWith("ai-proxy-"))
                    && main["fallbackTag"] == null && ai["fallbackTag"] == null,
                    "main and AI selection stay independent in " + (tun ? "TUN" : "proxy"));
                Check((string)main["interval"] == "900s" && (string)main["bench_interval"] == "900s"
                    && (string)main["watch_interval"] == "300s" && (int)main["sampling"] == 10
                    && (int)main["tolerance"] == 300 && (int)ai["tolerance"] == 300
                    && main["max_rtt"] == null && ai["max_rtt"] == null
                    && (int)main["expected"] == 2 && (int)main["active_size"] == 2
                    && (bool)main["balance"] && (string)main["balance_mode"] == "round-robin"
                    && (string)ai["balance_mode"] == "connection"
                    && !(bool)main["interrupt_exist_connections"] && (int)ai["expected"] == 1,
                    "health policy, small-pool limits and existing-session stability in " + (tun ? "TUN" : "proxy"));
                Check(JToken.DeepEquals(config["dns"], original["dns"])
                    && JToken.DeepEquals(config["inbounds"], original["inbounds"])
                    && (string)config["route"]["final"] == (tun ? "direct" : "proxy")
                    && outbounds.Any(o => (string)o["tag"] == "irancell")
                    && outbounds.Where(o => (string)o["type"] == "socks").All(o => (string)o["detour"] == "chain-default-1"),
                    "DNS, sharing inbound, VOD, chain and Game Mode final preserved in " + (tun ? "TUN" : "proxy"));
                var rules = (JArray)config["route"]["rules"];
                var aiRule = rules.OfType<JObject>().Single(r => (string)r["outbound"] == "ai-proxy");
                Check(aiRule["domain_suffix"].Values<string>().Contains("showip.net")
                    && aiRule["inbound"].Values<string>().SequenceEqual(new[] { tun ? "tun-in" : "mixed-in" })
                    && rules.IndexOf(aiRule) > rules.IndexOf(rules.Single(r => (string)r["outbound"] == "block"))
                    && rules.IndexOf(aiRule) < rules.IndexOf(rules.Single(r => r["rule_set"] != null))
                    && rules.Any(r => (string)r["action"] == "reject" && (string)r["network"] == "udp" && (int?)r["port"] == 443),
                    "AI routing follows Shield/protocol handling and precedes geo with UDP/443 rejection");
                Check(outbounds.Where(o => (string)o["type"] == "socks"
                    && ((string)o["tag"]).Contains("-proxy-")).All(b =>
                    ins.Count(i => (int)i["port"] == (int)b["server_port"]
                        && (string)i["tag"] == "bridge-" + (string)b["tag"]) == 1),
                    "sing-box members reach matching Xray listeners without collapsing the pool");
            }
        }
        Check(released.Distinct().Count() == 3 && released.Count == 3, "all bridge ports released once after disposal");
        using (var emptyAi = AutoSelectorPoolPlan.Create(links, new[] { "missing://invalid" }, true, () => next++, released.Add))
        {
            var config = JObject.Parse(emptyAi.Apply(BaseConfig(false).ToString()));
            Check(emptyAi.AiMembers == 0 && !config["outbounds"].Any(o => (string)o["tag"] == "ai-proxy")
                && config["route"]["rules"].Any(r => r["domain_suffix"] != null && (string)r["action"] == "reject"),
                "enabled but empty AI pool fails closed instead of falling into main");
        }
        using (var disabled = AutoSelectorPoolPlan.Create(links, new[] { "hy2://fixture" }, false, () => next++, released.Add))
        {
            var config = JObject.Parse(disabled.Apply(BaseConfig(false).ToString()));
            Check(disabled.AiMembers == 0 && !config["route"]["rules"].Any(r => r["domain_suffix"] != null
                && r["domain_suffix"].Values<string>().Contains("showip.net")), "AI off adds no AI group or policy");
        }
        int before = released.Count, attempts = 0;
        try
        {
            AutoSelectorPoolPlan.Create(links, new[] { "hy2://fixture" }, true,
                () => { if (++attempts == 3) throw new InvalidOperationException("no port"); return next++; }, released.Add);
            throw new Exception("allocator failure not propagated");
        }
        catch (InvalidOperationException) { }
        Check(released.Count == before + 2, "partial build failure releases allocated bridge ports");
        var disposable = AutoSelectorPoolPlan.Create(links, null, false, () => next++, released.Add);
        before = released.Count; disposable.Dispose(); disposable.Dispose();
        Check(released.Count == before + 2, "repeated teardown cannot return ports twice");
        Check(AutoSelectorPoolPlan.Create(new[] { "missing://invalid" }, null, false,
            () => { throw new Exception("unexpected allocation"); }, released.Add) == null,
            "no accepted main member prevents configuration and allocates no ports");
        foreach (int count in new[] { 9, 35, 36 })
        {
            var largeLinks = Enumerable.Range(0, count).Select(i => "hy2://auto-" + i).ToArray();
            foreach (var link in largeLinks)
                v2rayN.Handler.ShareHandler.Nodes[link] = v2rayN.Handler.ShareHandler.Nodes["hy2://fixture"];
            using (var large = AutoSelectorPoolPlan.Create(largeLinks, largeLinks, true, () => next++, released.Add))
            {
                var config = JObject.Parse(large.Apply(BaseConfig(false).ToString()));
                var group = config["outbounds"].Single(o => (string)o["tag"] == "proxy");
                var aiGroup = config["outbounds"].Single(o => (string)o["tag"] == "ai-proxy");
                Check(group["outbounds"].Count() == count && aiGroup["outbounds"].Count() == count
                    && (int)group["expected"] == 3 && (int)aiGroup["expected"] == 3
                    && (int)group["active_size"] == Math.Min(35, count)
                    && (int)aiGroup["active_size"] == Math.Min(35, count),
                    "main/AI pools retain all " + count + " members with three ready and at most 35 closely checked");
            }
        }
        Console.WriteLine(passed + " Auto Selector pool checks passed.");
    }

    private static JObject BaseConfig(bool tun)
    {
        var root = JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_clientSample);
        root["inbounds"] = new JArray(new JObject
        {
            ["type"] = tun ? "tun" : "mixed", ["tag"] = tun ? "tun-in" : "mixed-in",
            ["listen"] = "0.0.0.0", ["listen_port"] = 10808
        });
        root["outbounds"][0]["type"] = "socks";
        root["outbounds"][0]["detour"] = "chain-default-1";
        ((JArray)root["outbounds"]).Add(new JObject { ["type"] = "direct", ["tag"] = "chain-default-1" });
        ((JArray)root["outbounds"]).Add(new JObject { ["type"] = "socks", ["tag"] = "irancell", ["server_port"] = 29000, ["detour"] = "chain-default-1" });
        ((JArray)root["route"]["rules"]).Add(new JObject { ["domain"] = new JArray("ads.invalid"), ["outbound"] = "block" });
        if (tun) root["route"]["final"] = "direct";
        return root;
    }
}
