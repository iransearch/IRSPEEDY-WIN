using System;
using System.Collections.Generic;
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
    }
}
