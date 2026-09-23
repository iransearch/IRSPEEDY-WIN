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
