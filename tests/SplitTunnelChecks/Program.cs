using IRSpeedyVPN.Services.SplitTunneling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Resource
{
    internal static class RegHelper
    {
        internal static Dictionary<string, string> Values = new Dictionary<string, string>();
        public static string GetSettingValue(string key) => Values.ContainsKey(key) ? Values[key] : null;
        public static void SetSettingValue(string key, string value) { Values[key] = value; }
    }
}
class Program
{
    static int checks;
    static readonly string Browser = @"C:\Program Files\A B (x) #1\browser.exe";
    static readonly string Other = @"C:\Apps\other.exe";
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    static void Reject(Action action, string name)
    {
        try { action(); } catch (InvalidOperationException) { checks++; return; }
        throw new Exception(name);
    }
    static SplitTunnelSettings Settings(SplitTunnelMode mode) => new SplitTunnelSettings { Enabled = true, Mode = mode,
        Apps = new List<SplitTunnelApp> { new SplitTunnelApp { Path = Browser, Name = "Browser" } } };
    static string Base(bool tun = true)
    {
        var c = JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_clientSample);
        c["inbounds"] = new JArray(JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_mixedInbound));
        if (tun) ((JArray)c["inbounds"]).Add(JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_vpnInbound));
        var rules = (JArray)c["route"]["rules"];
        rules.Add(new JObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject" });
        rules.Add(JObject.Parse(IRSpeedyVPN.Services.SingBox.Samples.sg_vpnRouteRules));
        rules.Add(new JObject { ["domain_suffix"] = new JArray("ai.example"), ["outbound"] = "ai", ["action"] = "route" });
        c["dns"]["final"] = "dns-remote";
        return c.ToString();
    }
    // Evaluate a small subset independently to verify effective route precedence,
    // including logical AND/invert, unknown processes and non-TUN callers.
    static bool Match(JObject rule, string process, string inbound, string network, int port)
    {
        bool ok;
        if ((string)rule["type"] == "logical")
        {
            Check(rule["inbound"] == null, "logical rule has no default-rule-only fields");
            ok = ((JArray)rule["rules"]).OfType<JObject>().All(r => Match(r, process, inbound, network, port));
        }
        else
        {
            ok = rule["inbound"] == null || ((JArray)rule["inbound"]).Values<string>().Contains(inbound);
            if (rule["process_path_regex"] != null)
                ok &= process != null && ((JArray)rule["process_path_regex"]).Values<string>().Any(p => Regex.IsMatch(process, p));
            if (rule["process_name"] != null)
                ok &= process != null && ((JArray)rule["process_name"]).Values<string>().Contains(process.Split('\\').Last().ToLowerInvariant());
            if (rule["port"] != null) ok &= (int)rule["port"] == port;
            if (rule["network"] != null) ok &= (string)rule["network"] == network;
            if (rule["protocol"] != null) ok &= port == 53;
            if (rule["rule_set"] != null || rule["domain_suffix"] != null) ok = false;
        }
        return (bool?)rule["invert"] == true ? !ok : ok;
    }
    static string Route(JObject c, string process, string inbound = "tun-in", string network = "tcp", int port = 443)
    {
        foreach (var r in ((JArray)c["route"]["rules"]).OfType<JObject>())
        {
            if (!Match(r, process, inbound, network, port)) continue;
            string action = (string)r["action"];
            if (action == "sniff") continue;
            return (string)r["outbound"] ?? action;
        }
        return (string)c["route"]["final"];
    }
    static void Main(string[] args)
    {
        string original = Base();
        Check(SplitTunnelPolicyBuilder.Apply(original, new SplitTunnelSettings()) == original, "disabled byte-identical");
        Check(SplitTunnelPolicyBuilder.Apply(Base(false), Settings(SplitTunnelMode.ExcludeSelectedApps)) == Base(false), "proxy untouched");
        var exclude = JObject.Parse(SplitTunnelPolicyBuilder.Apply(original, Settings(SplitTunnelMode.ExcludeSelectedApps)));
        Check(Route(exclude, Browser) == "split-direct", "selected excluded");
        Check(Route(exclude, Browser, network: "udp") == "split-direct", "selected UDP bypasses VPN QUIC reject");
        Check(Route(exclude, Other, network: "udp") == "reject", "unselected retains QUIC policy");
        Check(Route(exclude, Other) == "proxy", "unselected uses VPN");
        Check(Route(exclude, Browser, "mixed-in") == "proxy", "shared proxy unaffected");
        Check(Route(exclude, Browser, port: 53) == "hijack-dns", "DNS capture precedes app bypass");
        var inc = JObject.Parse(SplitTunnelPolicyBuilder.Apply(original, Settings(SplitTunnelMode.IncludeOnlySelectedApps)));
        Check(Route(inc, Browser) == "proxy", "selected included");
        Check(Route(inc, Other) == "split-direct", "unselected direct");
        Check(Route(inc, Other, network: "udp") == "split-direct", "unselected UDP direct");
        Check(Route(inc, null) == "proxy", "unknown and forwarded hotspot stays VPN");
        Check(Route(inc, Other, "mixed-in") == "proxy", "mixed inlet remains VPN");
        Check(Route(inc, @"C:\Runtime\throne.exe") == "direct", "core loop protection");
        Check((bool)inc["route"]["auto_detect_interface"], "physical interface detection retained");
        Check(inc["route"]["rules"].Children().Any(r => r["domain_suffix"] != null), "AI rules preserved");
        Check(inc["route"]["rule_set"].Children().Count() == 2, "geo definitions preserved");
        var dns = (JObject)inc["dns"]["rules"][0];
        Check(Match(dns, Other, "tun-in", "udp", 53), "direct DNS for known unselected");
        Check(!Match(dns, null, "tun-in", "udp", 53), "dnscache without app identity stays on VPN DNS");
        Check(!Match(dns, Other, "mixed-in", "udp", 53), "shared proxy DNS unaffected");
        Check(inc["inbounds"].Children().First(i => (string)i["type"] == "tun")["address"].Values<string>().Any(a => a.Contains(":")), "IPv6 captured");
        var empty = Settings(SplitTunnelMode.ExcludeSelectedApps); empty.Apps.Clear();
        Check(Route(JObject.Parse(SplitTunnelPolicyBuilder.Apply(original, empty)), Other) == "proxy", "exclude empty VPN");
        empty.Mode = SplitTunnelMode.IncludeOnlySelectedApps;
        Check(Route(JObject.Parse(SplitTunnelPolicyBuilder.Apply(original, empty)), Other) == "split-direct", "include empty known apps direct");
        var game = JObject.Parse(original); game["route"]["final"] = "direct";
        Check(Route(JObject.Parse(SplitTunnelPolicyBuilder.Apply(game.ToString(), Settings(SplitTunnelMode.IncludeOnlySelectedApps))), Browser) == "proxy", "split takes precedence over game catch-all");
        var noGeo = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(original, null, System.IO.Path.GetTempPath());
        Check(Route(JObject.Parse(SplitTunnelPolicyBuilder.Apply(noGeo.SingBoxConfig, Settings(SplitTunnelMode.ExcludeSelectedApps))), Browser) == "split-direct", "missing geo does not discard app rules");

        string exact = AppPathPattern.ForApp(new SplitTunnelApp { Path = Browser });
        Check(Regex.IsMatch(Browser.ToUpperInvariant(), exact), "case-insensitive path");
        Check(!Regex.IsMatch(Browser + ".evil.exe", exact), "exact anchoring");
        Check(!exact.Contains(@"\ ") && !exact.Contains(@"\#"), "RE2 compatible spaces and hash");
        var folder = new SplitTunnelApp { Path = @"D:\Games\One", Root = @"D:\Games\One", Kind = AppMatchKind.Folder };
        Check(Regex.IsMatch(@"D:\Games\One\bin\game.exe", AppPathPattern.ForApp(folder)), "folder children");
        Check(!Regex.IsMatch(@"D:\Games\OneMore\game.exe", AppPathPattern.ForApp(folder)), "folder boundary");
        var version = new SplitTunnelApp { Path = @"C:\Apps\Discord\app-1.0\Discord.exe", Root = @"C:\Apps\Discord", ExeName = "Discord.exe", Kind = AppMatchKind.Versioned };
        Check(Regex.IsMatch(@"C:\Apps\Discord\app-99.1\Discord.exe", AppPathPattern.ForApp(version)), "update survives");
        Check(!Regex.IsMatch(@"C:\Apps\Discord\app-99.1\helper.exe", AppPathPattern.ForApp(version)), "version pattern bounded");
        foreach (var path in new[] { @"\\server\app.exe", "relative.exe", @"C:\Apps\..\evil.exe", @"C:\app.exe:stream", @"C:\*.exe" })
            Check(AppPathPattern.ForApp(new SplitTunnelApp { Path = path }) == null, "reject invalid path " + path);
        var invalid = Settings(SplitTunnelMode.ExcludeSelectedApps); invalid.Apps[0].Path = "bad";
        Reject(() => SplitTunnelPolicyBuilder.Apply(original, invalid), "invalid selection cannot silently disable policy");
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelPreviewApplications"] = JsonConvert.SerializeObject(new[] { Browser });
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelPreviewEnabled"] = "1";
        var migrated = SplitTunnelStore.Load();
        Check(!migrated.Enabled && migrated.Apps.Count == 1, "preview imports without silently enabling routing");
        SplitTunnelStore.Save(Settings(SplitTunnelMode.IncludeOnlySelectedApps));
        var loaded = SplitTunnelStore.Load();
        Check(loaded.Enabled && loaded.Mode == SplitTunnelMode.IncludeOnlySelectedApps && loaded.Apps[0].Path == Browser, "persistence round trip");
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelSettingsV1"] = "{\"Version\":999}";
        Reject(() => SplitTunnelStore.Load(), "unknown schema version");
        if (args.Length == 1)
        {
            System.IO.Directory.CreateDirectory(args[0]);
            var fixture = JObject.Parse(noGeo.SingBoxConfig);
            var outs = (JArray)fixture["outbounds"];
            // Production's extended core supports its legacy block outbound; the
            // stock 1.14 schema check uses equivalent modern fixture outbounds.
            outs.RemoveAll();
            outs.Add(new JObject { ["type"] = "socks", ["tag"] = "proxy", ["server"] = "127.0.0.1", ["server_port"] = 10990 });
            outs.Add(new JObject { ["type"] = "socks", ["tag"] = "ai", ["server"] = "127.0.0.1", ["server_port"] = 10991 });
            outs.Add(new JObject { ["type"] = "direct", ["tag"] = "direct" });
            foreach (SplitTunnelMode mode in Enum.GetValues(typeof(SplitTunnelMode)))
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[0], mode + ".json"), SplitTunnelPolicyBuilder.Apply(fixture.ToString(), Settings(mode)));
        }
        Console.WriteLine("PASS: " + checks + " split-tunnel path, routing, DNS, IPv6, migration and persistence checks.");
    }
}
