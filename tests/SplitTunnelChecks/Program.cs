using IRSpeedyVPN.Services.SplitTunneling;
using IRSpeedyVPN.Services.SingBox;
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
    static SplitTunnelSettings Settings() => new SplitTunnelSettings { Enabled = true,
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
        return c.ToString();
    }
    // Evaluate a small subset independently to verify effective route precedence,
    // including logical AND/invert, unknown processes and non-TUN callers.
    static bool Match(JObject rule, string process, string inbound, string network, int port, string domain = "www.youtube.com", string sourceIp = "127.0.0.1")
    {
        bool ok;
        if ((string)rule["type"] == "logical")
        {
            if (rule["inbound"] != null) throw new Exception("invalid logical rule schema");
            var matches = ((JArray)rule["rules"]).OfType<JObject>().Select(r => Match(r, process, inbound, network, port, domain, sourceIp));
            ok = (string)rule["mode"] == "or" ? matches.Any(v => v) : matches.All(v => v);
        }
        else
        {
            ok = rule["inbound"] == null || ((JArray)rule["inbound"]).Values<string>().Contains(inbound);
            if (rule["source_ip_cidr"] != null)
                ok &= ((JArray)rule["source_ip_cidr"]).Values<string>().Any(cidr => InSubnet(sourceIp, cidr));
            if (rule["process_path_regex"] != null)
                ok &= process != null && ((JArray)rule["process_path_regex"]).Values<string>().Any(p => Regex.IsMatch(process, p));
            if (rule["process_name"] != null)
                ok &= process != null && ((JArray)rule["process_name"]).Values<string>().Contains(process.Split('\\').Last().ToLowerInvariant());
            if (rule["port"] != null) ok &= (int)rule["port"] == port;
            if (rule["network"] != null) ok &= (string)rule["network"] == network;
            if (rule["protocol"] != null) ok &= port == 53;
            if (rule["domain"] != null)
                ok &= (rule["domain"] is JArray ? rule["domain"].Values<string>() : new[] { (string)rule["domain"] }).Contains(domain);
            if (rule["query_type"] != null) ok &= (string)rule["query_type"] == "A";
            if (rule["rule_set"] != null || rule["domain_suffix"] != null) ok = false;
        }
        return (bool?)rule["invert"] == true ? !ok : ok;
    }
    static bool InSubnet(string address, string cidr)
    {
        var parts = cidr.Split('/');
        var value = System.Net.IPAddress.Parse(address).GetAddressBytes();
        var prefix = System.Net.IPAddress.Parse(parts[0]).GetAddressBytes();
        if (value.Length != prefix.Length) return false;
        int bits = int.Parse(parts[1]);
        for (int i = 0; i < value.Length && bits > 0; i++, bits -= 8)
        {
            int mask = 255 << (8 - Math.Min(bits, 8)) & 255;
            if ((value[i] & mask) != (prefix[i] & mask)) return false;
        }
        return true;
    }
    static string Route(JObject c, string process, string inbound = "tun-in", string network = "tcp", int port = 443, string sourceIp = "127.0.0.1")
    {
        foreach (var r in ((JArray)c["route"]["rules"]).OfType<JObject>())
        {
            if (!Match(r, process, inbound, network, port, sourceIp: sourceIp)) continue;
            string action = (string)r["action"];
            if (action == "sniff") continue;
            return (string)r["outbound"] ?? action;
        }
        return (string)c["route"]["final"];
    }
    static JObject Build(string json, SplitTunnelSettings settings) =>
        JObject.Parse(SplitTunnelPolicyBuilder.Apply(TunBrowserCompatibility.Apply(json), settings));
    static string DnsRoute(JObject config, string process, string inbound = "tun-in", string domain = "www.youtube.com", string sourceIp = "127.0.0.1")
    {
        foreach (var rule in config["dns"]["rules"].OfType<JObject>())
            if (Match(rule, process, inbound, "udp", 53, domain, sourceIp))
                return (string)rule["server"] ?? (string)rule["action"];
        return (string)config["dns"]["final"] ?? (string)config["dns"]["servers"][0]["tag"];
    }
    static bool FastQuicReject(JObject config, string process)
    {
        bool sniffed = false;
        foreach (var r in config["route"]["rules"].OfType<JObject>())
        {
            if (!Match(r, process, "tun-in", "udp", 443)) continue;
            if ((string)r["action"] == "sniff") { sniffed = true; continue; }
            return (string)r["action"] == "reject" && !sniffed && (bool?)r["no_drop"] == true
                && (string)r["method"] == "default";
        }
        return false;
    }
    static void Main(string[] args)
    {
        string original = Base();
        Check(SplitTunnelPolicyBuilder.Apply(original, new SplitTunnelSettings()) == original, "split disabled byte-identical");
        Check(SplitTunnelPolicyBuilder.Apply(Base(false), new SplitTunnelSettings()) == Base(false), "disabled proxy byte-identical");
        Check(TunBrowserCompatibility.Apply(Base(false)) == Base(false), "browser fix does not change proxy-only mode");
        var normal = JObject.Parse(TunBrowserCompatibility.Apply(original));
        Check(!FastQuicReject(JObject.Parse(original), Browser), "reproduce old post-sniff QUIC rejection");
        Check(FastQuicReject(normal, Browser), "normal TUN rejects QUIC before sniff without dropping");
        Check(Route(normal, @"C:\Runtime\throne.exe", network: "udp") == "direct", "core UDP/443 not rejected");
        Check(JToken.DeepEquals(normal, JObject.Parse(TunBrowserCompatibility.Apply(normal.ToString()))), "browser transform idempotent");
        var inc = Build(original, Settings());
        Check(Route(inc, Browser) == "proxy", "selected browser included");
        Check(Route(inc, Other) == "split-direct", "unselected direct");
        Check(Route(inc, Other, network: "udp") == "split-direct", "unselected QUIC direct");
        Check(FastQuicReject(inc, Browser), "selected browser HTTP3 falls back before sniff");
        Check(Route(inc, null) == "proxy", "unknown and forwarded hotspot stays VPN");
        Check(Route(inc, Other, "mixed-in") == "split-direct", "local proxy honors app choices alongside TUN");
        Check(Route(inc, Other, "mixed-in", "udp") == "split-direct", "local SOCKS UDP honors app choices");
        Check(Route(inc, @"C:\Runtime\throne.exe") == "direct", "core loop protection");
        Check(Route(inc, Other, port: 53) == "hijack-dns", "DNS capture precedes app bypass");
        Check((bool)inc["route"]["auto_detect_interface"], "physical interface detection retained");
        Check(inc["route"]["rules"].Children().Any(r => r["domain_suffix"] != null), "AI rules preserved");
        Check(inc["route"]["rule_set"].Children().Count() == 2, "geo definitions preserved");
        foreach (var domain in new[] { "www.youtube.com", "i.ytimg.com", "r1.googlevideo.com", "api.ipify.org" })
        {
            Check(DnsRoute(inc, Browser, domain: domain) == "dns-remote", "selected browser DNS VPN: " + domain);
            Check(DnsRoute(inc, Other, domain: domain) == "split-dns-direct", "unselected DNS direct: " + domain);
            Check(DnsRoute(inc, @"C:\Windows\System32\svchost.exe", domain: domain) == "dns-remote", "Windows shared DNS VPN: " + domain);
        }
        Check(Route(inc, @"C:\Windows\System32\svchost.exe") == "split-direct", "only DNS service queries are special, not all svchost traffic");
        Check(DnsRoute(inc, null) == "dns-remote", "unattributed DNS keeps VPN resolver");
        Check(DnsRoute(inc, Other, "mixed-in", sourceIp: "192.168.137.2") == "dns-remote", "shared proxy DNS unaffected");
        Check(DnsRoute(inc, Other, domain: "localhost") == "predefined", "localhost before process DNS rules");
        Check((bool)inc["dns"]["rules"].First(r => (string)r["server"] == "split-dns-direct")["disable_cache"], "direct DNS cannot pollute VPN cache");
        Check(inc["inbounds"].Children().First(i => (string)i["type"] == "tun")["address"].Values<string>().Any(a => a.Contains(":")), "IPv6 captured");
        var empty = Settings(); empty.Apps.Clear();
        Check(Route(Build(original, empty), Other) == "split-direct", "empty list: known apps direct");
        var game = JObject.Parse(original); game["route"]["final"] = "direct";
        Check(Route(Build(game.ToString(), Settings()), Browser) == "proxy", "split takes precedence over game catch-all");
        var noGeo = IRSpeedyVPN.Services.GeoRoutingFallback.Apply(original, null, System.IO.Path.GetTempPath());
        Check(Route(Build(noGeo.SingBoxConfig, Settings()), Browser) == "proxy", "missing geo preserves selected VPN");
        Check(Route(Build(noGeo.SingBoxConfig, Settings()), Other) == "split-direct", "missing geo preserves unselected direct");

        const string clientPath = @"C:\Program Files\IRSPEEDY\Client.exe";
        foreach (var type in new[] { "mixed", "http", "socks" })
        {
            var input = JObject.Parse(Base(false));
            input["inbounds"][0]["type"] = type;
            // The proxy-only generator does not emit the TUN QUIC policy.
            foreach (var rule in ((JArray)input["route"]["rules"]).OfType<JObject>().Where(r => (string)r["action"] == "reject").ToArray()) rule.Remove();
            var proxy = JObject.Parse(SplitTunnelPolicyBuilder.Apply(input.ToString(), Settings(), clientPath));
            Check(Route(proxy, Browser, "mixed-in") == "proxy", type + " selected VPN");
            Check(Route(proxy, Other, "mixed-in") == "split-direct", type + " unselected direct");
            Check(Route(proxy, Other, "mixed-in", sourceIp: "::1") == "split-direct", type + " IPv6 loopback");
            Check(Route(proxy, Browser, "mixed-in", sourceIp: "::1") == "proxy", type + " selected IPv6 loopback");
            Check(Route(proxy, Other, "mixed-in", sourceIp: "192.168.137.2") == "proxy", type + " remote sharing isolated");
            Check(Route(proxy, Other, "mixed-in", sourceIp: "fd00::2") == "proxy", type + " remote IPv6 isolated");
            Check(Route(proxy, null, "mixed-in") == "proxy", type + " unknown owner preserves VPN policy");
            Check(Route(proxy, clientPath, "mixed-in") == "proxy", type + " client egress probe stays VPN");
            Check(Route(proxy, clientPath + ".other.exe", "mixed-in") == "split-direct", type + " probe exemption is exact");
            Check(DnsRoute(proxy, Other, "mixed-in") == "split-dns-direct", type + " direct resolver");
            Check(DnsRoute(proxy, Browser, "mixed-in") == "dns-remote", type + " VPN resolver");
            Check(!proxy["inbounds"].Any(i => (string)i["type"] == "tun"), type + " does not force TUN");
            Check(Route(JObject.Parse(SplitTunnelPolicyBuilder.Apply(input.ToString(), empty)), Other, "mixed-in") == "split-direct", type + " empty selection stays direct");
        }

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
        var invalid = Settings(); invalid.Apps[0].Path = "bad";
        Reject(() => SplitTunnelPolicyBuilder.Apply(original, invalid), "invalid selection cannot silently disable policy");
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelPreviewApplications"] = JsonConvert.SerializeObject(new[] { Browser });
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelPreviewEnabled"] = "1";
        var migrated = SplitTunnelStore.Load();
        Check(!migrated.Enabled && migrated.Apps.Count == 1, "preview imports without silently enabling routing");
        SplitTunnelStore.Save(Settings());
        var loaded = SplitTunnelStore.Load();
        Check(loaded.Enabled && loaded.Version == 2 && loaded.Apps[0].Path == Browser, "persistence round trip");
        Check(!JObject.Parse(IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelSettingsV1"]).ContainsKey("Mode"), "new settings contain no mode");
        foreach (int oldMode in new[] { 0, 1 })
        {
            var old = JObject.FromObject(Settings()); old["Version"] = 1; old["Mode"] = oldMode;
            IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelSettingsV1"] = old.ToString();
            var upgraded = SplitTunnelStore.Load();
            Check(upgraded.Version == 2 && upgraded.Enabled && upgraded.Apps[0].Path == Browser, "migrate old mode list intact: " + oldMode);
            Check(Route(Build(original, upgraded), Browser) == "proxy", "old mode now selected-only: " + oldMode);
            Check(Route(Build(original, upgraded), Other) == "split-direct", "old mode now unselected direct: " + oldMode);
        }
        var manual = new SplitTunnelApp { Name = "Manual.exe", Path = @"C:\Apps\Manual.exe", Source = "Manual" };
        var catalog = Settings(); catalog.CustomApps.Add(manual);
        SplitTunnelStore.Save(catalog);
        var catalogLoaded = SplitTunnelStore.Load();
        Check(catalogLoaded.CustomApps.Count == 1 && catalogLoaded.Apps.Count == 1, "unchecked manual entry survives reopen");
        Check(Route(Build(original, catalogLoaded), manual.Path) == "split-direct", "manual catalog is not a routing selection");
        catalogLoaded.CustomApps.Clear(); SplitTunnelStore.Save(catalogLoaded);
        Check(SplitTunnelStore.Load().CustomApps.Count == 0, "manual deletion persists");
        var legacyManual = JObject.FromObject(Settings());
        legacyManual.Remove("CustomApps"); ((JArray)legacyManual["Apps"]).Add(JObject.FromObject(manual));
        IRSpeedyVPN.Resource.RegHelper.Values["SplitTunnelSettingsV1"] = legacyManual.ToString();
        Check(SplitTunnelStore.Load().CustomApps.Count == 1, "old selected manual entries migrate to catalog");
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
            System.IO.File.WriteAllText(System.IO.Path.Combine(args[0], "SelectedOnly.json"), Build(fixture.ToString(), Settings()).ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(args[0], "NormalTun.json"), TunBrowserCompatibility.Apply(fixture.ToString()));
            ((JArray)fixture["inbounds"]).RemoveAt(1);
            foreach (var rule in ((JArray)fixture["route"]["rules"]).OfType<JObject>().Where(r => (string)r["action"] == "reject").ToArray()) rule.Remove();
            System.IO.File.WriteAllText(System.IO.Path.Combine(args[0], "ProxySelectedOnly.json"), SplitTunnelPolicyBuilder.Apply(fixture.ToString(), Settings(), clientPath));
        }
        Console.WriteLine("PASS: " + checks + " split-tunnel path, routing, DNS, IPv6, migration and persistence checks.");
    }
}
