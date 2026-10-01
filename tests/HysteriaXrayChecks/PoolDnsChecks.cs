using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IRSpeedyVPN.Services.Libcore;
using IRSpeedyVPN.Services.Xray;
using Newtonsoft.Json.Linq;
using v2rayN.Handler;
using v2rayN.Mode;

internal static class PoolDnsChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Run()
    {
        ShareHandler.Nodes["vless://pool-grpc"] = Node("grpc", "grpc.pool.example");
        ShareHandler.Nodes["vless://pool-ip"] = Node("grpc", "192.0.2.7");
        var xhttp = Node("xhttp", "upload.pool.example");
        xhttp.transportExtra = @"{
            ""downloadSettings"": {
                ""address"": ""download.pool.example"", ""port"": 443,
                ""network"": ""xhttp"", ""security"": ""tls"",
                ""tlsSettings"": { ""serverName"": ""download-tls.example"" },
                ""sockopt"": { ""domainStrategy"": ""UseIPv4"", ""mark"": 37, ""tcpKeepAliveIdle"": 42 }
            }
        }";
        ShareHandler.Nodes["vless://pool-xhttp"] = xhttp;
        var links = new[] { "hy2://fixture", "vless://pool-grpc", "vless://pool-xhttp", "vless://pool-ip" };
        var config = JObject.Parse(ConfigGenerator.GetSmartBalancerConfig(links, 19003,
            "user", "pass", links, out var ai, out var members, out var hysteria, true));
        Check(ai && members == 4 && hysteria == 1, "DNS integration changed accepted Pool members");
        var poolMembers = ((JArray)config["outbounds"]).OfType<JObject>()
            .Where(o => ((string)o["tag"]).StartsWith("smart-proxy-")
                || ((string)o["tag"]).StartsWith("ai-proxy-")).ToArray();
        Check(poolMembers.Length == 8, "Main and AI Pool fixtures were not both generated");
        foreach (var outbound in poolMembers)
        {
            Check(!outbound.SelectTokens("$..sockopt.domainStrategy").Any(),
                "Pool socket DNS overrides the Core resolver: " + outbound["tag"]);
            if ((string)outbound["protocol"] == "hysteria2")
            {
                Check((string)outbound["settings"]["server"] == "hy.example"
                    && outbound["streamSettings"] == null, "Hysteria server/adapter changed");
                continue;
            }
            var stream = outbound["streamSettings"];
            var server = (string)outbound["settings"]["address"];
            Check(server == "grpc.pool.example" || server == "upload.pool.example" || server == "192.0.2.7",
                "DNS integration rewrote the server address");
            Check((string)stream["tlsSettings"]["serverName"] == "tls.pool.example", "TLS SNI changed");
            if ((string)stream["network"] == "grpc")
                Check((string)stream["grpcSettings"]["serviceName"] == "/test", "gRPC transport changed");
            else
            {
                var download = stream["xhttpSettings"]["extra"]["downloadSettings"];
                Check((string)download["address"] == "download.pool.example"
                    && (string)download["tlsSettings"]["serverName"] == "download-tls.example"
                    && (int)download["sockopt"]["mark"] == 37
                    && (int)download["sockopt"]["tcpKeepAliveIdle"] == 42,
                    "XHTTP download transport/socket options changed");
            }
        }

        // Pool-only normalization must not mutate parsed nodes or the separate
        // single-server paths, even after reusing the same XHTTP extras.
        var single = JObject.Parse(ConfigGenerator.GetConfig("vless://pool-xhttp", 19004, "user", "pass"));
        var singleStream = single["outbounds"][0]["streamSettings"];
        Check((string)singleStream["sockopt"]["domainStrategy"] == "UseIP"
            && (string)singleStream["xhttpSettings"]["extra"]["downloadSettings"]["sockopt"]["domainStrategy"] == "UseIPv4",
            "Pool DNS normalization leaked into single-server configuration");
        var probe = JObject.Parse(ConfigGenerator.GetUrlTestXrayConfig(new List<ConfigGenerator.XraySocksInfo> {
            new ConfigGenerator.XraySocksInfo { Link = "vless://pool-grpc", Tag = "probe", Port = 19005, User = "user", Pass = "pass" }
        }));
        Check((string)probe["outbounds"][0]["streamSettings"]["sockopt"]["domainStrategy"] == "UseIP",
            "DNS normalization changed a probe without the Core DNS opt-in");


        var box = JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_clientSample);
        var direct = box["dns"]["servers"].Single(s => (string)s["tag"] == "dns-direct");
        Check((string)direct["type"] == "udp" && (string)direct["server"] == "1.1.1.1"
            && direct["detour"] == null, "Pool bootstrap DNS now depends on the proxy");
        Check((string)box["route"]["default_domain_resolver"]["server"] == "dns-direct"
            && (bool)box["route"]["auto_detect_interface"], "Direct DNS lost physical-interface routing");

        var remote = box["dns"]["servers"].Single(s => (string)s["tag"] == "dns-remote");
        Check((string)remote["type"] == "https" && (string)remote["server"] == "1.1.1.1"
            && (string)remote["path"] == "/dns-query" && (string)remote["detour"] == "proxy",
            "Website DNS is not Cloudflare DoH inside the VPN");
        var serializedRemote = JObject.FromObject(remote.ToObject<IRSpeedyVPN.Services.SingBox.DnsServer>());
        Check((string)serializedRemote["path"] == "/dns-query"
            && (string)serializedRemote["detour"] == "proxy", "DNS model discarded DoH path or VPN detour");
        var probeBox = JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_UrlTest);
        var probeDirect = probeBox["dns"]["servers"].Single(x => (string)x["tag"] == "dns-direct");
        Check((string)probeDirect["type"] == "local"
            && (string)probeDirect["domain_resolver"] == "dns-local"
            && (string)probeBox["route"]["default_domain_resolver"]["server"] == "dns-direct"
            && string.IsNullOrEmpty((string)probeBox["route"]["default_domain_resolver"]["strategy"]),
            "Server test DNS is no longer isolated from live Pool DNS");
        Check((string)probe["outbounds"][0]["streamSettings"]["sockopt"]["domainStrategy"] == "UseIP",
            "Server test Xray sockets inherited live Pool DNS");

        CheckStartWire(config.ToString());
        Console.WriteLine("PASS: live Cloudflare DoH/ForceIP preserved; server-test DNS and Xray probe routing remain isolated.");
    }

    private static VmessItem Node(string network, string address)
    {
        return new VmessItem { configType = EConfigType.VLESS, address = address, port = 443,
            network = network, streamSecurity = "tls", sni = "tls.pool.example", path = "/test",
            fingerPrint = "chrome", id = "00000000-0000-4000-8000-000000000001" };
    }

    private static void CheckStartWire(string config)
    {
        var request = new LoadConfigReq { CoreConfig = "{\"dns\":{}}", NeedXray = true, XrayConfig = config };
        var unwired = LibcoreProto.EncodeLoadConfigReq(request);
        request.XrayOutboundDnsStrategy = "";
        Check(unwired.SequenceEqual(LibcoreProto.EncodeLoadConfigReq(request)), "Unwired RPC bytes changed");
        request.XrayOutboundDnsStrategy = SmartIpRouting.OutboundDnsStrategy;
        // Throne libcore.proto: field 13, wire type 2, seven ASCII bytes.
        // ForceIP must propagate DNS errors instead of silently using system DNS.
        var field13 = new byte[] { 0x6a, 0x07 }.Concat(Encoding.ASCII.GetBytes("ForceIP"));
        Check(LibcoreProto.EncodeLoadConfigReq(request).SequenceEqual(unwired.Concat(field13)),
            "Start did not transmit the strict DNS strategy under Core's field 13");
    }

}
