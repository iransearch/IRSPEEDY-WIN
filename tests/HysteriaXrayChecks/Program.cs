using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using IRSpeedyVPN.Services.Xray;
using v2rayN.Handler;
using v2rayN.Mode;

class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var hy = new VmessItem { configType = EConfigType.Hysteria2, address = "hy.example", port = 443,
            password = "test-password", obfs = "salamander", obfs_param = "test-obfs", sni = "tls.example", allowInsecure = "false" };
        ShareHandler.Nodes["hy2://fixture"] = hy;
        ShareHandler.Nodes["hysteria2://fixture"] = hy;
        ShareHandler.Nodes["vless://plain"] = new VmessItem { configType = EConfigType.VLESS, network = "tcp" };
        ShareHandler.Nodes["vless://xhttp"] = new VmessItem { configType = EConfigType.VLESS, network = "xhttp" };
        ShareHandler.Nodes["vless://reality"] = new VmessItem { configType = EConfigType.VLESS, network = "tcp", streamSecurity = "reality" };
        foreach (var link in new[] { "hy2://fixture", "hysteria2://fixture" })
        {
            Check(ConfigGenerator.LinkNeedsXrayForUrlTest(link), "Hysteria probe bypassed Xray");
            Check(!ConfigGenerator.LinkNeedsXray(link), "Single-server connection policy changed");
            Check(ConfigGenerator.UrlTestRefusalReason(link) == null, "Valid Hysteria probe rejected");
        }
        Check(!ConfigGenerator.LinkNeedsXrayForUrlTest("vless://plain"), "Plain VLESS route changed");
        Check(ConfigGenerator.LinkNeedsXrayForUrlTest("vless://xhttp"), "XHTTP route regressed");
        Check(ConfigGenerator.LinkNeedsXrayForUrlTest("vless://reality"), "Reality route regressed");
        Check(!ConfigGenerator.LinkNeedsXrayForUrlTest(null), "Null link accepted");
        Check(ConfigGenerator.UrlTestRefusalReason("invalid") == "invalid-link", "Invalid link accepted");

        var root = JObject.Parse(ConfigGenerator.GetUrlTestXrayConfig(new List<ConfigGenerator.XraySocksInfo> {
            new ConfigGenerator.XraySocksInfo { Link = "hy2://fixture", Tag = "xray-0", Port = 19001, User = "test-user", Pass = "test-pass" }
        }));
        var outbound = root["outbounds"][0];
        Check((string)outbound["protocol"] == "hysteria2", "Wrong Core adapter schema");
        Check((string)outbound["settings"]["server"] == hy.address && (int)outbound["settings"]["server_port"] == 443, "Server lost");
        Check((string)outbound["settings"]["password"] == hy.password, "Auth lost");
        Check((string)outbound["settings"]["obfs"]["password"] == hy.obfs_param, "Obfuscation lost");
        Check((bool)outbound["settings"]["tls"]["enabled"] && (string)outbound["settings"]["tls"]["server_name"] == hy.sni, "TLS lost");
        Check((string)root["inbounds"][0]["listen"] == "127.0.0.1" && (bool)root["inbounds"][0]["settings"]["udp"], "SOCKS bridge regressed");
        Check((string)root["routing"]["rules"][0]["outboundTag"] == "xray-0", "Probe routed to a different outbound");
        Console.WriteLine("PASS: Xray probe policy, connection-policy isolation, and Hysteria2 test configuration.");

        var links = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            var link = "hy2://smart-fixture-" + i;
            ShareHandler.Nodes[link] = hy;
            links.Add(link);
        }
        bool ai;
        int members, hysteriaMembers;
        var smart = ConfigGenerator.GetSmartBalancerConfig(links, 19002,
            "test-user", "test-pass", null, out ai, out members, out hysteriaMembers);
        Check(members == 7 && hysteriaMembers == 7 && !ai, "Smart pool membership changed");
        Check(ContainsXrayGeo(smart), "Prior-commit Xray geo routing was not restored");

        // Rebuild repeatedly in one process, with unchanged main pool and changed
        // AI members. This catches lifetime caching and accumulation of old rules.
        IRSpeedyVPN.Resource.RegHelper.AiSetting = "";
        Check(SmartIpRouting.IsEnabled(), "Unset AI differs from the UI default");
        for (int round = 0; round < 6; round++)
        {
            bool enabled = round % 2 == 0;
            SmartIpRouting.SetEnabled(enabled);
            var aiLinks = new[] { round < 2 ? "hy2://fixture" : "vless://reality" };
            var rebuilt = JObject.Parse(ConfigGenerator.GetSmartBalancerConfig(links, 19002,
                "test-user", "test-pass", aiLinks, out ai, out members, out hysteriaMembers));
            var aiRules = ((JArray)rebuilt["routing"]["rules"]).Where(r => (string)r["balancerTag"] == "ai-balancer").ToArray();
            var aiMembers = ((JArray)rebuilt["outbounds"]).Where(o => ((string)o["tag"]).StartsWith("ai-proxy-")).ToArray();
            Check(ai == enabled && aiRules.Length == (enabled ? 1 : 0) && aiMembers.Length == (enabled ? 1 : 0),
                "AI toggle was cached, or old AI rules/members survived the next build");
            Check(((JArray)rebuilt["routing"]["balancers"]).Count == (enabled ? 2 : 1), "Stale AI balancer remained");
            if (enabled)
            {
                Check(((JArray)rebuilt["routing"]["rules"]).IndexOf(aiRules[0]) == 1
                    && aiRules[0]["domain"].Values<string>().Contains("domain:showip.net"), "ShowIP AI rule lost priority");
                Check((string)aiMembers[0]["protocol"] == (round < 2 ? "hysteria2" : "vless"), "Stale AI API member retained");
            }
            Check((string)rebuilt["burstObservatory"]["pingConfig"]["interval"] == "15m"
                && (int)rebuilt["burstObservatory"]["pingConfig"]["sampling"] == 2, "Probe policy changed");
            Check((string)rebuilt["log"]["loglevel"] == "info", "Runtime route decisions remain hidden");
        }
        // One startup uses the captured setting even if a later registry read changes.
        SmartIpRouting.SetEnabled(false);
        ConfigGenerator.GetSmartBalancerConfig(links, 19002, "test-user", "test-pass",
            new[] { "hy2://fixture" }, out ai, out members, out hysteriaMembers, true);
        Check(ai, "Startup did not use its AI/VOD preference snapshot");
        Check(IRSpeedyVPN.Common.ConnectionDiagnostics.Lines.Any(l => l.Contains("ai-setting-saved"))
            && IRSpeedyVPN.Common.ConnectionDiagnostics.Lines.Any(l => l.Contains("accepted=1")), "Toggle/member diagnosis missing");
        Console.WriteLine("PASS: repeated AI on/off, current members, ShowIP rule, shared startup snapshot and preserved probe policy.");
        CheckAiIsolation(links);
        PoolDnsChecks.Run();

        var singBox = IRSpeedyVPN.Services.SingBox.Samples.sg_clientSample;
        var runtime = Path.Combine(Path.GetTempPath(), "IRSpeedy-GeoRouting-" + Guid.NewGuid().ToString("N"));
        var geoDir = Path.Combine(runtime, "geo");
        Directory.CreateDirectory(geoDir);
        try
        {
            var ordinary = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(singBox, null, runtime);
            Check(ordinary.XrayConfig == null && ordinary.MissingFiles.Length == 2
                && !ContainsCountryRule(ordinary.SingBoxConfig)
                && HasDnsHijack(ordinary.SingBoxConfig),
                "Ordinary connection still depends on missing geo or Xray DAT files");
            var absent = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(singBox, smart, runtime);
            Check(absent.MissingFiles.Length == 4, "Incomplete missing-geo diagnosis");
            Check(!ContainsCountryRule(absent.SingBoxConfig), "Missing SRS still used by sing-box");
            Check(!ContainsXrayGeo(absent.XrayConfig), "Missing DAT still used by Xray");
            Check(HasSmartCatchall(absent.XrayConfig) && HasDnsHijack(absent.SingBoxConfig),
                "Missing geo stopped unrelated connection routing");

            File.WriteAllText(Path.Combine(geoDir, "ir_IP.srs"), "fixture");
            File.WriteAllText(Path.Combine(geoDir, "category-ir_SITE.srs"), "fixture");
            File.WriteAllText(Path.Combine(runtime, "geoip.dat"), "fixture");
            var missingDat = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(singBox, smart, runtime);
            Check(!ContainsCountryRule(missingDat.SingBoxConfig) && !ContainsXrayGeo(missingDat.XrayConfig),
                "One missing DAT left half of country routing enabled");
            Check(missingDat.MissingFiles.SequenceEqual(new[] { "geosite.dat" }), "Wrong missing DAT file");

            File.WriteAllText(Path.Combine(runtime, "geosite.dat"), "fixture");
            File.Delete(Path.Combine(geoDir, "category-ir_SITE.srs"));
            var missingSrs = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(singBox, smart, runtime);
            Check(!ContainsCountryRule(missingSrs.SingBoxConfig) && !ContainsXrayGeo(missingSrs.XrayConfig),
                "One missing SRS left half of country routing enabled");

            File.WriteAllText(Path.Combine(geoDir, "category-ir_SITE.srs"), "fixture");
            var complete = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(singBox, smart, runtime);
            Check(complete.MissingFiles.Length == 0 && complete.SingBoxConfig == singBox
                && complete.XrayConfig == smart, "Complete geo runtime unexpectedly changed routing");

            var withShield = JObject.Parse(singBox);
            ((JArray)withShield["route"]["rule_set"]).Add(new JObject
            {
                ["type"] = "local", ["format"] = "binary", ["path"] = "geo/shield.srs", ["tag"] = "shield"
            });
            ((JArray)withShield["route"]["rules"]).Add(new JObject
            {
                ["action"] = "route", ["outbound"] = "block", ["rule_set"] = new JArray("shield")
            });
            var missingShield = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(withShield.ToString(), smart, runtime);
            Check(ContainsCountryRule(missingShield.SingBoxConfig) && ContainsXrayGeo(missingShield.XrayConfig),
                "Missing optional shield disabled intact country routing");
            Check(missingShield.MissingFiles.Contains("shield.srs")
                && !missingShield.SingBoxConfig.Contains("\"shield\""),
                "Missing shield left a broken local rule-set reference");
        }
        finally { Directory.Delete(runtime, true); }
        Console.WriteLine("PASS: restored Smart routing; missing/partial geo skips dependent rules and preserves the connection.");
    }

    static void CheckAiIsolation(List<string> mainLinks)
    {
        ShareHandler.Nodes["vless://refused-ai"] = new VmessItem {
            configType = EConfigType.VLESS, address = "public.example", port = 443, network = "tcp"
        };
        // Repeated valid/empty/rejected/off transitions must never retain a stale
        // service route. Include rejected links before the first valid member.
        var inputs = new string[][] {
            new[] { "invalid", "vless://refused-ai", "hy2://fixture", "vless://reality" },
            null, new string[0], new[] { " ", "invalid", "vless://refused-ai" },
            new[] { "vless://reality" }, null
        };
        var baseline = JObject.Parse(ConfigGenerator.GetSmartBalancerConfig(mainLinks, 19002,
            "test-user", "test-pass", null, out _, out _, out _, false));
        for (int i = 0; i < inputs.Length; i++)
        {
            bool enabled = i != inputs.Length - 1;
            bool policyActive;
            var config = JObject.Parse(ConfigGenerator.GetSmartBalancerConfig(mainLinks, 19002,
                "test-user", "test-pass", inputs[i], out policyActive, out _, out _, enabled));
            var outbounds = (JArray)config["outbounds"];
            var rules = (JArray)config["routing"]["rules"];
            var members = outbounds.Where(o => ((string)o["tag"]).StartsWith("ai-proxy-")).ToArray();
            var aiRules = rules.Where(r => r["domain"]?.Values<string>().Contains("domain:showip.net") == true).ToArray();
            var balancer = config["routing"]["balancers"].FirstOrDefault(b => (string)b["tag"] == "ai-balancer");
            Check(policyActive == enabled && aiRules.Length == (enabled ? 1 : 0), "Empty AI pool silently removed its policy");
            if (enabled)
            {
                Check(rules.IndexOf(aiRules[0]) == 1, "AI policy lost priority over direct/main rules");
                Check(aiRules[0]["domain"].Values<string>().SequenceEqual(SmartIpRouting.AiDomains.Select(d => "domain:" + d)),
                    "Empty AI protection lost service domains");
                if (members.Length > 0)
                {
                    Check(balancer != null && (string)balancer["fallbackTag"] == (string)members[0]["tag"],
                        "AI selection failure can escape to the main/default outbound");
                    Check((string)aiRules[0]["balancerTag"] == "ai-balancer" && aiRules[0]["outboundTag"] == null,
                        "Nonempty AI pool did not use its dedicated balancer");
                    Check(balancer["selector"].Values<string>().SequenceEqual(new[] { "ai-proxy-" }), "AI selector includes another pool");
                }
                else
                {
                    Check(balancer == null && (string)aiRules[0]["outboundTag"] == "block" && aiRules[0]["balancerTag"] == null,
                        "Empty/rejected AI traffic can escape to the main pool or Direct");
                    Check(outbounds.Any(o => (string)o["tag"] == "block" && (string)o["protocol"] == "blackhole"),
                        "AI block rule references no blackhole");
                }
            }
            else Check(balancer == null && members.Length == 0, "AI off retained blocking/pool state");
            Check(JToken.DeepEquals(config["routing"]["balancers"][0], baseline["routing"]["balancers"][0]), "Main pool policy changed");
            Check(JToken.DeepEquals(config["burstObservatory"]["pingConfig"], baseline["burstObservatory"]["pingConfig"]), "Internal probe policy changed");
        }
        Console.WriteLine("PASS: AI-only fallback, empty/rejected AI blocking, policy transitions, unchanged main pool/probes.");
    }

    static bool ContainsCountryRule(string json) => JObject.Parse(json)["route"]["rules"]
        .Any(rule => rule["rule_set"]?.Values<string>().Contains("ir_IP") == true);
    static bool HasDnsHijack(string json) => JObject.Parse(json)["route"]["rules"]
        .Any(rule => (string)rule["action"] == "hijack-dns");
    static bool ContainsXrayGeo(string json) => JObject.Parse(json)["routing"]["rules"]
        .Any(rule => (rule["ip"] as JArray)?.Values<string>().Any(v => v.StartsWith("geoip:")) == true
            || (rule["domain"] as JArray)?.Values<string>().Any(v => v.StartsWith("geosite:")) == true);
    static bool HasSmartCatchall(string json) => JObject.Parse(json)["routing"]["rules"]
        .Any(rule => (string)rule["balancerTag"] == "smart-balancer-1" && (string)rule["network"] == "tcp,udp");
}
