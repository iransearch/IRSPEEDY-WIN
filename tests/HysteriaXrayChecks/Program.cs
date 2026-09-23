using System;
using System.Collections.Generic;
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

        // Regression: all seven candidates used to pass conversion, then Core
        // rejected geoip:private because the SRS runtime has no geoip.dat.
        var links = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            string link = "hy2://smart-fixture-" + i;
            ShareHandler.Nodes[link] = hy;
            links.Add(link);
        }
        bool ai;
        int members, hysteriaMembers;
        var smart = JObject.Parse(ConfigGenerator.GetSmartBalancerConfig(
            links, 19002, "test-user", "test-pass", null, out ai, out members, out hysteriaMembers));
        Check(members == 7 && hysteriaMembers == 7 && !ai, "Smart pool membership changed");
        foreach (string value in smart.Descendants().OfType<JValue>()
            .Where(v => v.Type == JTokenType.String).Select(v => (string)v))
            Check(!value.StartsWith("geoip:") && !value.StartsWith("geosite:") && !value.StartsWith("ext:"),
                "Smart config still requires an external geodata database: " + value);
        var rules = (JArray)smart["routing"]["rules"];
        Check((string)rules[0]["outboundTag"] == "block" && (string)rules[0]["port"] == "443", "QUIC block order changed");
        Check((string)rules.Last["balancerTag"] == "smart-balancer-1", "Public traffic lost its Smart pool");
        var localIps = (JArray)rules.Single(r => r["ip"] != null)["ip"];
        Check(localIps.Values<string>().Contains("192.168.0.0/16") && localIps.Values<string>().Contains("fc00::/7"),
            "IPv4/IPv6 local bypass lost");
        var localDomains = (JArray)rules.Single(r => r["domain"] != null && (string)r["outboundTag"] == "direct")["domain"];
        Check(localDomains.Values<string>().Contains("domain:localhost") && localDomains.Values<string>().Contains("domain:home.arpa"),
            "Local hostname bypass lost");
        Check((string)smart["routing"]["balancers"][0]["strategy"]["type"] == "leastLoad"
            && smart["routing"]["balancers"][0]["fallbackTag"] == null, "Balancer policy changed");
        Check((string)smart["burstObservatory"]["pingConfig"]["interval"] == "30m"
            && (int)smart["burstObservatory"]["pingConfig"]["sampling"] == 2, "Probe policy changed");

        // Country routing is applied before traffic enters the inner Xray pool.
        var outer = JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_clientSample);
        var iran = outer["route"]["rules"].Single(r => r["rule_set"] != null);
        Check((string)iran["outbound"] == "direct" && iran["rule_set"].Values<string>()
            .SequenceEqual(new[] { "ir_IP", "category-ir_SITE" }), "Outer Iran bypass changed");
        Check(outer["route"]["rule_set"].All(r => (string)r["format"] == "binary"
            && ((string)r["path"]).EndsWith(".srs")), "Country routing gained a DAT dependency");
        Console.WriteLine("PASS: seven-member Smart config needs no DAT files; local bypass, Iran SRS rules and pool policy preserved.");
    }
}
