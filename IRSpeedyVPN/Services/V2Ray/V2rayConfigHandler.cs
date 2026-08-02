using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using v2rayN.Base;
using v2rayN.Mode;


namespace v2rayN.Handler
{
    /// <summary>
    /// v2ray配置文件处理类
    /// </summary>
    class V2rayConfigHandler
    {
        private static string SampleClient = Global.v2raySampleClient;
        private static string SampleServer = Global.v2raySampleServer;

        #region 生成客户端配置

        /// <summary>
        /// 生成v2ray的客户端配置文件
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static int GenerateClientConfig(VmessItem node, string fileName, out string msg, out string content)
        {
            content = string.Empty;
            try
            {
                if (node == null)
                {
                    msg = "CheckServerSettings";
                    return -1;
                }

                msg = "InitialConfiguration";
                if (node.configType == EConfigType.Custom)
                {
                    return GenerateClientCustomConfig(node, fileName, out msg);
                }
                else
                {
                    V2rayConfig v2rayConfig = null;
                    if (GenerateClientConfigContent(node, false, ref v2rayConfig, out msg) != 0)
                    {
                        return -1;
                    }
                    if (Utils.IsNullOrEmpty(fileName))
                    {
                        content = Utils.ToJson(v2rayConfig);
                    }
                    else
                    {
                        Utils.ToJsonFile(v2rayConfig, fileName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog("GenerateClientConfig", ex);
                msg = "FailedGenDefaultConfiguration";
                return -1;
            }
            return 0;
        }



        /// <summary>
        /// 日志
        /// </summary>
        /// <param name="config"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int log(Config config, ref V2rayConfig v2rayConfig, bool blExport)
        {
            try
            {
                if (blExport)
                {
                    if (config.logEnabled)
                    {
                        v2rayConfig.log.loglevel = config.loglevel;
                    }
                    else
                    {
                        v2rayConfig.log.loglevel = config.loglevel;
                        v2rayConfig.log.access = "";
                        v2rayConfig.log.error = "";
                    }
                }
                else
                {
                    if (config.logEnabled)
                    {
                        v2rayConfig.log.loglevel = config.loglevel;
                        v2rayConfig.log.access = Utils.GetPath(v2rayConfig.log.access);
                        v2rayConfig.log.error = Utils.GetPath(v2rayConfig.log.error);
                    }
                    else
                    {
                        v2rayConfig.log.loglevel = config.loglevel;
                        v2rayConfig.log.access = "";
                        v2rayConfig.log.error = "";
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        /// <summary>
        /// 本地端口
        /// </summary>
        /// <param name="config"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int inbound(Config config, ref V2rayConfig v2rayConfig)
        {
            try
            {
                v2rayConfig.inbounds = new List<Inbounds>();

                Inbounds inbound = GetInbound(config.inbound[0], Global.InboundSocks, 0, true);
                v2rayConfig.inbounds.Add(inbound);

                //http
                Inbounds inbound2 = GetInbound(config.inbound[0], Global.InboundHttp, 1, false);
                v2rayConfig.inbounds.Add(inbound2);

                if (config.inbound[0].allowLANConn)
                {
                    Inbounds inbound3 = GetInbound(config.inbound[0], Global.InboundSocks2, 2, true);
                    inbound3.listen = "0.0.0.0";
                    v2rayConfig.inbounds.Add(inbound3);

                    Inbounds inbound4 = GetInbound(config.inbound[0], Global.InboundHttp2, 3, false);
                    inbound4.listen = "0.0.0.0";
                    v2rayConfig.inbounds.Add(inbound4);

                    //auth
                    if (!Utils.IsNullOrEmpty(config.inbound[0].user) && !Utils.IsNullOrEmpty(config.inbound[0].pass))
                    {
                        inbound3.settings.auth = "password";
                        inbound3.settings.accounts = new List<AccountsItem> { new AccountsItem() { user = config.inbound[0].user, pass = config.inbound[0].pass } };

                        inbound4.settings.auth = "password";
                        inbound4.settings.accounts = new List<AccountsItem> { new AccountsItem() { user = config.inbound[0].user, pass = config.inbound[0].pass } };
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        private static Inbounds GetInbound(InItem inItem, string tag, int offset, bool bSocks)
        {
            string result = Utils.GetEmbedText(Global.v2raySampleInbound);
            if (Utils.IsNullOrEmpty(result))
            {
                return null;
            }

            var inbound = Utils.FromJson<Inbounds>(result);
            if (inbound == null)
            {
                return null;
            }
            inbound.tag = tag;
            inbound.port = inItem.localPort + offset;
            inbound.protocol = bSocks ? Global.InboundSocks : Global.InboundHttp;
            //inbound.settings.udp = inItem.udpEnabled;
            inbound.sniffing.enabled = inItem.sniffingEnabled;

            return inbound;
        }

        /// <summary>
        /// 路由
        /// </summary>
        /// <param name="config"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int routing(Config config, ref V2rayConfig v2rayConfig)
        {
            try
            {
                if (v2rayConfig.routing != null
                  && v2rayConfig.routing.rules != null)
                {
                    v2rayConfig.routing.domainStrategy = config.domainStrategy;
                    v2rayConfig.routing.domainMatcher = Utils.IsNullOrEmpty(config.domainMatcher) ? null : config.domainMatcher;

                    if (config.enableRoutingAdvanced)
                    {
                        if (config.routings != null && config.routingIndex < config.routings.Count)
                        {
                            foreach (var item in config.routings[config.routingIndex].rules)
                            {
                                if (item.enabled)
                                {
                                    routingUserRule(item, ref v2rayConfig);
                                }
                            }
                        }
                    }
                    else
                    {
                        var lockedItem = ConfigHandler.GetLockedRoutingItem(ref config);
                        if (lockedItem != null)
                        {
                            foreach (var item in lockedItem.rules)
                            {
                                routingUserRule(item, ref v2rayConfig);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }
        private static int routingUserRule(RulesItem rules, ref V2rayConfig v2rayConfig)
        {
            try
            {
                if (rules == null)
                {
                    return 0;
                }
                if (Utils.IsNullOrEmpty(rules.port))
                {
                    rules.port = null;
                }
                if (rules.domain != null && rules.domain.Count == 0)
                {
                    rules.domain = null;
                }
                if (rules.ip != null && rules.ip.Count == 0)
                {
                    rules.ip = null;
                }
                if (rules.protocol != null && rules.protocol.Count == 0)
                {
                    rules.protocol = null;
                }

                var hasDomainIp = false;
                if (rules.domain != null && rules.domain.Count > 0)
                {
                    var it = Utils.DeepCopy(rules);
                    it.ip = null;
                    it.type = "field";
                    for (int k = it.domain.Count - 1; k >= 0; k--)
                    {
                        if (it.domain[k].StartsWith("#"))
                        {
                            it.domain.RemoveAt(k);
                        }
                        it.domain[k] = it.domain[k].Replace(Global.RoutingRuleComma, ",");
                    }
                    //if (Utils.IsNullOrEmpty(it.port))
                    //{
                    //    it.port = null;
                    //}
                    //if (it.protocol != null && it.protocol.Count == 0)
                    //{
                    //    it.protocol = null;
                    //}
                    v2rayConfig.routing.rules.Add(it);
                    hasDomainIp = true;
                }
                if (rules.ip != null && rules.ip.Count > 0)
                {
                    var it = Utils.DeepCopy(rules);
                    it.domain = null;
                    it.type = "field";
                    //if (Utils.IsNullOrEmpty(it.port))
                    //{
                    //    it.port = null;
                    //}
                    //if (it.protocol != null && it.protocol.Count == 0)
                    //{
                    //    it.protocol = null;
                    //}
                    v2rayConfig.routing.rules.Add(it);
                    hasDomainIp = true;
                }
                if (!hasDomainIp)
                {
                    if (!Utils.IsNullOrEmpty(rules.port))
                    {
                        var it = Utils.DeepCopy(rules);
                        //it.domain = null;
                        //it.ip = null;
                        //if (it.protocol != null && it.protocol.Count == 0)
                        //{
                        //    it.protocol = null;
                        //}
                        it.type = "field";
                        v2rayConfig.routing.rules.Add(it);
                    }
                    else if (rules.protocol != null && rules.protocol.Count > 0)
                    {
                        var it = Utils.DeepCopy(rules);
                        //it.domain = null;
                        //it.ip = null;
                        //if (Utils.IsNullOrEmpty(it.port))
                        //{
                        //    it.port = null;
                        //}
                        it.type = "field";
                        v2rayConfig.routing.rules.Add(it);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        /// <summary>
        /// vmess协议服务器配置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int outbound(VmessItem node, ref V2rayConfig v2rayConfig)
        {
            try
            {
                var config = LazyConfig.Instance.GetConfig();
                Outbounds outbound = v2rayConfig.outbounds[0];
                if (node.configType == EConfigType.VMess)
                {
                    VnextItem vnextItem;
                    if (outbound.settings.vnext.Count <= 0)
                    {
                        vnextItem = new VnextItem();
                        outbound.settings.vnext.Add(vnextItem);
                    }
                    else
                    {
                        vnextItem = outbound.settings.vnext[0];
                    }
                    //远程服务器地址和端口
                    vnextItem.address = node.address;
                    vnextItem.port = node.port;

                    UsersItem usersItem;
                    if (vnextItem.users.Count <= 0)
                    {
                        usersItem = new UsersItem();
                        vnextItem.users.Add(usersItem);
                    }
                    else
                    {
                        usersItem = vnextItem.users[0];
                    }
                    //远程服务器用户ID
                    usersItem.id = node.id;
                    usersItem.alterId = node.alterId;
                    usersItem.email = Global.userEMail;
                    if (Global.vmessSecuritys.Contains(node.security))
                    {
                        usersItem.security = node.security;
                    }
                    else
                    {
                        usersItem.security = Global.DefaultSecurity;
                    }

                    //Mux
                    outbound.mux.enabled = config.muxEnabled;
                    outbound.mux.concurrency = config.muxEnabled ? 8 : -1;

                    boundStreamSettings(node, "out", outbound.streamSettings);

                    outbound.protocol = Global.vmessProtocolLite;
                    outbound.settings.servers = null;
                }
                else if (node.configType == EConfigType.Shadowsocks)
                {
                    ServersItem serversItem;
                    if (outbound.settings.servers.Count <= 0)
                    {
                        serversItem = new ServersItem();
                        outbound.settings.servers.Add(serversItem);
                    }
                    else
                    {
                        serversItem = outbound.settings.servers[0];
                    }
                    //远程服务器地址和端口
                    serversItem.address = node.address;
                    serversItem.port = node.port;
                    serversItem.password = node.id;
                    serversItem.method = LazyConfig.Instance.GetShadowsocksSecuritys(node).Contains(node.security) ? node.security : "none";


                    serversItem.ota = false;
                    serversItem.level = 1;

                    outbound.mux.enabled = false;
                    outbound.mux.concurrency = -1;

                    boundStreamSettings(node, "out", outbound.streamSettings);

                    outbound.protocol = Global.ssProtocolLite;
                    outbound.settings.vnext = null;
                }
                else if (node.configType == EConfigType.Socks)
                {
                    ServersItem serversItem;
                    if (outbound.settings.servers.Count <= 0)
                    {
                        serversItem = new ServersItem();
                        outbound.settings.servers.Add(serversItem);
                    }
                    else
                    {
                        serversItem = outbound.settings.servers[0];
                    }
                    //远程服务器地址和端口
                    serversItem.address = node.address;
                    serversItem.port = node.port;
                    serversItem.method = null;
                    serversItem.password = null;

                    if (!Utils.IsNullOrEmpty(node.security)
                        && !Utils.IsNullOrEmpty(node.id))
                    {
                        SocksUsersItem socksUsersItem = new SocksUsersItem
                        {
                            user = node.security,
                            pass = node.id,
                            level = 1
                        };

                        serversItem.users = new List<SocksUsersItem>() { socksUsersItem };
                    }

                    outbound.mux.enabled = false;
                    outbound.mux.concurrency = -1;

                    outbound.protocol = Global.socksProtocolLite;
                    outbound.settings.vnext = null;
                }
                else if (node.configType == EConfigType.VLESS)
                {
                    VnextItem vnextItem;
                    if (outbound.settings.vnext.Count <= 0)
                    {
                        vnextItem = new VnextItem();
                        outbound.settings.vnext.Add(vnextItem);
                    }
                    else
                    {
                        vnextItem = outbound.settings.vnext[0];
                    }
                    //远程服务器地址和端口
                    vnextItem.address = node.address;
                    vnextItem.port = node.port;

                    UsersItem usersItem;
                    if (vnextItem.users.Count <= 0)
                    {
                        usersItem = new UsersItem();
                        vnextItem.users.Add(usersItem);
                    }
                    else
                    {
                        usersItem = vnextItem.users[0];
                    }
                    //远程服务器用户ID
                    usersItem.id = node.id;
                    usersItem.flow = string.Empty;
                    usersItem.email = Global.userEMail;
                    usersItem.encryption = node.security;

                    //Mux
                    outbound.mux.enabled = config.muxEnabled;
                    outbound.mux.concurrency = config.muxEnabled ? 8 : -1;

                    boundStreamSettings(node, "out", outbound.streamSettings);

                    //if xtls
                    if (node.streamSecurity == Global.StreamSecurityX)
                    {
                        if (Utils.IsNullOrEmpty(node.flow))
                        {
                            usersItem.flow = Global.xtlsFlows[1];
                        }
                        else
                        {
                            usersItem.flow = node.flow.Replace("splice", "direct");
                        }

                        outbound.mux.enabled = false;
                        outbound.mux.concurrency = -1;
                    }

                    outbound.protocol = Global.vlessProtocolLite;
                    outbound.settings.servers = null;
                }
                else if (node.configType == EConfigType.Trojan)
                {
                    ServersItem serversItem;
                    if (outbound.settings.servers.Count <= 0)
                    {
                        serversItem = new ServersItem();
                        outbound.settings.servers.Add(serversItem);
                    }
                    else
                    {
                        serversItem = outbound.settings.servers[0];
                    }
                    //远程服务器地址和端口
                    serversItem.address = node.address;
                    serversItem.port = node.port;
                    serversItem.password = node.id;
                    serversItem.flow = string.Empty;

                    serversItem.ota = false;
                    serversItem.level = 1;

                    //if xtls
                    if (node.streamSecurity == Global.StreamSecurityX)
                    {
                        if (Utils.IsNullOrEmpty(node.flow))
                        {
                            serversItem.flow = Global.xtlsFlows[1];
                        }
                        else
                        {
                            serversItem.flow = node.flow.Replace("splice", "direct");
                        }

                        outbound.mux.enabled = false;
                        outbound.mux.concurrency = -1;
                    }

                    outbound.mux.enabled = false;
                    outbound.mux.concurrency = -1;

                    boundStreamSettings(node, "out", outbound.streamSettings);

                    outbound.protocol = Global.trojanProtocolLite;
                    outbound.settings.vnext = null;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        /// <summary>
        /// 底层传输配置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="iobound"></param>
        /// <param name="streamSettings"></param>
        /// <returns></returns>
        private static Dictionary<string, object> GetTransportExtra(VmessItem node)
        {
            if (Utils.IsNullOrEmpty(node?.transportExtra))
                return null;

            try
            {
                return Utils.ParseJson(node.transportExtra);
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveTlsServerName(VmessItem node, string host, string sni)
        {
            if (!string.IsNullOrWhiteSpace(sni))
                return sni;
            if (!string.IsNullOrWhiteSpace(host))
                return Utils.String2List(host)[0];
            return null;
        }

        private static TlsSettings CreateTlsSettings(VmessItem node, string host, string sni)
        {
            var tlsSettings = new TlsSettings
            {
                allowInsecure = Utils.ToBool(node.allowInsecure),
                alpn = node.GetAlpn()
            };
            var serverName = ResolveTlsServerName(node, host, sni);
            if (!string.IsNullOrWhiteSpace(serverName))
                tlsSettings.serverName = serverName;
            if (!string.IsNullOrEmpty(node.fingerPrint))
                tlsSettings.fingerprint = node.fingerPrint;
            return tlsSettings;
        }

        private static int boundStreamSettings(VmessItem node, string iobound, StreamSettings streamSettings)
        {
            try
            {
                var config = LazyConfig.Instance.GetConfig();

                streamSettings.network = node.GetNetwork();
                streamSettings.sockopt = new Sockopt
                {
                    domainStrategy = "UseIP"
                };
                string host = node.requestHost.TrimEx();
                string sni = node.sni;

                //if tls
                if (node.streamSecurity == Global.StreamSecurity)
                {
                    streamSettings.security = node.streamSecurity;
                    streamSettings.tlsSettings = CreateTlsSettings(node, host, sni);
                }

                //if xtls
                if (node.streamSecurity == Global.StreamSecurityX)
                {
                    streamSettings.security = node.streamSecurity;
                    streamSettings.xtlsSettings = CreateTlsSettings(node, host, sni);
                }

                //if reality
                if (node.streamSecurity == Global.RealitySecurity)
                {
                    streamSettings.security = node.streamSecurity;
                    var realitySettings = new RealitySettings
                    {
                        publicKey = node.publicKey,
                        shortId = node.shortId
                    };
                    var serverName = ResolveTlsServerName(node, host, sni);
                    if (!string.IsNullOrWhiteSpace(serverName))
                        realitySettings.serverName = serverName;
                    if (!string.IsNullOrEmpty(node.fingerPrint))
                        realitySettings.fingerprint = node.fingerPrint;
                    streamSettings.realitySettings = realitySettings;
                }

                //streamSettings
                switch (node.GetNetwork())
                {
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
                        streamSettings.kcpSettings = kcpSettings;
                        break;
                    //ws
                    case "ws":
                        WsSettings wsSettings = new WsSettings
                        {
                        };

                        string path = node.path;
                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            wsSettings.headers = new Headers
                            {
                                Host = host
                            };
                        }
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            wsSettings.path = path;
                        }
                        streamSettings.wsSettings = wsSettings;

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
                        HttpSettings httpSettings = new HttpSettings();

                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            httpSettings.host = Utils.String2List(host);
                        }
                        httpSettings.path = node.path;

                        streamSettings.httpSettings = httpSettings;

                        //TlsSettings tlsSettings2 = new TlsSettings();
                        //tlsSettings2.allowInsecure = config.allowInsecure();
                        //streamSettings.tlsSettings = tlsSettings2;
                        break;
                    //quic
                    case "quic":
                        QuicSettings quicsettings = new QuicSettings
                        {
                            security = host,
                            key = node.path,
                            header = new Header
                            {
                                type = node.headerType
                            }
                        };
                        streamSettings.quicSettings = quicsettings;
                        if (node.streamSecurity == Global.StreamSecurity)
                        {
                            if (!string.IsNullOrWhiteSpace(sni))
                            {
                                streamSettings.tlsSettings.serverName = sni;
                            }
                            else
                            {
                                streamSettings.tlsSettings.serverName = node.address;
                            }
                        }
                        break;
                    case "grpc":
                        var grpcSettings = new GrpcSettings
                        {
                            serviceName = node.path,
                            multiMode = (node.headerType == Global.GrpcmultiMode)
                        };

                        streamSettings.grpcSettings = grpcSettings;
                        break;
                    case "xhttp":
                        var xhttpSettings = new XhttpSettings
                        {
                            path = node.path,
                            mode = string.IsNullOrEmpty(node.headerType) ? "auto" : node.headerType
                        };
                        if (!string.IsNullOrWhiteSpace(host))
                            xhttpSettings.host = host;
                        var xhttpExtra = GetTransportExtra(node);
                        if (xhttpExtra != null && xhttpExtra.Count > 0)
                            xhttpSettings.extra = xhttpExtra;
                        streamSettings.xhttpSettings = xhttpSettings;
                        break;
                    case "httpupgrade":
                        var httpupgradeSettings = new HttpupgradeSettings
                        {
                            path = node.path
                        };
                        if (!string.IsNullOrWhiteSpace(host))
                            httpupgradeSettings.host = host;
                        streamSettings.httpupgradeSettings = httpupgradeSettings;
                        break;
                    default:
                        //tcp带http伪装
                        if (node.headerType.Equals(Global.TcpHeaderHttp))
                        {
                            TcpSettings tcpSettings = new TcpSettings
                            {
                                header = new Header
                                {
                                    type = node.headerType
                                }
                            };

                            if (iobound.Equals("out"))
                            {
                                //request填入自定义Host
                                string request = Utils.GetEmbedText(Global.v2raySampleHttprequestFileName);
                                string[] arrHost = host.Split(',');
                                string host2 = string.Join("\",\"", arrHost);
                                request = request.Replace("$requestHost$", $"\"{host2}\"");
                                //request = request.Replace("$requestHost$", string.Format("\"{0}\"", config.requestHost()));

                                //填入自定义Path
                                string pathHttp = @"/";
                                if (!Utils.IsNullOrEmpty(node.path))
                                {
                                    string[] arrPath = node.path.Split(',');
                                    pathHttp = string.Join("\",\"", arrPath);
                                }
                                request = request.Replace("$requestPath$", $"\"{pathHttp}\"");
                                tcpSettings.header.request = Utils.FromJson<object>(request);
                            }
                            else if (iobound.Equals("in"))
                            {
                                //string response = Utils.GetEmbedText(Global.v2raySampleHttpresponseFileName);
                                //tcpSettings.header.response = Utils.FromJson<object>(response);
                            }

                            streamSettings.tcpSettings = tcpSettings;
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

        /// <summary>
        /// remoteDNS
        /// </summary>
        /// <param name="config"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int dns(Config config, ref V2rayConfig v2rayConfig)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.remoteDNS))
                {
                    return 0;
                }

                //Outbound Freedom domainStrategy
                if (!string.IsNullOrWhiteSpace(config.domainStrategy4Freedom))
                {
                    var outbound = v2rayConfig.outbounds[1];
                    outbound.settings.domainStrategy = config.domainStrategy4Freedom;
                    outbound.settings.userLevel = 0;
                }

                var obj = Utils.ParseJson(config.remoteDNS);
                if (obj != null && obj.ContainsKey("servers"))
                {
                    v2rayConfig.dns = obj;
                }
                else
                {
                    List<string> servers = new List<string>();

                    string[] arrDNS = config.remoteDNS.Split(',');
                    foreach (string str in arrDNS)
                    {
                        //if (Utils.IsIP(str))
                        //{
                        servers.Add(str);
                        //}
                    }
                    //servers.Add("localhost");
                    v2rayConfig.dns = new Mode.Dns
                    {
                        servers = servers
                    };
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        private static int statistic(Config config, ref V2rayConfig v2rayConfig)
        {
            if (config.enableStatistics)
            {
                string tag = Global.InboundAPITagName;
                API apiObj = new API();
                Policy policyObj = new Policy();
                SystemPolicy policySystemSetting = new SystemPolicy();

                string[] services = { "StatsService" };

                v2rayConfig.stats = new Stats();

                apiObj.tag = tag;
                apiObj.services = services.ToList();
                v2rayConfig.api = apiObj;

                policySystemSetting.statsOutboundDownlink = true;
                policySystemSetting.statsOutboundUplink = true;
                policyObj.system = policySystemSetting;
                v2rayConfig.policy = policyObj;

                if (!v2rayConfig.inbounds.Exists(item => item.tag == tag))
                {
                    Inbounds apiInbound = new Inbounds();
                    Inboundsettings apiInboundSettings = new Inboundsettings();
                    apiInbound.tag = tag;
                    apiInbound.listen = Global.Loopback;
                    apiInbound.port = Global.statePort;
                    apiInbound.protocol = Global.InboundAPIProtocal;
                    apiInboundSettings.address = Global.Loopback;
                    apiInbound.settings = apiInboundSettings;
                    v2rayConfig.inbounds.Add(apiInbound);
                }

                if (!v2rayConfig.routing.rules.Exists(item => item.outboundTag == tag))
                {
                    RulesItem apiRoutingRule = new RulesItem
                    {
                        inboundTag = new List<string> { tag },
                        outboundTag = tag,
                        type = "field"
                    };
                    v2rayConfig.routing.rules.Add(apiRoutingRule);
                }
            }
            return 0;
        }

        /// <summary>
        /// 生成v2ray的客户端配置文件(自定义配置)
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        private static int GenerateClientCustomConfig(VmessItem node, string fileName, out string msg)
        {
            try
            {
                //检查GUI设置
                if (node == null)
                {
                    msg = "CheckServerSettings";
                    return -1;
                }

                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                string addressFileName = node.address;
                if (!File.Exists(addressFileName))
                {
                    addressFileName = Utils.GetConfigPath(addressFileName);
                }
                if (!File.Exists(addressFileName))
                {
                    msg = "FailedGenDefaultConfiguration";
                    return -1;
                }
                File.Copy(addressFileName, fileName);

                //check again
                if (!File.Exists(fileName))
                {
                    msg = "FailedGenDefaultConfiguration";
                    return -1;
                }

                //overwrite port
                if (node.preSocksPort <= 0)
                {
                    var fileContent = File.ReadAllLines(fileName).ToList();
                    var coreType = LazyConfig.Instance.GetCoreType(node, node.configType);
                    switch (coreType)
                    {
                        case ECoreType.v2fly:
                        case ECoreType.SagerNet:
                        case ECoreType.Xray:
                        case ECoreType.v2fly_v5:
                            break;
                        case ECoreType.clash:
                        case ECoreType.clash_meta:
                            //remove the original 
                            var indexPort = fileContent.FindIndex(t => t.Contains("port:"));
                            if (indexPort >= 0)
                            {
                                fileContent.RemoveAt(indexPort);
                            }
                            indexPort = fileContent.FindIndex(t => t.Contains("socks-port:"));
                            if (indexPort >= 0)
                            {
                                fileContent.RemoveAt(indexPort);
                            }

                            fileContent.Add($"port: {LazyConfig.Instance.GetConfig().GetLocalPort(Global.InboundHttp)}");
                            fileContent.Add($"socks-port: {LazyConfig.Instance.GetConfig().GetLocalPort(Global.InboundSocks)}");
                            break;
                    }
                    File.WriteAllLines(fileName, fileContent);
                }

                //msg = string.Format("SuccessfulConfiguration, $"[{LazyConfig.Instance.GetConfig().GetGroupRemarks(node.groupId)}] {node.GetSummary()}");
                msg = string.Format("SuccessfulConfiguration", "");
            }
            catch (Exception ex)
            {
                Utils.SaveLog("GenerateClientCustomConfig", ex);
                msg = "FailedGenDefaultConfiguration";
                return -1;
            }
            return 0;
        }

        public static int GenerateClientConfigContent(VmessItem node, bool blExport, ref V2rayConfig v2rayConfig, out string msg)
        {
            try
            {
                if (node == null)
                {
                    msg = "CheckServerSettings";
                    return -1;
                }

                msg = "InitialConfiguration";

                //取得默认配置
                string result = Utils.GetEmbedText(SampleClient);
                if (Utils.IsNullOrEmpty(result))
                {
                    msg = "FailedGetDefaultConfiguration";
                    return -1;
                }

                //转成Json
                v2rayConfig = Utils.FromJson<V2rayConfig>(result);
                if (v2rayConfig == null)
                {
                    msg = "FailedGenDefaultConfiguration";
                    return -1;
                }

                var config = LazyConfig.Instance.GetConfig();

                //开始修改配置
                log(config, ref v2rayConfig, blExport);

                //本地端口
                inbound(config, ref v2rayConfig);

                //路由
                routing(config, ref v2rayConfig);

                //outbound
                outbound(node, ref v2rayConfig);

                //dns
                dns(config, ref v2rayConfig);

                //stat
                statistic(config, ref v2rayConfig);

                //msg = string.Format("SuccessfulConfiguration, $"[{config.GetGroupRemarks(node.groupId)}] {node.GetSummary()}");
                msg = string.Format("SuccessfulConfiguration", "");
            }
            catch (Exception ex)
            {
                Utils.SaveLog("GenerateClientConfig", ex);
                msg = "FailedGenDefaultConfiguration";
                return -1;
            }
            return 0;
        }

        #endregion

        #region 生成服务端端配置

        /// <summary>
        /// 生成v2ray的客户端配置文件
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static int GenerateServerConfig(VmessItem node, string fileName, out string msg)
        {
            try
            {
                //检查GUI设置
                if (node == null)
                {
                    msg = "CheckServerSettings";
                    return -1;
                }

                msg = "InitialConfiguration";

                //取得默认配置
                string result = Utils.GetEmbedText(SampleServer);
                if (Utils.IsNullOrEmpty(result))
                {
                    msg = "FailedGetDefaultConfiguration";
                    return -1;
                }

                //转成Json
                V2rayConfig v2rayConfig = Utils.FromJson<V2rayConfig>(result);
                if (v2rayConfig == null)
                {
                    msg = "FailedGenDefaultConfiguration";
                    return -1;
                }

                var config = LazyConfig.Instance.GetConfig();

                ////开始修改配置
                log(config, ref v2rayConfig, true);

                //vmess协议服务器配置
                ServerInbound(node, ref v2rayConfig);

                //传出设置
                ServerOutbound(config, ref v2rayConfig);

                Utils.ToJsonFile(v2rayConfig, fileName, false);

                msg = string.Format("SuccessfulConfiguration", node.GetSummary());
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
                msg = "FailedGenDefaultConfiguration";
                return -1;
            }
            return 0;
        }

        /// <summary>
        /// vmess协议服务器配置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int ServerInbound(VmessItem node, ref V2rayConfig v2rayConfig)
        {
            try
            {
                Inbounds inbound = v2rayConfig.inbounds[0];
                UsersItem usersItem;
                if (inbound.settings.clients.Count <= 0)
                {
                    usersItem = new UsersItem();
                    inbound.settings.clients.Add(usersItem);
                }
                else
                {
                    usersItem = inbound.settings.clients[0];
                }
                //远程服务器端口
                inbound.port = node.port;

                //远程服务器用户ID
                usersItem.id = node.id;
                usersItem.email = Global.userEMail;

                if (node.configType == EConfigType.VMess)
                {
                    inbound.protocol = Global.vmessProtocolLite;
                    usersItem.alterId = node.alterId;

                }
                else if (node.configType == EConfigType.VLESS)
                {
                    inbound.protocol = Global.vlessProtocolLite;
                    usersItem.flow = node.flow;
                    inbound.settings.decryption = node.security;
                }

                boundStreamSettings(node, "in", inbound.streamSettings);
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }

        /// <summary>
        /// 传出设置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="v2rayConfig"></param>
        /// <returns></returns>
        private static int ServerOutbound(Config config, ref V2rayConfig v2rayConfig)
        {
            try
            {
                if (v2rayConfig.outbounds[0] != null)
                {
                    v2rayConfig.outbounds[0].settings = null;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
            }
            return 0;
        }
        #endregion

        #region 导入(导出)客户端/服务端配置


        /// <summary>
        /// 导出为客户端配置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static int Export2ClientConfig(VmessItem node, string fileName, out string msg)
        {
            V2rayConfig v2rayConfig = null;
            if (GenerateClientConfigContent(node, true, ref v2rayConfig, out msg) != 0)
            {
                return -1;
            }
            return Utils.ToJsonFile(v2rayConfig, fileName, false);
        }
        public static string GetClientConfig(VmessItem node, out string msg)
        {
            V2rayConfig v2rayConfig = null;
            if (GenerateClientConfigContent(node, true, ref v2rayConfig, out msg) != 0)
            {
                return null;
            }
            return Utils.ToJson(v2rayConfig);
        }

        /// <summary>
        /// 导出为服务端配置
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static int Export2ServerConfig(VmessItem node, string fileName, out string msg)
        {
            return GenerateServerConfig(node, fileName, out msg);
        }

        #endregion

      

    }
}
