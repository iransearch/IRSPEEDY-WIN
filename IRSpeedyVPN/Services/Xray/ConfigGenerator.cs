using IRSpeedyVPN.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using v2rayN;
using v2rayN.Base;
using v2rayN.Handler;
using v2rayN.Mode;

namespace IRSpeedyVPN.Services.Xray
{
    public class ConfigGenerator
    {
        public class XraySocksInfo
        {
            public string Link { get; set; }
            public string Tag { get; set; }
            public int Port { get; set; }
            public string User { get; set; }
            public string Pass { get; set; }
        }

        public static bool NeedsXray(VmessItem item)
        {
            if (item == null)
                return false;
            if (string.Equals(item.GetNetwork(), "xhttp", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(item.streamSecurity, Global.RealitySecurity, StringComparison.OrdinalIgnoreCase);
        }

        public static bool LinkNeedsXray(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return false;
            string msg;
            var item = ShareHandler.ImportFromConfigLink(link, out msg);
            return NeedsXray(item);
        }

        /// <summary>
        /// Generate a complete Xray config for a single xhttp link (used with Start/LoadConfigReq)
        /// </summary>
        public static string GetConfig(string link, int port, string authUser = null, string authPass = null)
        {
            string msg;
            var item = ShareHandler.ImportFromConfigLink(link, out msg);
            if (item == null)
                return null;

            var cfg = GenerateConfig(item, port, authUser, authPass);
            return Utils.ToJson(cfg);
        }

        /// <summary>
        /// Generate Xray config for URL test — returns Xray JSON with SOCKS inbounds + vless outbounds
        /// </summary>
        public static string GetUrlTestXrayConfig(List<XraySocksInfo> socksInfos)
        {
            if (socksInfos == null || socksInfos.Count == 0)
                return "{}";

            var cfg = new XrayConfig
            {
                dns = new Dns { servers = new List<DnsServer>() },
                log = new Log { access = "none", loglevel = "debug" },
                inbounds = new List<Inbound>(),
                outbounds = new List<Outbound>(),
                routing = new Routing
                {
                    domainStrategy = "AsIs",
                    rules = new List<Rule>()
                }
            };

            foreach (var info in socksInfos)
            {
                cfg.inbounds.Add(new Inbound
                {
                    listen = "127.0.0.1",
                    port = info.Port,
                    protocol = "socks",
                    tag = $"{info.Tag}-inbound",
                    settings = new InboundSettings
                    {
                        auth = "password",
                        udp = true,
                        accounts = new List<Account>
                        {
                            new Account { user = info.User, pass = info.Pass }
                        }
                    }
                });

                if (!string.IsNullOrWhiteSpace(info.Link))
                {
                    string msg;
                    var item = ShareHandler.ImportFromConfigLink(info.Link, out msg);
                    if (item != null)
                    {
                        var proxy = new Outbound { tag = info.Tag };
                        FillOutboundForItem(proxy, item);
                        cfg.outbounds.Add(proxy);
                    }
                }

                cfg.routing.rules.Add(new Rule
                {
                    type = "field",
                    inboundTag = new List<string> { $"{info.Tag}-inbound" },
                    outboundTag = info.Tag
                });
            }

            cfg.outbounds.Add(new Outbound
            {
                tag = "direct",
                protocol = "freedom"
            });

            return Utils.ToJson(cfg);
        }

        private static XrayConfig GenerateConfig(VmessItem item, int port, string authUser = null, string authPass = null)
        {
            var cfg = new XrayConfig();
            FillLog(cfg);
            FillDns(cfg);
            FillInbound(cfg, item, port, authUser, authPass);
            FillOutbound(cfg, item);
            FillRouting(cfg);
            return cfg;
        }

        private static void FillLog(XrayConfig cfg)
        {
            cfg.log = new Log
            {
                access = "none",
                loglevel = "debug"
            };
        }

        private static void FillDns(XrayConfig cfg)
        {
            cfg.dns = new Dns
            {
                servers = new List<DnsServer>
                {
                    new DnsServer
                    {
                        address = "127.0.0.1",
                        port = 5533,
                        queryStrategy = "UseIPv4",
                        skipFallBack = true
                    },
                    new DnsServer
                    {
                        address = "127.0.0.1",
                        port = 5533,
                        queryStrategy = "UseIPv6",
                        skipFallBack = true
                    }
                }
            };
        }

        private static void FillInbound(XrayConfig cfg, VmessItem item, int port, string authUser = null, string authPass = null)
        {
            if (string.IsNullOrEmpty(authUser))
                authUser = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(authPass))
                authPass = Guid.NewGuid().ToString("N");

            cfg.inbounds = new List<Inbound>
            {
                new Inbound
                {
                    listen = "127.0.0.1",
                    port = port,
                    protocol = "socks",
                    tag = "proxy-inbound",
                    settings = new InboundSettings
                    {
                        auth = "password",
                        udp = true,
                        accounts = new List<Account>
                        {
                            new Account
                            {
                                user = authUser,
                                pass = authPass
                            }
                        }
                    }
                }
            };
        }

        private static void FillOutbound(XrayConfig cfg, VmessItem node)
        {
            if (cfg.outbounds == null)
                cfg.outbounds = new List<Outbound>();

            var proxy = new Outbound
            {
                tag = "proxy"
            };
            FillOutboundForItem(proxy, node);
            cfg.outbounds.Add(proxy);

            cfg.outbounds.Add(new Outbound
            {
                tag = "direct",
                protocol = "freedom",
                settings = new OutboundSettings
                {
                    domainStrategy = "AsIs"
                }
            });
        }

        private static void FillRouting(XrayConfig cfg)
        {
            cfg.routing = new Routing
            {
                domainStrategy = "AsIs",
                rules = new List<Rule>
                {
                    new Rule
                    {
                        type = "field",
                        inboundTag = new List<string> { "proxy-inbound" },
                        outboundTag = "proxy"
                    },
                    new Rule
                    {
                        type = "field",
                        ip = new List<string> { "127.0.0.1" },
                        port = "5533",
                        outboundTag = "direct"
                    }
                }
            };
        }
        public static string GetSmartBalancerConfig(IEnumerable<string> links, int port, string authUser, string authPass)
        {
            var root = JObject.Parse(Samples.BalancerConfig);
            var serializer = new JsonSerializer { NullValueHandling = NullValueHandling.Ignore };

            var inbounds = root["inbounds"] as JArray ?? new JArray();
            inbounds.Add(JObject.FromObject(new Inbound
            {
                listen = "127.0.0.1",
                port = port,
                protocol = "socks",
                tag = "smart-inbound",
                settings = new InboundSettings
                {
                    auth = "password",
                    udp = true,
                    accounts = new List<Account>
                    {
                        new Account { user = authUser, pass = authPass }
                    }
                }
            }, serializer));
            root["inbounds"] = inbounds;

            var outbounds = new JArray();
            int idx = 0;
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                string msg;
                var item = ShareHandler.ImportFromConfigLink(link, out msg);
                if (item == null)
                    continue;

                var proxy = new Outbound { tag = $"smart-proxy-{idx}" };
                FillOutboundForItem(proxy, item);
                if (proxy.protocol == null)
                    continue;

                // The pool skips the per-link LinkNeedsXray decision and hands every
                // URL to Xray, so a link the core refuses would abort the whole config
                // build and take the connection down. Drop it and keep the rest.
                var refusal = SmartIpRouting.CoreRefusalReason(proxy);
                if (refusal != null)
                {
                    LogHelper.WriteExLog($"Smart pool outbound rejected: {proxy.tag} ({refusal})");
                    continue;
                }

                outbounds.Add(JObject.FromObject(proxy, serializer));
                idx++;
            }

            if (idx == 0)
                return null;

            ApplySmartIpRouting(root, outbounds, serializer);

            // routing rules in the balancer sample reference the "direct" and "block" outbounds
            outbounds.Add(JObject.FromObject(new Outbound
            {
                tag = "direct",
                protocol = "freedom",
                settings = new OutboundSettings { domainStrategy = "AsIs" }
            }, serializer));
            outbounds.Add(JObject.FromObject(new Outbound
            {
                tag = "block",
                protocol = "blackhole"
            }, serializer));

            root["outbounds"] = outbounds;
            root.Remove("outbound");

            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Adds the AI leastLoad balancer next to the main smart balancer. Gated by
        /// the VOD/AI toggle; when it is off, or when no AI link survives, the config
        /// is left exactly as it was.
        /// </summary>
        private static void ApplySmartIpRouting(JObject root, JArray outbounds, JsonSerializer serializer)
        {
            if (!SmartIpRouting.IsEnabled())
                return;

            string aiFallbackTag;
            var aiOutbounds = SmartIpRouting.BuildOutbounds(
                SmartIpRouting.AiLinks, SmartIpRouting.AiProxyPrefix, "AI", serializer, out aiFallbackTag);

            if (aiOutbounds.Count == 0 || aiFallbackTag == null)
                return;

            var balancers = root["routing"]?["balancers"] as JArray;
            var rules = root["routing"]?["rules"] as JArray;
            if (balancers == null || rules == null)
                return;

            SmartIpRouting.ExtendObservatorySelector(root);

            foreach (var outbound in aiOutbounds)
                outbounds.Add(outbound);
            balancers.Add(SmartIpRouting.AiBalancer(aiFallbackTag));

            // The AI rule runs ahead of the geoip/geosite checks and the catch-all so
            // its traffic never reaches the main balancer. The first rule is the
            // UDP/443 block, which must stay first.
            rules.Insert(rules.Count > 0 ? 1 : 0, SmartIpRouting.AiRule());
        }

        public static void FillOutboundForItem(Outbound outbound, VmessItem node)
        {
            if (outbound == null || node == null) return;

            try
            {
                if (node.configType == EConfigType.VMess)
                {
                    outbound.protocol = "vmess";
                    outbound.settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port,
                        id = node.id,
                        alterId = node.alterId,
                        security = string.IsNullOrEmpty(node.security) ? "auto" : node.security
                    };
                    boundStreamSettings(node, outbound);
                }
                else if (node.configType == EConfigType.VLESS)
                {
                    outbound.protocol = "vless";
                    outbound.settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port,
                        id = node.id,
                        encryption = string.IsNullOrEmpty(node.security) ? "none" : node.security,
                        flow = node.flow ?? ""
                    };
                    boundStreamSettings(node, outbound);
                }
                else if (node.configType == EConfigType.Trojan)
                {
                    outbound.protocol = "trojan";
                    outbound.settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port,
                        password = node.id
                    };
                    boundStreamSettings(node, outbound);
                }
                else if (node.configType == EConfigType.Shadowsocks)
                {
                    outbound.protocol = "shadowsocks";
                    outbound.settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port,
                        method = string.IsNullOrEmpty(node.security) ? "aes-256-gcm" : node.security,
                        password = node.id
                    };
                }
                else if (node.configType == EConfigType.Socks)
                {
                    outbound.protocol = "socks";
                    var settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port
                    };
                    if (!string.IsNullOrEmpty(node.id) && !string.IsNullOrEmpty(node.security))
                    {
                        settings.users = new List<SocksUser>
                        {
                            new SocksUser { user = node.security, pass = node.id }
                        };
                    }
                    outbound.settings = settings;
                }
                else if (node.configType == EConfigType.Http)
                {
                    outbound.protocol = "http";
                    var settings = new OutboundSettings
                    {
                        address = node.address,
                        port = node.port
                    };
                    if (!string.IsNullOrEmpty(node.id) && !string.IsNullOrEmpty(node.security))
                    {
                        settings.users = new List<SocksUser>
                        {
                            new SocksUser { user = node.security, pass = node.id }
                        };
                    }
                    outbound.settings = settings;
                }
                else if (node.configType == EConfigType.Hysteria2)
                {
                    outbound.protocol = "hysteria2";
                    var settings = new OutboundSettings
                    {
                        server = node.address,
                        server_port = node.port > 0 ? node.port : 443,
                        password = node.password
                    };
                    if (!string.IsNullOrWhiteSpace(node.obfs_param))
                    {
                        settings.obfs = new HysteriaObfs
                        {
                            type = string.IsNullOrWhiteSpace(node.obfs) ? "salamander" : node.obfs,
                            password = node.obfs_param
                        };
                    }
                    settings.tls = new HysteriaTls
                    {
                        enabled = true,
                        server_name = string.IsNullOrWhiteSpace(node.sni) ? null : node.sni,
                        insecure = string.IsNullOrWhiteSpace(node.allowInsecure) ? (bool?)null : Utils.ToBool(node.allowInsecure)
                    };
                    outbound.settings = settings;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
        }

        private static void boundStreamSettings(VmessItem node, Outbound outbound)
        {
            try
            {
                var network = node.GetNetwork();
                if (string.IsNullOrEmpty(network))
                    return;

                var stream = new StreamSettings
                {
                    network = string.Equals(network, "tcp", StringComparison.OrdinalIgnoreCase) ? "raw" : network,
                    sockopt = new Sockopt
                    {
                        domainStrategy = "UseIP"
                    }
                };

                string host = (node.requestHost ?? "").Trim();
                string sni = node.sni;

                if (string.Equals(node.streamSecurity, Global.StreamSecurity, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.streamSecurity, Global.RealitySecurity, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(node.streamSecurity, Global.RealitySecurity, StringComparison.OrdinalIgnoreCase))
                    {
                        stream.security = "reality";
                        stream.realitySettings = new RealitySettings
                        {
                            serverName = !string.IsNullOrWhiteSpace(sni) ? sni : (!string.IsNullOrWhiteSpace(host) ? host : null),
                            fingerprint = node.fingerPrint,
                            password = node.publicKey,
                            shortId = node.shortId,
                            spiderX = node.spiderX
                        };
                    }
                    else
                    {
                        stream.security = "tls";
                        stream.tlsSettings = new TlsSettings
                        {
                            //allowInsecure = Utils.ToBool(node.allowInsecure),
                            pinnedPeerCertSha256=node.certSha256,
                            serverName = !string.IsNullOrWhiteSpace(sni) ? sni : null,
                            alpn = node.GetAlpn()
                        };
                        if(Utils.ToBool(node.allowInsecure))
                        {                            
                            stream.tlsSettings.pinnedPeerCertSha256 = node.certSha256;
                        }
                        if (!string.IsNullOrEmpty(node.fingerPrint))
                            stream.tlsSettings.fingerprint = node.fingerPrint;
                    }
                }
                else if (string.Equals(node.streamSecurity, Global.StreamSecurityX, StringComparison.OrdinalIgnoreCase))
                {
                    stream.security = "xtls";
                    stream.xtlsSettings = new XtlsSettings
                    {
                        allowInsecure = Utils.ToBool(node.allowInsecure),
                        serverName = !string.IsNullOrWhiteSpace(sni) ? sni : null,
                        alpn = node.GetAlpn()
                    };
                }
                else
                {
                    stream.security = "none";
                }

                switch (network)
                {
                    case "tcp":
                        if (string.Equals(node.headerType, Global.TcpHeaderHttp, StringComparison.OrdinalIgnoreCase))
                        {
                            stream.tcpSettings = new TcpSettings
                            {
                                header = new Header { type = "http" }
                            };
                        }
                        break;

                    case "kcp":
                        stream.kcpSettings = new KcpSettings
                        {
                            header = new Header { type = node.headerType }
                        };
                        if (!string.IsNullOrWhiteSpace(node.path))
                            stream.kcpSettings.seed = node.path;
                        break;

                    case "ws":
                        stream.wsSettings = new WsSettings
                        {
                            path = node.path,
                            headers = !string.IsNullOrWhiteSpace(host)
                                ? new Headers { Host = host }
                                : null
                        };
                        break;

                    case "h2":
                        stream.httpSettings = new HttpSettings
                        {
                            path = node.path,
                            host = !string.IsNullOrWhiteSpace(host)
                                ? Utils.String2List(host)
                                : null
                        };
                        break;

                    case "quic":
                        stream.quicSettings = new QuicSettings
                        {
                            security = host,
                            key = node.path,
                            header = new Header { type = node.headerType }
                        };
                        break;

                    case "grpc":
                        stream.grpcSettings = new GrpcSettings
                        {
                            serviceName = node.path,
                            multiMode = string.Equals(node.headerType, Global.GrpcmultiMode, StringComparison.OrdinalIgnoreCase)
                        };
                        break;

                    case "xhttp":
                        stream.xhttpSettings = new XhttpSettings
                        {
                            host = !string.IsNullOrWhiteSpace(host) ? host : null,
                            path = !string.IsNullOrWhiteSpace(node.path) ? node.path : null,
                            mode = string.IsNullOrWhiteSpace(node.headerType) ? "auto" : node.headerType
                        };
                        if (!string.IsNullOrEmpty(node.transportExtra))
                        {
                            try
                            {
                                var extraObject = Utils.ParseJson(node.transportExtra) as Dictionary<string, object>;

                                if (extraObject != null)
                                {
                                    // Cast downloadSettings to Dictionary
                                    if (extraObject.ContainsKey("downloadSettings") &&
                                        extraObject["downloadSettings"] is Dictionary<string, object> downloadSettings)
                                    {
                                        // Cast tlsSettings to Dictionary
                                        if (downloadSettings.ContainsKey("tlsSettings") &&
                                            downloadSettings["tlsSettings"] is Dictionary<string, object> tlsSettings)
                                        {
                                            // Remove allowInsecure
                                            tlsSettings.Remove("allowInsecure");                                         
                                        }
                                    }

                                    stream.xhttpSettings.extra = extraObject;
                                }
                            }
                            catch { }
                        }
                        break;

                    case "httpupgrade":
                        stream.httpupgradeSettings = new HttpupgradeSettings
                        {
                            host = !string.IsNullOrWhiteSpace(host) ? host : null,
                            path = !string.IsNullOrWhiteSpace(node.path) ? node.path : null
                        };
                        break;
                }

                outbound.streamSettings = stream;
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
        }
    }
}
