using IRSpeedyVPN.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using System.Web.Routing;
using System.Web.Script.Serialization;
using System.Windows.Interop;
using v2rayN;
using v2rayN.Base;
using v2rayN.Handler;
using v2rayN.Mode;
using static System.Windows.Forms.LinkLabel;

namespace IRSpeedyVPN.Services.SingBox
{
    public class ConfigGenerator
    {
        public static CoreType Core
        {
            get
            {
                return Environment.Is64BitOperatingSystem ? CoreType.NekoBox : CoreType.SingBox;
            }
        }
        public static string GetConfig(string Link, int port, bool VpnMode, bool isShareActive, string[] shieldFiles, string chainLink, string[] defaultChainLink, string vodLink, bool hasDefaultchain, string overrideServer = null, int? overrideServerPort = null, string[] excludeprocesspath = null)
        {
            var result = GetConfigEx(Link, port, VpnMode, false, "", false, isShareActive, shieldFiles, chainLink, defaultChainLink, vodLink,hasDefaultchain, overrideServer, overrideServerPort, excludeprocesspath);
            return result;
        }

        public static string GetConfigEx(string Link, int port, bool VpnMode, bool addExtraInbounds, string chain, bool legacyDNS, bool isShareActive, string[] shieldFiles, string chainLink, string[] defaultChainLink, string vodLink, bool hasDefaultchain, string overrideServer = null, int? overrideServerPort = null, string[] excludeprocesspath = null)
        {
            SingBoxConfig cfg;
            if (Link.StartsWith("local://"))
            {
                cfg = GenerateConfig(null, port, true, addExtraInbounds, chain, legacyDNS, isShareActive, shieldFiles, chainLink, defaultChainLink, vodLink, hasDefaultchain, overrideServer, overrideServerPort);
                Random random = new Random(Environment.TickCount);
                if (cfg.inbounds != null && cfg.inbounds.Count > 0)
                    cfg.inbounds[0].interface_name = $"irspeedy-tun-{random.Next(4095).ToString("X")}";
            }
            else
            {
                string msg;
                var item = ShareHandler.ImportFromConfigLink(Link, out msg);
                cfg = GenerateConfig(item, port, VpnMode, addExtraInbounds, chain, legacyDNS, isShareActive, shieldFiles, chainLink, defaultChainLink, vodLink, hasDefaultchain, overrideServer, overrideServerPort,excludeprocesspath);
            }

            string res = Utils.ToJson(cfg);
            return res;
        }

        public static string GetUrlTestConfig(Dictionary<string, string[]> links, int port, out Dictionary<string, string> tagToUrl, Dictionary<string, EndpointOverride> endpointOverrides = null, Dictionary<string, Tuple<int, string, string>> socksOverrides = null)
        {
            if (links == null)
            {
                throw new ArgumentNullException(nameof(links));
            }

            tagToUrl = new Dictionary<string, string>();
            var cfg = Utils.FromJson<SingBoxConfig>(Samples.sg_UrlTest);
            if (cfg.outbounds == null)
            {
                cfg.outbounds = new List<Outbound>();
            }

            int idx = 0;
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link.Key))
                {
                    continue;
                }

                Tuple<int, string, string> socks = null;
                bool isSocks = socksOverrides != null && socksOverrides.TryGetValue(link.Key, out socks);

                var tag = idx == 0 ? "proxy" : $"proxy-{idx}";

                if (!isSocks)
                {
                    string msg;
                    var item = ShareHandler.ImportFromConfigLink(link.Key, out msg);
                    if (item == null)
                    {
                        continue;
                    }

                    var outbound = new Outbound();
                    FillOutboundForItem(outbound, item);
                    ApplyEndpointOverride(outbound, endpointOverrides != null && endpointOverrides.TryGetValue(link.Key, out var endpointOverride) ? endpointOverride : null);
                    outbound.tag = tag;

                    if (link.Value != null)
                    {
                        string detour;
                        var chainOutbounds = CreateChainOutbound(link.Value, $"{idx}", out detour, out msg);
                        if (chainOutbounds.Count() > 0)
                        {
                            cfg.outbounds.AddRange(chainOutbounds);
                            outbound.detour = detour;
                        }
                    }

                    cfg.outbounds.Add(outbound);
                    tagToUrl[tag] = link.Key;
                }
                else
                {
                    var outbound = CreateXraySocksOutbound(tag, socks.Item1, socks.Item2, socks.Item3);
                    cfg.outbounds.Add(outbound);
                    tagToUrl[tag] = link.Key;
                }
                idx++;
            }

            return Utils.ToJson(cfg);
        }

        private static void ApplyEndpointOverride(Outbound outbound, EndpointOverride endpointOverride)
        {
            if (outbound == null || endpointOverride == null)
                return;

            if (!string.IsNullOrWhiteSpace(endpointOverride.Server))
                outbound.server = endpointOverride.Server;

            if (endpointOverride.ServerPort > 0)
                outbound.server_port = endpointOverride.ServerPort;
        }
        private static List<Outbound> CreateChainOutbound(string[] chainLinks,string proxTag,out string detour,out string msg)
        {
            List<Outbound> outbounds = new List<Outbound>();
            int idy = 0;
            detour = null;
            msg = null;
            foreach (string lnk in chainLinks)
            {
                if (string.IsNullOrWhiteSpace(lnk))
                {
                    continue;
                }
                idy++;
                var item = ShareHandler.ImportFromConfigLink(lnk, out msg);
                var outboundchain = new Outbound();
                FillOutboundForItem(outboundchain, item);
                outboundchain.detour = detour;
                detour = outboundchain.tag = $"chain-{proxTag}-{idy}";
                outbounds.Add(outboundchain);
                
            }            
            return outbounds;

        }
        private static Outbound CloneOutbound(Outbound source)
        {
            if (source == null)
            {
                return new Outbound();
            }
            var json = Utils.ToJson(source);
            return Utils.FromJson<Outbound>(json);
        }

        private static void FillOutboundForItem(Outbound outbound, VmessItem node)
        {
            if (outbound == null || node == null)
            {
                return;
            }

            try
            {
                if (node.configType == EConfigType.VMess)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.uuid = node.id;
                    outbound.alter_id = node.alterId;
                    outbound.security = Global.vmessSecuritys.Contains(node.security) ? node.security : Global.DefaultSecurity;
                    outbound.multiplex = null;
                    boundStreamSettings(node, "out", outbound);
                    outbound.type = Global.vmessProtocolLite;
                }
                else if (node.configType == EConfigType.Shadowsocks)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.password = node.id;
                    outbound.method = LazyConfig.Instance.GetShadowsocksSecuritys(node).Contains(node.security) ? node.security : "none";
                    outbound.multiplex = null;
                    boundStreamSettings(node, "out", outbound);
                    outbound.type = Global.ssProtocolLite;
                }
                else if (node.configType == EConfigType.Shadowsocksr)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.password = node.id;
                    outbound.method = node.security;
                    outbound.security = null;
                    outbound.obfs = node.obfs;
                    outbound.obfs_param = node.obfs_param;
                    outbound.protocol = node.protocol;
                    outbound.protocol_param = node.protocol_param;
                    outbound.tls = null;
                    outbound.multiplex = null;
                    boundStreamSettings(node, "out", outbound);
                    outbound.type = Global.ssrProtocolLite;
                }
                else if (node.configType == EConfigType.Socks)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.method = null;
                    if (!Utils.IsNullOrEmpty(node.security) && !Utils.IsNullOrEmpty(node.id))
                    {
                        outbound.password = node.id;
                        outbound.username = node.security;
                    }
                    outbound.multiplex = null;
                                        outbound.type = Global.socksProtocolLite;
                }
                else if (node.configType == EConfigType.Http)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.method = null;
                    outbound.username = null;
                    outbound.password = null;
                    if (!Utils.IsNullOrEmpty(node.security))
                    {
                        outbound.username = node.security;
                        outbound.password = node.id;
                    }
                    outbound.multiplex = null;
                    outbound.type = "http";
                }
                else if (node.configType == EConfigType.VLESS)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.uuid = node.id;
                    outbound.alter_id = null;
                    outbound.security = null;
                    if (!Utils.IsNullOrEmpty(node.packetEncoding))
                        outbound.packet_encoding = node.packetEncoding;
                    else if (string.IsNullOrEmpty(outbound.packet_encoding))
                        outbound.packet_encoding = "";
                    if (!Utils.IsNullOrEmpty(node.flow))
                        outbound.flow = node.flow.Replace("splice", "direct");
                    boundStreamSettings(node, "out", outbound);
                    outbound.multiplex = null;
                    outbound.type = Global.vlessProtocolLite;
                }
                else if (node.configType == EConfigType.Trojan)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.password = node.id;
                    outbound.security = null;
                    boundStreamSettings(node, "out", outbound);
                    outbound.type = Global.trojanProtocolLite;
                }
                else if (node.configType == EConfigType.WireGaurd)
                {
                    var serilizer = new JavaScriptSerializer();
                    var wgOutbound = serilizer.Deserialize<Outbound>(Samples.sg_wgOutbound);
                    outbound.server = wgOutbound.server;
                    outbound.server_port = wgOutbound.server_port;
                    outbound.type = wgOutbound.type;
                    outbound.tag = wgOutbound.tag;
                    outbound.private_key = node.privateKey;
                    outbound.peer_public_key = node.publicKey;
                    outbound.pre_shared_key = node.preSharedKey;
                    if (node.ipList != null)
                        outbound.local_address = node.ipList.ToList();
                    outbound.tls = null;
                    outbound.multiplex = null;
                    outbound.type = Global.wireguardProtocolLite;
                }
                else if (node.configType == EConfigType.SSH)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.user = node.username;
                    outbound.password = node.password;
                    outbound.client_version = "SSH-2.0-OpenSSH_7.4p1";
                    outbound.tls = null;
                    outbound.multiplex = null;
                    outbound.security = null;
                    outbound.type = Global.sshProtocolLite;
                }
                else if (node.configType == EConfigType.Hysteria2)
                {
                    outbound.server = node.address;
                    if (node.portEnd > node.port)
                    {
                        outbound.server_port = null;
                        outbound.server_ports = new List<string> { $"{node.port}:{node.portEnd}" };
                    }
                    else
                    {
                        outbound.server_port = node.port > 0 ? node.port : 443;
                        outbound.server_ports = null;
                    }
                    outbound.password = node.password;
                    outbound.type = "hysteria2";
                    outbound.multiplex = null;

                    if (!string.IsNullOrWhiteSpace(node.obfs_param))
                    {
                        outbound.obfs = new HysteriaObfs
                        {
                            password = node.obfs_param,
                            type = node.obfs
                        };
                    }

                    var tlsEnabled = string.Equals(node.streamSecurity, Global.StreamSecurity, StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(node.sni)
                        || !string.IsNullOrWhiteSpace(node.allowInsecure);
                    if (tlsEnabled)
                    {
                        outbound.tls = new Tls
                        {
                            enabled = true,
                            insecure = string.IsNullOrEmpty(node.allowInsecure) ? (bool?)null : Utils.ToBool(node.allowInsecure),
                            server_name = string.IsNullOrWhiteSpace(node.sni) ? null : node.sni
                        };
                    }
                }
                else if (node.configType == EConfigType.Tuic)
                {
                    outbound.server = node.address;
                    outbound.server_port = node.port;
                    outbound.uuid = node.id;
                    outbound.password = node.password;
                    outbound.type = "tuic";
                    outbound.multiplex = null;
                    outbound.congestion_control = node.congestionControl;
                    outbound.udp_relay_mode = node.udpRelayMode;

                    outbound.tls = new Tls
                    {
                        enabled = true,
                        insecure = string.IsNullOrEmpty(node.allowInsecure) ? (bool?)null : Utils.ToBool(node.allowInsecure),
                        server_name = string.IsNullOrWhiteSpace(node.sni) ? null : node.sni,
                        alpn = node.GetAlpn()
                    };
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
        }
       

        private static SingBoxConfig GenerateConfig(VmessItem item, int port, bool vpnmode, bool addExtraInbounds, string chain, bool legacyDNS, bool isShareActive, string[] shieldFiles, string chainLink, string[] defaultChainLink, string vodLink, bool hasDefaultchain, string overrideServer = null, int? overrideServerPort = null,string[] excludeprocesspath=null)
        {
            var serilizer = new JavaScriptSerializer();
            SingBoxConfig cfg = null;
            if (item != null)
            {
                cfg = serilizer.Deserialize<SingBoxConfig>(Samples.sg_clientSample);
                //cfg.dns.rules.Clear();
                FillDns(cfg, item, vpnmode,excludeprocesspath);
                FillInbound(cfg, item, port, vpnmode,isShareActive);
                Filloutbound(cfg, item, overrideServer, overrideServerPort);
                ApplyChainOutbound(cfg, chainLink, defaultChainLink);
                ApplyAiOutbound(cfg, hasDefaultchain);
                ApplyVodOutbound(cfg, vodLink, hasDefaultchain);
                FillRoute(cfg, item, vpnmode, shieldFiles, hasDefaultchain,excludeprocesspath);
                FillLog(cfg, "vgaurd.txt");
            }
            else
            {

                cfg = serilizer.Deserialize<SingBoxConfig>(Samples.sg_localvpn);
                FillLog(cfg,"vgaurdv.txt" );
            }
            return cfg;

        }

        private static void FillLog(SingBoxConfig cfg, string logname)
        {
            if (File.Exists(".\\slog.txt"))
            {
                cfg.log.disabled = false;
                cfg.log.output= logname;
                cfg.log.timestamp = true;

            }
        }

        private static void FillRoute(SingBoxConfig cfg, VmessItem item, bool vpnmode, string[] shieldFiles, bool hasDefaultChain, string[] excludeprocesspath = null)
        {
            if (vpnmode)
            {
                cfg.route.find_process = true;
                cfg.route.rules.Add(Utils.FromJson<Rule>(Samples.sg_vpnRouteRules));
                
                if (excludeprocesspath != null && excludeprocesspath.Length > 0)
                {
                    foreach (string p in excludeprocesspath)
                    {
                        if (p != null)
                        {
                            var exRule = Utils.FromJson<Rule>(Samples.sg_ExcludeRouteRules);
                            exRule.process_path = p;
                            cfg.route.rules.Add(exRule);
                        }
                    }

                }
                
               
            }

            if (hasDefaultChain)
            {
                foreach (var rule in cfg.route.rules.Where(x => x.outbound == "direct"))
                {
                    rule.outbound = "chain-default-1";
                }
            }
            ApplySpeedyShield(cfg, shieldFiles);


        }

        private static void ApplySpeedyShield(SingBoxConfig cfg, string[] shieldFiles)
        {
            if (cfg?.route == null || shieldFiles == null || shieldFiles.Length == 0)
                return;

            var files = shieldFiles
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                return;

            bool hasGambling = files.Any(x => string.Equals(x, "gambling", StringComparison.OrdinalIgnoreCase));
            var ruleSetTags = new List<string>();

            if (cfg.route.rule_set == null)
                cfg.route.rule_set = new List<RuleSet>();

            foreach (var file in files)
            {
                if (string.Equals(file, "gambling", StringComparison.OrdinalIgnoreCase))
                    continue;

                var tag = file.EndsWith(".srs", StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(0, file.Length - 4)
                    : file;

                ruleSetTags.Add(tag);
                if (!cfg.route.rule_set.Any(r => string.Equals(r.tag, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    cfg.route.rule_set.Add(new RuleSet
                    {
                        format = "binary",
                        path = $"geo/{file}",
                        tag = tag,
                        type = "local"
                    });
                }
            }

            if (hasGambling)
            {
                EnsureGamblingRules(cfg);
            }

            if (ruleSetTags.Count > 0)
            {
                var rule = cfg.route.rules?.FirstOrDefault(r =>
                    string.Equals(r.outbound, "block", StringComparison.OrdinalIgnoreCase) &&
                    r.rule_set != null);

                if (rule == null)
                {
                    rule = new Rule
                    {
                        outbound = "block",
                        action = "route",
                        rule_set = new List<string>()
                    };
                    cfg.route.rules.Add(rule);
                }

                rule.rule_set = ruleSetTags;
            }
        }

        private static void EnsureGamblingRules(SingBoxConfig cfg)
        {
            if (cfg?.route?.rules == null)
                return;

            var domainRule = cfg.route.rules.FirstOrDefault(r =>
                string.Equals(r.outbound, "block", StringComparison.OrdinalIgnoreCase) &&
                r.domain != null);
            if (domainRule == null)
            {
                domainRule = new Rule { outbound = "block", domain = new List<string>() };
                cfg.route.rules.Add(domainRule);
            }
            domainRule.domain = Samples.sg_gamblingDomains.ToList();

            var suffixRule = cfg.route.rules.FirstOrDefault(r =>
                string.Equals(r.outbound, "block", StringComparison.OrdinalIgnoreCase) &&
                r.domain_suffix != null);
            if (suffixRule == null)
            {
                suffixRule = new Rule { outbound = "block", domain_suffix = new List<object>() };
                cfg.route.rules.Add(suffixRule);
            }
            suffixRule.domain_suffix = Samples.sg_gamblingDomainSuffix.ToList<object>();
        }

        private static void ApplyChainOutbound(SingBoxConfig cfg, string chainLink, string[] defaultChainLink)
        {



            if (cfg == null || cfg.outbounds == null)
                return;
            if (defaultChainLink != null)
                defaultChainLink = defaultChainLink.Where(s => !string.IsNullOrEmpty(s)).ToArray();

            
            List<string> lstChains = new List<string>();
            
            try
            {
                if (defaultChainLink != null)
                    lstChains.AddRange(defaultChainLink);
                if (!string.IsNullOrEmpty(chainLink))
                {
                    if (defaultChainLink == null || defaultChainLink.Length == 0 || !string.Equals(chainLink.Trim(), defaultChainLink[0].Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        lstChains.Add(chainLink.Trim());
                    }
                }
               
                string detourTag = null;
                string msg;
                var chainoutbounds = CreateChainOutbound(lstChains.ToArray(), "default", out detourTag, out msg);
                cfg.outbounds.AddRange(chainoutbounds);
                if (detourTag != null && cfg.outbounds.Count > 0 && cfg.outbounds[0] != null)
                    cfg.outbounds[0].detour = detourTag;
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
        }

        private static Outbound BuildChainOutbound(string chainLink, string tag)
        {
            if (string.IsNullOrWhiteSpace(chainLink))
                return null;

            string msg;
            var chainItem = ShareHandler.ImportFromConfigLink(chainLink, out msg);
            if (chainItem == null)
                return null;

            var chainOutbound = new Outbound();
            FillOutboundForItem(chainOutbound, chainItem);
            chainOutbound.tag = tag;
            if (chainOutbound.tls != null)
                chainOutbound.tls.insecure = false;

            return chainOutbound;
        }

        /// <summary>
        /// Routes AI traffic through the hard-coded SMART IP links. A sing-box urltest
        /// outbound keeps picking the lowest-latency link, mirroring the leastLoad
        /// balancer the Xray smart-fast path uses. Shares the VOD/AI settings toggle.
        /// </summary>
        private static void ApplyAiOutbound(SingBoxConfig cfg, bool hasDefaultChain)
        {
            if (cfg == null || cfg.outbounds == null || cfg.route?.rules == null)
                return;
            if (!Xray.SmartIpRouting.IsEnabled())
                return;

            try
            {
                var tags = new List<string>();
                foreach (var link in Xray.SmartIpRouting.AiLinks)
                {
                    var tag = $"ai-proxy-{tags.Count + 1}";

                    string msg;
                    var item = ShareHandler.ImportFromConfigLink(link, out msg);
                    if (item == null)
                    {
                        LogHelper.WriteExLog($"SMART IP AI outbound rejected: {tag}");
                        continue;
                    }

                    var aiOutbound = new Outbound();
                    FillOutboundForItem(aiOutbound, item);
                    if (hasDefaultChain)
                        aiOutbound.detour = "chain-default-1";
                    aiOutbound.tag = tag;

                    cfg.outbounds.Add(aiOutbound);
                    tags.Add(tag);
                }

                LogHelper.WriteExLog(
                    $"SMART IP AI routing: requested={Xray.SmartIpRouting.AiLinks.Length}"
                    + $", accepted={tags.Count}"
                    + $", mode={(tags.Count > 0 ? "urltest" : "disabled")}");

                if (tags.Count == 0)
                    return;

                cfg.outbounds.Add(new Outbound
                {
                    tag = "ai",
                    type = "urltest",
                    outbounds = tags,
                    url = Xray.SmartIpRouting.AiTestUrl,
                    interval = Xray.SmartIpRouting.ObserverInterval
                });

                var domains = Xray.SmartIpRouting.AiDomains;
                cfg.route.rules.Insert(cfg.route.rules.Count() - 1, new Rule
                {
                    action = "route",
                    outbound = "ai",
                    domain = domains.ToList()
                });
                cfg.route.rules.Insert(cfg.route.rules.Count() - 1, new Rule
                {
                    action = "route",
                    outbound = "ai",
                    domain_suffix = domains.Select(x => (object)("." + x)).ToList()
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        private static void ApplyVodOutbound(SingBoxConfig cfg, string vodLink,bool hasDefualtChain)
        {
            if (cfg == null || cfg.outbounds == null || cfg.route?.rules == null)
                return;
            if (string.IsNullOrWhiteSpace(vodLink))
                return;

            try
            {
                string msg;
                var vodItem = ShareHandler.ImportFromConfigLink(vodLink, out msg);
                if (vodItem == null)
                    return;

                var vodOutbound = new Outbound();
                FillOutboundForItem(vodOutbound, vodItem);
                if (hasDefualtChain)
                    vodOutbound.detour = "chain-default-1";
                vodOutbound.tag = "irancell";
                if (vodOutbound.tls != null)
                    vodOutbound.tls.insecure = false;

                cfg.outbounds.Add(vodOutbound);

                cfg.route.rules.Insert(cfg.route.rules.Count() - 1,new Rule
                {
                    action = "route",
                    outbound = "irancell",
                    domain = Samples.sg_vodDomains.ToList()
                });
                cfg.route.rules.Insert(cfg.route.rules.Count() - 1, new Rule
                {
                    action = "route",
                    outbound = "irancell",
                    domain_suffix = Samples.sg_vodDomainSuffix.ToList<object>()
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        private static void FillDns(SingBoxConfig cfg, VmessItem item, bool vpnmode, string[] excludeprocesspath)
        {
           /*
                DnsServer dnss = Utils.FromJson<DnsServer>(Samples.sg_vpnDnsServer);
                cfg.dns.rules.Clear();
                cfg.dns.servers.Clear();
                cfg.dns.servers.Add(dnss);
            */
            IPAddress ip;
            if (!IPAddress.TryParse(item.address, out ip))
                cfg.dns.rules.Add(new Rule()
                {
                    domain = (new string[] { item.address }).ToList(),
                    server = "dns-direct",
                    action= "route",
                    strategy = "prefer_ipv4"

                });
            if (!string.IsNullOrWhiteSpace(item.hostOverride))
            {
                var hosts=item.hostOverride.Split(',');
                foreach (var host in hosts)
                {
                    if (!string.IsNullOrEmpty(host))
                        if (!IPAddress.TryParse(item.hostOverride, out ip))
                            cfg.dns.rules.Add(new Rule()
                            {
                                domain = (new string[] { host }).ToList(),
                                server = "dns-direct",
                                action = "route",
                                strategy = "prefer_ipv4"

                            });
                }
            }
            if (Core == CoreType.SingBox)
                cfg.dns.servers.ForEach(x =>
                {
                   // if (x.tag != "dns-remote")
                      //  x.address = "local";
                });
            if (excludeprocesspath != null && excludeprocesspath.Length > 0)
            {
                var exRule = Utils.FromJson<Rule>(Samples.sg_ExcludeDNSRules);                                   
                    exRule.process_path = excludeprocesspath;                                   
                cfg.dns.rules.Add(exRule);

            }
        }
        

        private static void Filloutbound(SingBoxConfig cfg, VmessItem node, string overrideServer = null, int? overrideServerPort = null)
        {
            try
            {
                if (cfg?.outbounds == null || cfg.outbounds.Count == 0 || node == null)
                    return;
              

                FillOutboundForItem(cfg.outbounds[0], node);
                ApplyEndpointOverride(cfg.outbounds[0], new EndpointOverride
                {
                    Server = overrideServer,
                    ServerPort = overrideServerPort ?? 0
                });
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }

        }

        public class EndpointOverride
        {
            public string Server { get; set; }
            public int ServerPort { get; set; }
        }
        private static int boundStreamSettings(VmessItem node, string iobound, Outbound outbound)
        {
            try
            {
                var config = LazyConfig.Instance.GetConfig();

                //outbound.network = node.GetNetwork();                
                string host = node.requestHost.TrimEx();
                string sni = node.sni;

                //if tls
                if (node.streamSecurity == Global.StreamSecurity || node.streamSecurity == Global.RealitySecurity)
                {
                    // outbound.security = node.streamSecurity;

                    Tls tlsSettings = new Tls
                    {
                        enabled = true,
                        insecure = string.IsNullOrEmpty(node.allowInsecure) ? (bool?)null : Utils.ToBool(node.allowInsecure),
                        alpn = node.GetAlpn()
                    };
                    if (!string.IsNullOrWhiteSpace(sni))
                    {
                        tlsSettings.server_name = sni;
                    }
                    else if (!string.IsNullOrWhiteSpace(host))
                    {
                        tlsSettings.server_name = Utils.String2List(host)[0];
                    }
                    if(node.streamSecurity==Global.RealitySecurity)
                    {
                        tlsSettings.reality = new Reality()
                        {
                            enabled = true,
                            public_key = node.publicKey,
                            short_id = node.shortId
                        };
                    }
                    if(!string.IsNullOrEmpty(node.fingerPrint))
                    {
                        tlsSettings.utls = new Utls()
                        {
                            enabled = true,
                            fingerprint = node.fingerPrint
                        };

                    }
                    outbound.tls = tlsSettings;
                }
                /*

                //if xtls
                if (node.streamSecurity == Global.StreamSecurityX)
                {
                    outbound.security = node.streamSecurity;

                    TlsSettings xtlsSettings = new TlsSettings
                    {
                        allowInsecure = Utils.ToBool(node.allowInsecure),
                        alpn = node.GetAlpn()
                    };
                    if (!string.IsNullOrWhiteSpace(sni))
                    {
                        xtlsSettings.serverName = sni;
                    }
                    else if (!string.IsNullOrWhiteSpace(host))
                    {
                        xtlsSettings.serverName = Utils.String2List(host)[0];
                    }
                    outbound.xtlsSettings = xtlsSettings;
                }*/

                //streamSettings
                switch (node.GetNetwork())
                {
                    /*
                    //kcp基本配置暂时是默认值，用户能自己设置伪装类型
                    case "kcp":
                        KcpSettings kcpSettings = new KcpSettings
                        {
                            mtu = config.kcpItem.mtu,
                            tti = config.kcpItem.tti
                        };
                        if (iobound.Equals("out"))
                        {
                            kcpSettings.uplinkCapacity = config.kcpItem.uplinkCapacity;
                            kcpSettings.downlinkCapacity = config.kcpItem.downlinkCapacity;
                        }
                        else if (iobound.Equals("in"))
                        {
                            kcpSettings.uplinkCapacity = config.kcpItem.downlinkCapacity; ;
                            kcpSettings.downlinkCapacity = config.kcpItem.downlinkCapacity;
                        }
                        else
                        {
                            kcpSettings.uplinkCapacity = config.kcpItem.uplinkCapacity;
                            kcpSettings.downlinkCapacity = config.kcpItem.downlinkCapacity;
                        }

                        kcpSettings.congestion = config.kcpItem.congestion;
                        kcpSettings.readBufferSize = config.kcpItem.readBufferSize;
                        kcpSettings.writeBufferSize = config.kcpItem.writeBufferSize;
                        kcpSettings.header = new Header
                        {
                            type = node.headerType
                        };
                        if (!Utils.IsNullOrEmpty(node.path))
                        {
                            kcpSettings.seed = node.path;
                        }
                        outbound.kcpSettings = kcpSettings;
                        break;*/
                    //ws
                    case "ws":                     
                        string path = node.path;
                        outbound.transport = new Transport();
                        if (!string.IsNullOrWhiteSpace(host))
                        {                            
                            outbound.transport.headers.Add("Host", host);
                        }
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            outbound.transport.path = path;
                        }
                        outbound.transport.type ="ws";

                        //TlsSettings tlsSettings = new TlsSettings();
                        //tlsSettings.allowInsecure = config.allowInsecure();
                        //if (!string.IsNullOrWhiteSpace(host))
                        //{
                        //    tlsSettings.serverName = host;
                        //}
                        //streamSettings.tlsSettings = tlsSettings;
                        break;
                    //h2
                    
                    case "h2":
                        outbound.transport = new Transport();
                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            outbound.transport.host = Utils.String2List(host);
                        }
                        outbound.transport.path = node.path;                        
                        outbound.transport.type = "http";

                        //TlsSettings tlsSettings2 = new TlsSettings();
                        //tlsSettings2.allowInsecure = config.allowInsecure();
                        //streamSettings.tlsSettings = tlsSettings2;
                        break;
                    //quic
                    case "quic":
                        /*
                        QuicSettings quicsettings = new QuicSettings
                        {
                            security = host,
                            key = node.path,
                            header = new Header
                            {
                                type = node.headerType
                            }
                        };*/
                        outbound.transport = new Transport();
                        outbound.transport.headers = new Dictionary<string, object>();
                        outbound.transport.headers.Add("type", node.headerType);
                        outbound.transport.type = "quic";
                        outbound.transport.headers = null;
                        if (node.streamSecurity == Global.StreamSecurity)
                        {
                            if (!string.IsNullOrWhiteSpace(sni))
                            {
                                outbound.tls.server_name = sni;
                            }
                            else
                            {
                                outbound.tls.server_name = node.address;
                            }
                        }
                        break;
                case "grpc":
                        /*
                        var grpcSettings = new GrpcSettings
                        {
                            serviceName = node.path,
                            multiMode = (node.headerType == Global.GrpcmultiMode)
                        };
                        */
                      
                        outbound.transport = new Transport();
                        outbound.transport.type = "grpc";
                        outbound.transport.service_name = String.IsNullOrEmpty(node.path) ? null : node.path;
                        outbound.transport.headers = null;
                        break;
                    case "xhttp":
                        outbound.transport = new Transport();
                        outbound.transport.type = "xhttp";
                        outbound.transport.path = String.IsNullOrEmpty(node.path) ? null : node.path;
                        outbound.transport.mode = String.IsNullOrEmpty(node.headerType) ? "auto" : node.headerType;
                        outbound.transport.headers = null;
                      //  if (!string.IsNullOrWhiteSpace(host))
                        //    outbound.transport.host = Utils.String2List(host);
                        if (!Utils.IsNullOrEmpty(node.transportExtra))
                        {
                            try
                            {
                                outbound.transport.extra = Utils.ParseJson(node.transportExtra);
                            }
                            catch
                            {
                            }
                        }
                        outbound.packet_encoding = "xudp";
                        break;
                    case "httpupgrade":
                        outbound.transport = new Transport();
                        outbound.transport.type = "httpupgrade";

                        outbound.transport.path = String.IsNullOrEmpty(node.path) ? null : node.path;
                       // if (!string.IsNullOrWhiteSpace(host))
                         //   outbound.transport.host = Utils.String2List(host);
                        outbound.transport.headers = null;
                        break;
                    default:
                        //tcp带http伪装
                       
                        if (node.headerType.Equals(Global.TcpHeaderHttp))
                        {
                            outbound.transport = new Transport();
                            outbound.transport.type = "http";
                            outbound.transport.headers = Utils.FromJson<Dictionary<string, object>>(Samples.sg_httpheaders);
                            outbound.transport.headers.Add("type", node.headerType);
                            if (iobound.Equals("out"))
                            {
                                //request填入自定义Host                                
                                string[] arrHost = host.Split(',');
                                string host2 = string.Join("\",\"", arrHost);
                                outbound.transport.headers.Add("Host", host2);
                                outbound.transport.method = "GET";
                                //request = request.Replace("$requestHost$", string.Format("\"{0}\"", config.requestHost()));
                                outbound.transport.host = arrHost.ToList();
                                //填入自定义Path
                                string pathHttp = @"/";
                                if (!Utils.IsNullOrEmpty(node.path))
                                {                                    
                                    string[] arrPath = node.path.Split(',');
                                    pathHttp = string.Join("\",\"", arrPath);
                                    outbound.transport.path = pathHttp;
                                }                              
                                
                           //     outbound.transport.headers.Add("request", Utils.FromJson<object>(request));

                            }
                            else if (iobound.Equals("in"))
                            {
                                outbound.transport = null;
                                //string response = Utils.GetEmbedText(Global.v2raySampleHttpresponseFileName);
                                //tcpSettings.header.response = Utils.FromJson<object>(response);
                            }
                            

                        }
                        else
                        {
                            outbound.transport = null;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        private static void FillInbound(SingBoxConfig cfg, VmessItem item, int port, bool vpnmode, bool isShareActive)
        {
            var serilizer = new JavaScriptSerializer();
            var inbound = serilizer.Deserialize<Inbound>(Samples.sg_mixedInbound);
            inbound.listen_port = port;
            if (isShareActive)
                inbound.listen = "0.0.0.0";
            cfg.inbounds = new List<Inbound>();            
            
            cfg.inbounds.Add(inbound);
            if (vpnmode)
            {
                inbound = serilizer.Deserialize<Inbound>( Samples.sg_vpnInbound);               
                cfg.inbounds.Add(inbound);
            }
            
        }

        /// <summary>
        /// Creates a SingBox SOCKS outbound that points to a local Xray SOCKS inbound.
        /// Used when a link needs Xray (e.g. xhttp transport).
        /// </summary>
        public static Outbound CreateXraySocksOutbound(string tag, int xrayPort, string authUser, string authPass)
        {
            return new Outbound
            {
                tag = tag,
                type = "socks",
                server = "127.0.0.1",
                server_port = xrayPort,
                username = authUser,
                password = authPass
            };
        }
    }
}
