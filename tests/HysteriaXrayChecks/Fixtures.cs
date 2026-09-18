using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// Isolate the production Xray generator from WPF and the link parser.
// Tests supply already-parsed nodes; this harness does not test link parsing.
namespace v2rayN.Base { }
namespace IRSpeedyVPN.Common
{
    public static class LogHelper { public static void WriteLog(Exception error) { throw new Exception("Generator log", error); } }
}
namespace IRSpeedyVPN.Resource
{
    public static class RegHelper { public static string GetSettingValue(string key) { return "0"; } }
}
namespace v2rayN
{
    public static class Global
    {
        public const string RealitySecurity = "reality", StreamSecurity = "tls", StreamSecurityX = "xtls",
            TcpHeaderHttp = "http", GrpcmultiMode = "multi";
    }
    public static class Utils
    {
        public static string ToJson(object value) { return JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }); }
        public static bool ToBool(string value) { return value == "true" || value == "1"; }
        public static List<string> String2List(string value) { return new List<string>(value.Split(',')); }
        public static object ParseJson(string value) { return JsonConvert.DeserializeObject<Dictionary<string, object>>(value); }
        public static void SaveLog(string message, Exception error) { throw new Exception("Generator failure", error); }
    }
}
namespace v2rayN.Mode
{
    public enum EConfigType { VMess, VLESS, Trojan, Shadowsocks, Socks, Http, Hysteria2 }
    public class VmessItem
    {
        public EConfigType configType;
        public int port, alterId;
        public string address, allowInsecure, certSha256, fingerPrint, flow, headerType, id, obfs,
            obfs_param, password, path, publicKey, requestHost, security, shortId, sni, spiderX,
            streamSecurity, transportExtra, network;
        public string GetNetwork() { return network; }
        public List<string> GetAlpn() { return null; }
    }
}
namespace v2rayN.Handler
{
    public static class ShareHandler
    {
        public static readonly Dictionary<string, v2rayN.Mode.VmessItem> Nodes = new Dictionary<string, v2rayN.Mode.VmessItem>();
        public static v2rayN.Mode.VmessItem ImportFromConfigLink(string link, out string message)
        {
            message = "";
            v2rayN.Mode.VmessItem node;
            return Nodes.TryGetValue(link, out node) ? node : null;
        }
    }
}
