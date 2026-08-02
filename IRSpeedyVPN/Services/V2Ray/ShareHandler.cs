using IRSpeedyVPN.Services.SingBox;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using v2rayN.Base;
using v2rayN.Mode;
namespace v2rayN.Handler
{
    class ShareHandler
    {



        #region  ImportShareUrl 


        /// <summary>
        /// 从剪贴板导入URL
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static VmessItem ImportFromConfigLink(string configLink, out string msg)
        {
            msg = string.Empty;
            VmessItem vmessItem = new VmessItem();
            configLink= configLink.Replace("%3A",":");
            try
            {
                //载入配置文件 
                string result = configLink.TrimEx();// Utils.GetClipboardData();
                if (Utils.IsNullOrEmpty(result))
                {
                    msg = "FailedReadConfiguration";
                    return null;
                }
                if (result.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
                {
                    result = Global.socksProtocol + result.Substring("socks5://".Length);
                }

                if (result.StartsWith(Global.vmessProtocol))
                {
                    int indexSplit = result.IndexOf("?");
                    if (indexSplit > 0)
                    {
                        vmessItem = ResolveStdVmess(result) ?? ResolveVmess4Kitsunebi(result);
                    }
                    else
                    {
                        vmessItem = ResolveVmess(result, out msg);
                    }

                    ConfigHandler.UpgradeServerVersion(ref vmessItem);
                }
                else if (result.StartsWith(Global.ssProtocol))
                {
                    msg = "ConfigurationFormatIncorrect";

                    vmessItem = ResolveSSLegacy(result) ?? ResolveSip002(result);
                    if (vmessItem == null)
                    {
                        return null;
                    }
                    if (vmessItem.address.Length == 0 || vmessItem.port == 0 || vmessItem.security.Length == 0 || vmessItem.id.Length == 0)
                    {
                        return null;
                    }

                    vmessItem.configType = EConfigType.Shadowsocks;
                }
                else if (result.StartsWith(Global.ssrProtocol))
                {
                    msg = "ConfigurationFormatIncorrect";

                    vmessItem = ResolveSsr(result, null);
                    if (vmessItem == null)
                    {
                        return null;
                    }
                    if (vmessItem.address.Length == 0 || vmessItem.port == 0 || vmessItem.security.Length == 0 || vmessItem.id.Length == 0)
                    {
                        return null;
                    }

                    vmessItem.configType = EConfigType.Shadowsocksr;
                }
                else if (result.StartsWith(Global.socksProtocol))
                {
                    msg = "ConfigurationFormatIncorrect";

                    vmessItem = ResolveSocksNew(result) ?? ResolveSocks(result);
                    if (vmessItem == null)
                    {
                        return null;
                    }
                    if (vmessItem.address.Length == 0 || vmessItem.port == 0)
                    {
                        return null;
                    }

                    vmessItem.configType = EConfigType.Socks;
                }
                else if (result.StartsWith(Global.httpProtocol, StringComparison.OrdinalIgnoreCase))
                {
                    msg = "ConfigurationFormatIncorrect";
                    vmessItem = ResolveHttpProxy(result);
                    if (vmessItem == null || vmessItem.address.Length == 0 || vmessItem.port == 0)
                    {
                        return null;
                    }
                }
                else if (result.StartsWith(Global.trojanProtocol))
                {

                    vmessItem = ResolveTrojan(result);
                }
                else if (result.StartsWith(Global.wireguardProtocol) || result.StartsWith(Global.wireguardFullProtocol))
                {

                    vmessItem = ResolveWireGaurd(result);
                }
                else if (result.StartsWith(Global.vlessProtocol))
                {
                    vmessItem = ResolveStdVLESS(result);

                    ConfigHandler.UpgradeServerVersion(ref vmessItem);
                }
                else if (result.StartsWith(Global.sshProtocol))
                {

                    vmessItem = ResolveSSH(result);
                }
                else if (result.StartsWith(Global.Hysteria2ProtocolLite) || result.StartsWith(Global.Hysteria2Protocol))
                {
                    vmessItem = ResolveHysteria2(result);
                }
                else if (result.StartsWith(Global.tuicProtocol))
                {
                    vmessItem = ResolveTuic(result);
                }
                else
                {
                    msg = "NonvmessOrssProtocol";
                    return null;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
                msg = "Incorrectconfiguration";
                return null;
            }

            return vmessItem;
        }

        private static VmessItem ResolveHttpProxy(string result)
        {
            Uri url;
            try
            {
                url = new Uri(result);
            }
            catch (UriFormatException)
            {
                return null;
            }

            if (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Keep HTTP proxy parsing strict so plain web URLs are not mistaken as proxy nodes.
            if (!string.IsNullOrEmpty(url.AbsolutePath) && url.AbsolutePath != "/")
            {
                return null;
            }

            var item = new VmessItem
            {
                configType = EConfigType.Http,
                address = url.DnsSafeHost,
                port = url.Port > 0 ? url.Port : 80,
                remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped)
            };

            var userInfo = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
            if (!string.IsNullOrWhiteSpace(userInfo))
            {
                var parts = userInfo.Split(new[] { ':' }, 2);
                item.security = parts[0];
                if (parts.Length > 1)
                {
                    item.id = parts[1];
                }
            }

            return item;
        }

        private static VmessItem ResolveSSH(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.SSH
            };

            Uri url = new Uri(result);

            item.address = url.DnsSafeHost;
            item.port = url.Port > 0 ? url.Port : 22;
            item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
            var query = HttpUtility.ParseQueryString(url.Query.Replace("&amp;", "&"));
            item.username = string.IsNullOrEmpty(query["u"]) ? "root" : query["u"];
            //item.password = query["p"];            
            item.password = url.UserInfo;
            return item;
        }

        private static readonly Regex Hysteria2ShareLinkRegex = new Regex(
            @"^(?:hy2|hysteria2)://(?:(?<userinfo>[^@/?#]*)@)?(?<host>\[[^\]]+\]|[^:/?#@]+):(?<port>[^/?#]+)(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryParseHysteria2ShareLink(string result, out string host, out int port, out int portEnd, out string password, out string remarks, out string query)
        {
            host = null;
            port = 0;
            portEnd = 0;
            password = null;
            remarks = null;
            query = null;

            var match = Hysteria2ShareLinkRegex.Match(result.TrimEx());
            if (!match.Success)
                return false;

            host = match.Groups["host"].Value;
            if (host.StartsWith("[") && host.EndsWith("]"))
                host = host.Substring(1, host.Length - 2);

            password = match.Groups["userinfo"].Value;

            var rest = match.Groups["rest"].Value;
            var hashIndex = rest.IndexOf('#');
            if (hashIndex >= 0)
            {
                remarks = Uri.UnescapeDataString(rest.Substring(hashIndex + 1));
                rest = rest.Substring(0, hashIndex);
            }
            if (rest.StartsWith("?"))
                query = rest;

            // Get port spec from URL or mport parameter
            var portSpec = match.Groups["port"].Value;
            if (!string.IsNullOrEmpty(query))
            {
                var mport = System.Web.HttpUtility.ParseQueryString(query)["mport"];
                if (!string.IsNullOrEmpty(mport))
                    portSpec = mport;
            }

            // Parse port range (same logic for both URL port and mport)
            var dashIndex = portSpec.IndexOf('-');
            if (dashIndex > 0)
            {
                if (!int.TryParse(portSpec.Substring(0, dashIndex), out port) ||
                    !int.TryParse(portSpec.Substring(dashIndex + 1), out portEnd) ||
                    port <= 0 || portEnd <= 0 || portEnd < port)
                {
                    return false;
                }
            }
            else if (!int.TryParse(portSpec, out port) || port <= 0)
            {
                port = 443;
            }

            return !Utils.IsNullOrEmpty(host);
        }

        private static VmessItem ResolveHysteria2(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.Hysteria2,
                streamSecurity = Global.StreamSecurity
            };

            if (!TryParseHysteria2ShareLink(result, out var host, out var port, out var portEnd, out var password, out var remarks, out var query))
                return null;

            item.address = host;
            item.port = port;
            item.portEnd = portEnd;
            item.remarks = remarks ?? "";
            item.password = password;

            var queryParams = HttpUtility.ParseQueryString((query ?? "").Replace("&amp;", "&"));
            var obfsPassword = queryParams["obfs-password"];
            var obfs = queryParams["obfs"];
            if (!Utils.IsNullOrEmpty(obfsPassword))
            {
                item.obfs = obfs ?? "salamander";
                item.obfs_param = obfsPassword;
            }

            var security = queryParams["security"];
            if (!Utils.IsNullOrEmpty(security))
                item.streamSecurity = security;

            item.sni = queryParams["sni"] ?? "";
            item.allowInsecure =queryParams.GetValues("allowInsecure")?.FirstOrDefault()?? queryParams.GetValues("insecure")?.FirstOrDefault();

            return item;
        }

        private static VmessItem ResolveTuic(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.Tuic,
                streamSecurity = Global.StreamSecurity
            };

            Uri url = new Uri(result);
            item.address = url.DnsSafeHost;
            item.port = url.Port > 0 ? url.Port : 443;
            item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);

            if (!string.IsNullOrWhiteSpace(url.UserInfo))
            {
                var parts = url.UserInfo.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    item.id = parts[0];
                    item.password = parts[1];
                }
            }

            var query = HttpUtility.ParseQueryString(url.Query.Replace("&amp;", "&"));
            item.sni = query["sni"] ?? "";
            item.allowInsecure = query["allow_insecure"] ?? query["allowInsecure"];

            var alpnRaw = query["alpn"];
            if (!Utils.IsNullOrEmpty(alpnRaw))
                item.alpn = Utils.String2List(Utils.UrlDecode(alpnRaw));

            item.congestionControl = query["congestion_control"];
            item.udpRelayMode = query["udp_relay_mode"];

            return item;
        }

        private static VmessItem ResolveWireGaurd(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.WireGaurd
            };

           
            if (result.StartsWith(Global.wireguardProtocol))
            {
                Uri url = new Uri(result);
                item.address = url.DnsSafeHost;
                item.port = url.Port > 0 ? url.Port : 80;
                item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
                var query = HttpUtility.ParseQueryString(url.Query.Replace("&amp;", "&"));
                item.privateKey = query["priv"];
                item.publicKey = query["pub"];
                item.preSharedKey = query["psk"];
                item.mtu = query["mtu"];
                item.ipList = string.IsNullOrEmpty(query["lp"]) ? null : query["lp"].Split(',').Select(x => x.Trim()).ToArray();
            }
            else
            {
                result = result.Substring(Global.wireguardFullProtocol.Length);
                var wg=Utils.FromJson<WireGuardUnformatted>(Utils.Base64Decode(result));
                item.address = wg.server;
                item.port = wg.server_port;
                // item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);

                item.privateKey = wg.private_key;
                item.publicKey = wg.peer_public_key;
                item.preSharedKey = wg.pre_shared_key;
                item.mtu = wg.mtu;
                item.ipList = wg.local_address.ToArray();
            }

            return item;
        }

        private static VmessItem ResolveVmess(string result, out string msg)
        {
            msg = string.Empty;
            var vmessItem = new VmessItem
            {
                configType = EConfigType.VMess
            };

            result = result.Substring(Global.vmessProtocol.Length);
            result = Utils.Base64Decode(result);

            //转成Json
            VmessQRCode vmessQRCode = Utils.FromJson<VmessQRCode>(result);
            if (vmessQRCode == null)
            {
                msg = "FailedConversionConfiguration";
                return null;
            }

            vmessItem.network = Global.DefaultNetwork;
            vmessItem.headerType = Global.None;

            vmessItem.configVersion = Utils.ToInt(vmessQRCode.v);
            vmessItem.remarks = Utils.ToString(vmessQRCode.ps);
            vmessItem.address = Utils.ToString(vmessQRCode.add);
            vmessItem.port = Utils.ToInt(vmessQRCode.port);
            vmessItem.id = Utils.ToString(vmessQRCode.id);
            vmessItem.alterId = Utils.ToInt(vmessQRCode.aid);
            vmessItem.security = Utils.ToString(vmessQRCode.scy);

            vmessItem.security = !Utils.IsNullOrEmpty(vmessQRCode.scy) ? vmessQRCode.scy : Global.DefaultSecurity;
            if (!Utils.IsNullOrEmpty(vmessQRCode.net))
            {
                vmessItem.network = vmessQRCode.net;
            }
            if (!Utils.IsNullOrEmpty(vmessQRCode.type))
            {
                vmessItem.headerType = vmessQRCode.type;
            }

            vmessItem.requestHost = Utils.ToString(vmessQRCode.host);
            vmessItem.path = Utils.ToString(vmessQRCode.path);
            vmessItem.streamSecurity = Utils.ToString(vmessQRCode.tls);
            vmessItem.sni = Utils.ToString(vmessQRCode.sni);
            vmessItem.alpn = Utils.String2List(vmessQRCode.alpn);

            return vmessItem;
        }

        private static VmessItem ResolveVmess4Kitsunebi(string result)
        {
            VmessItem vmessItem = new VmessItem
            {
                configType = EConfigType.VMess
            };
            result = result.Substring(Global.vmessProtocol.Length);
            int indexSplit = result.IndexOf("?");
            if (indexSplit > 0)
            {
                result = result.Substring(0, indexSplit);
            }
            result = Utils.Base64Decode(result);

            string[] arr1 = result.Split('@');
            if (arr1.Length != 2)
            {
                return null;
            }
            string[] arr21 = arr1[0].Split(':');
            string[] arr22 = arr1[1].Split(':');
            if (arr21.Length != 2 || arr21.Length != 2)
            {
                return null;
            }

            vmessItem.address = arr22[0];
            vmessItem.port = Utils.ToInt(arr22[1]);
            vmessItem.security = arr21[0];
            vmessItem.id = arr21[1];

            vmessItem.network = Global.DefaultNetwork;
            vmessItem.headerType = Global.None;
            vmessItem.remarks = "Alien";

            return vmessItem;
        }

        private static VmessItem ResolveStdVmess(string result)
        {
            VmessItem i = new VmessItem
            {
                configType = EConfigType.VMess,
                security = "auto"
            };

            Uri u = new Uri(result);

            i.address = u.DnsSafeHost;
            i.port = u.Port;
            i.remarks = u.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
            var q = HttpUtility.ParseQueryString(u.Query.Replace("&amp;", "&"));

            var m = StdVmessUserInfo.Match(u.UserInfo);
            if (!m.Success) return null;

            i.id = m.Groups["id"].Value;

            if (m.Groups["streamSecurity"].Success)
            {
                i.streamSecurity = m.Groups["streamSecurity"].Value;
            }
            switch (i.streamSecurity)
            {
                case "tls":
                    // TODO tls config
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(i.streamSecurity))
                        return null;
                    break;
            }

            i.network = m.Groups["network"].Value;
            switch (i.network)
            {
                case "tcp":
                    string t1 = q["type"] ?? "none";
                    i.headerType = t1;
                    // TODO http option

                    break;
                case "kcp":
                    i.headerType = q["type"] ?? "none";
                    // TODO kcp seed
                    break;

                case "ws":
                    string p1 = q["path"] ?? "/";
                    string h1 = q["host"] ?? "";
                    i.requestHost = Utils.UrlDecode(h1);
                    i.path = p1;
                    break;

                case "http":
                case "h2":
                    i.network = "h2";
                    string p2 = q["path"] ?? "/";
                    string h2 = q["host"] ?? "";
                    i.requestHost = Utils.UrlDecode(h2);
                    i.path = p2;
                    break;

                case "quic":
                    string s = q["security"] ?? "none";
                    string k = q["key"] ?? "";
                    string t3 = q["type"] ?? "none";
                    i.headerType = t3;
                    i.requestHost = Utils.UrlDecode(s);
                    i.path = k;
                    break;

                default:
                    return null;
            }

            return i;
        }

        private static VmessItem ResolveSip002(string result)
        {
            Uri parsedUrl;
            try
            {
                parsedUrl = new Uri(result);
            }
            catch (UriFormatException)
            {
                return null;
            }
            VmessItem server = new VmessItem
            {
                remarks = parsedUrl.GetComponents(UriComponents.Fragment, UriFormat.Unescaped),
                address = parsedUrl.DnsSafeHost,
                port = parsedUrl.Port,
            };
            string rawUserInfo = parsedUrl.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped);
            //2022-blake3
            if (rawUserInfo.Contains(":"))
            {
                string[] userInfoParts = rawUserInfo.Split(new[] { ':' }, 2);
                if (userInfoParts.Length != 2)
                {
                    return null;
                }
                server.security = userInfoParts[0];
                server.id = Utils.UrlDecode(userInfoParts[1]);
            }
            else
            {
                // parse base64 UserInfo
                string userInfo = Utils.Base64Decode(rawUserInfo);
                string[] userInfoParts = userInfo.Split(new[] { ':' }, 2);
                if (userInfoParts.Length != 2)
                {
                    return null;
                }
                server.security = userInfoParts[0];
                server.id = userInfoParts[1];
            }

            NameValueCollection queryParameters = HttpUtility.ParseQueryString(parsedUrl.Query);
            if (queryParameters["plugin"] != null)
            {
                //obfs-host exists
                var obfsHost = queryParameters["plugin"].Split(';').FirstOrDefault(t => t.Contains("obfs-host"));
                if (queryParameters["plugin"].Contains("obfs=http") && !Utils.IsNullOrEmpty(obfsHost))
                {
                    obfsHost = obfsHost.Replace("obfs-host=", "");
                    server.network = Global.DefaultNetwork;
                    server.headerType = Global.TcpHeaderHttp;
                    server.requestHost = obfsHost;
                }
                else
                {
                    return null;
                }
            }

            return server;
        }

        private static readonly Regex UrlFinder = new Regex(@"ss://(?<base64>[A-Za-z0-9+-/=_]+)(?:#(?<tag>\S+))?", RegexOptions.IgnoreCase);
        private static readonly Regex DetailsParser = new Regex(@"^((?<method>.+?):(?<password>.*)@(?<hostname>.+?):(?<port>\d+?))$", RegexOptions.IgnoreCase);
        private static VmessItem ResolveSsr(string ssrURL, string force_group)
        {
            // ssr://host:port:protocol:method:obfs:base64pass/?obfsparam=base64&remarks=base64&group=base64&udpport=0&uot=1
            Match ssr = Regex.Match(ssrURL, "ssr://([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
            if (!ssr.Success)
                throw new FormatException();

            string data = Utils.DecodeUrlSafeBase64(ssr.Groups[1].Value);
            Dictionary<string, string> params_dict = new Dictionary<string, string>();

            Match match = null;

            int param_start_pos = data.IndexOf("?");
            if (param_start_pos > 0)
            {
                params_dict = Utils.ParseParam(data.Substring(param_start_pos + 1));
                data = data.Substring(0, param_start_pos);
            }
            if (data.IndexOf("/") >= 0)
            {
                data = data.Substring(0, data.LastIndexOf("/"));
            }

            Regex UrlFinder = new Regex("^(.+):([^:]+):([^:]*):([^:]+):([^:]*):([^:]+)");
            match = UrlFinder.Match(data);

            if (match == null || !match.Success)
                throw new FormatException();
            VmessItem item =new VmessItem();
            item.address = match.Groups[1].Value;
            item.port = ushort.Parse(match.Groups[2].Value);
            item.protocol = match.Groups[3].Value.Length == 0 ? "origin" : match.Groups[3].Value;
            item.protocol = item.protocol.Replace("_compatible", "");
            item.security = match.Groups[4].Value;
            item.obfs = match.Groups[5].Value.Length == 0 ? "plain" : match.Groups[5].Value;
            item.obfs = item.obfs.Replace("_compatible", "");
            item.id = Utils.DecodeStandardSSRUrlSafeBase64(match.Groups[6].Value);

            if (params_dict.ContainsKey("protoparam"))
            {
                item.protocol_param = Utils.DecodeStandardSSRUrlSafeBase64(params_dict["protoparam"]);
            }
            if (params_dict.ContainsKey("obfsparam"))
            {
                item.obfs_param = Utils.DecodeStandardSSRUrlSafeBase64(params_dict["obfsparam"]);
            }
            if (params_dict.ContainsKey("remarks"))
            {
                item.remarks = Utils.DecodeStandardSSRUrlSafeBase64(params_dict["remarks"]);
            }
            if (params_dict.ContainsKey("group"))
            {
                //group = Util.Base64.DecodeStandardSSRUrlSafeBase64(params_dict["group"]);
            }
            else
            {
             //   group = "";
            }
            if (params_dict.ContainsKey("uot"))
            {
                item.udp_over_tcp = int.Parse(params_dict["uot"]) != 0;
            }
            if (params_dict.ContainsKey("udpport"))
            {
                //server_udp_port = ushort.Parse(params_dict["udpport"]);
            }
            //if (!String.IsNullOrEmpty(force_group))
            //   group = force_group;
            return item;
        }
        private static VmessItem ResolveSSLegacy(string result)
        {
            var match = UrlFinder.Match(result);
            if (!match.Success)
                return null;

            VmessItem server = new VmessItem();
            var base64 = match.Groups["base64"].Value.TrimEnd('/');
            var tag = match.Groups["tag"].Value;
            if (!Utils.IsNullOrEmpty(tag))
            {
                server.remarks = Utils.UrlDecode(tag);
            }
            Match details;
            try
            {
                details = DetailsParser.Match(Utils.Base64Decode(base64));
            }
            catch (FormatException)
            {
                return null;
            }
            if (!details.Success)
                return null;
            server.security = details.Groups["method"].Value;
            server.id = details.Groups["password"].Value;
            server.address = details.Groups["hostname"].Value;
            server.port = int.Parse(details.Groups["port"].Value);
            return server;
        }


        private static readonly Regex StdVmessUserInfo = new Regex(
            @"^(?<network>[a-z]+)(\+(?<streamSecurity>[a-z]+))?:(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$");

        private static VmessItem ResolveSocks(string result)
        {
            VmessItem vmessItem = new VmessItem
            {
                configType = EConfigType.Socks
            };
            result = result.Substring(Global.socksProtocol.Length);
            //remark
            int indexRemark = result.IndexOf("#");
            if (indexRemark > 0)
            {
                try
                {
                    vmessItem.remarks = Utils.UrlDecode(result.Substring(indexRemark + 1, result.Length - indexRemark - 1));
                }
                catch { }
                result = result.Substring(0, indexRemark);
            }
            //part decode
            int indexS = result.IndexOf("@");
            if (indexS > 0)
            {
            }
            else
            {
                result = Utils.Base64Decode(result);
            }

            string[] arr1 = result.Split('@');
            if (arr1.Length != 2)
            {
                return null;
            }
            string[] arr21 = arr1[0].Split(':');
            //string[] arr22 = arr1[1].Split(':');
            int indexPort = arr1[1].LastIndexOf(":");
            if (arr21.Length != 2 || indexPort < 0)
            {
                return null;
            }
            vmessItem.address = arr1[1].Substring(0, indexPort);
            vmessItem.port = Utils.ToInt(arr1[1].Substring(indexPort + 1, arr1[1].Length - (indexPort + 1)));
            vmessItem.security = arr21[0];
            vmessItem.id = arr21[1];

            return vmessItem;
        }

        private static VmessItem ResolveSocksNew(string result)
        {
            Uri parsedUrl;
            try
            {
                parsedUrl = new Uri(result);
            }
            catch (UriFormatException)
            {
                return null;
            }
            VmessItem server = new VmessItem
            {
                remarks = parsedUrl.GetComponents(UriComponents.Fragment, UriFormat.Unescaped),
                address = parsedUrl.DnsSafeHost,
                port = parsedUrl.Port,
            };

            // parse base64 UserInfo
            string rawUserInfo = parsedUrl.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
            string userInfo = rawUserInfo.Contains(":") ? rawUserInfo : Utils.Base64Decode(rawUserInfo);
            string[] userInfoParts = userInfo.Split(new[] { ':' }, 2);
            if (userInfoParts.Length == 2)
            {
                server.security = userInfoParts[0];
                server.id = userInfoParts[1];
            }
            var query = HttpUtility.ParseQueryString(parsedUrl.Query.Replace("&amp;", "&"));
            server.hostOverride = query["host"];
            return server;
        }

        private static VmessItem ResolveTrojan(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.Trojan
            };

            Uri url = new Uri(result);

            item.address = url.DnsSafeHost;
            item.port = url.Port;
            item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
            item.id = url.UserInfo;

            var query = HttpUtility.ParseQueryString(url.Query.Replace("&amp;", "&"));
            ResolveStdTransport(query, ref item);

            return item;
        }
        private static VmessItem ResolveStdVLESS(string result)
        {
            VmessItem item = new VmessItem
            {
                configType = EConfigType.VLESS,
                security = "none"
            };

            Uri url = new Uri(result);

            item.address = url.DnsSafeHost;
            item.port = url.Port;
            item.remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
            item.id = url.UserInfo;

            var query = HttpUtility.ParseQueryString(url.Query.Replace("&amp;", "&"));
            item.security = query["encryption"] ?? "none";
            item.streamSecurity = query["security"] ?? "";
            ResolveStdTransport(query, ref item);

            return item;
        }

        private static int ResolveStdTransport(NameValueCollection query, ref VmessItem item)
        {
            item.flow = query["flow"] ?? "";
            item.streamSecurity = query["security"] ?? "";
            item.sni = query["sni"] ?? "";
            item.alpn = Utils.String2List(Utils.UrlDecode(query["alpn"] ?? ""));
            item.allowInsecure = query["allowInsecure"] ?? query["insecure"];
            item.network = query["type"] ?? "tcp";
            item.packetEncoding = query["packetEncoding"] ?? "";
            item.fingerPrint = query["fp"];
            item.publicKey= query["pbk"];
            item.shortId= query["sid"];
            item.spiderX = Utils.UrlDecode(query["spx"] ?? "");
            item.certSha256 = query["pcs"];
            switch (item.network)
            {
                case "tcp":
                    item.headerType = query["headerType"] ?? "none";
                    item.requestHost = Utils.UrlDecode(query["host"] ?? "");

                    break;
                case "kcp":
                    item.headerType = query["headerType"] ?? "none";
                    item.path = Utils.UrlDecode(query["seed"] ?? "");
                    break;

                case "ws":
                    item.requestHost = Utils.UrlDecode(query["host"] ?? "");
                    item.path = Utils.UrlDecode(query["path"] ?? "/");
                    break;

                case "http":
                case "h2":
                    item.network = "h2";
                    item.requestHost = Utils.UrlDecode(query["host"] ?? "");
                    item.path = Utils.UrlDecode(query["path"] ?? "/");
                    break;

                case "quic":
                    item.headerType = query["headerType"] ?? "none";
                    item.requestHost = query["quicSecurity"] ?? "none";
                    item.path = Utils.UrlDecode(query["key"] ?? "");
                    break;
                case "grpc":
                    item.path = Utils.UrlDecode(query["serviceName"] ?? "");
                    item.headerType = Utils.UrlDecode(query["mode"] ?? Global.GrpcgunMode);
                    break;
                case "xhttp":
                    item.requestHost = Utils.UrlDecode(query["host"] ?? "");
                    item.path = Utils.UrlDecode(query["path"] ?? "/");
                    item.headerType = Utils.UrlDecode(query["mode"] ?? "auto");
                    if (!Utils.IsNullOrEmpty(query["extra"]))
                        item.transportExtra = Utils.UrlDecode(query["extra"]);
                    break;
                case "httpupgrade":
                    item.requestHost = Utils.UrlDecode(query["host"] ?? "");
                    item.path = Utils.UrlDecode(query["path"] ?? "/");
                    break;
                default:
                    break;
            }
            return 0;
        }

        #endregion
    }
}
