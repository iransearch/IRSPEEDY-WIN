using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;

namespace IRSpeedyVPN.Services.Fastest
{
    internal sealed class FastestRoute
    {
        public IVPNService Service { get; set; }
        public Url Url { get; set; }
        public string Tag { get; set; }
        public int? HysteriaSocksPort { get; set; }

        public string Link => Url?.url;
    }

    internal sealed class FastestPreparedRoute
    {
        public FastestRoute Route { get; set; }
        public JObject Outbound { get; set; }
    }

    internal sealed class FastestBuildResult
    {
        public string XrayConfig { get; set; }
        public FastestRoute FallbackRoute { get; set; }
        public IReadOnlyList<FastestPreparedRoute> Routes { get; set; }
    }

    internal static class FastestConnectionConfigBuilder
    {
        internal const string ProxyPrefix = "smart-1-proxy-";
        internal const string BalancerTag = "smart-balancer-1";
        internal const string PendingFallbackTag = "smart-balancer-pending";
        private const string ObservatoryInterval = "20m";

        public static FastestBuildResult Build(
            IEnumerable<FastestRoute> routes,
            string socksAddress,
            int socksPort,
            string username)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));

            var prepared = new List<FastestPreparedRoute>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routes)
            {
                if (route == null || route.Url == null || string.IsNullOrWhiteSpace(route.Link))
                    continue;
                if (route.Url.chainproxy == 1)
                    continue;
                if (string.IsNullOrWhiteSpace(route.Tag) || !tags.Add(route.Tag))
                    continue;

                var outbound = TryCreateOutbound(route);
                if (outbound == null)
                    continue;

                outbound["tag"] = route.Tag;
                prepared.Add(new FastestPreparedRoute
                {
                    Route = route,
                    Outbound = outbound
                });
            }

            if (prepared.Count == 0)
                throw new InvalidOperationException("No compatible FASTEST CONNECTION routes were found.");

            var fallback = FastestConnectionCache.SelectFallback(prepared, username)
                ?? prepared[0].Route;

            var root = BuildRoot(
                prepared.Select(x => x.Outbound),
                fallback.Tag,
                socksAddress,
                socksPort);

            return new FastestBuildResult
            {
                XrayConfig = root.ToString(Formatting.None),
                FallbackRoute = fallback,
                Routes = prepared
            };
        }

        private static JObject BuildRoot(
            IEnumerable<JObject> proxyOutbounds,
            string fallbackTag,
            string socksAddress,
            int socksPort)
        {
            var outbounds = new JArray();
            foreach (var outbound in proxyOutbounds)
                outbounds.Add(outbound);

            outbounds.Add(DirectOutbound());
            outbounds.Add(BlockOutbound());
            outbounds.Add(PendingFallbackOutbound());
            outbounds.Add(DnsOutbound());

            return new JObject
            {
                ["burstObservatory"] = BurstObservatory(),
                ["inbounds"] = Inbounds(socksAddress, socksPort),
                ["log"] = new JObject
                {
                    ["loglevel"] = "warning"
                },
                ["outbounds"] = outbounds,
                ["remarks"] = "\uD83D\uDE80 SMART SERVER",
                ["routing"] = Routing(fallbackTag)
            };
        }

        private static JObject TryCreateOutbound(FastestRoute route)
        {
            if (route.HysteriaSocksPort.HasValue)
                return SocksOutbound(route.HysteriaSocksPort.Value);

            var stripped = StripName(route.Link);
            var separator = stripped?.IndexOf(':') ?? -1;
            if (separator <= 0)
                return null;

            var scheme = stripped.Substring(0, separator).ToLowerInvariant();
            switch (scheme)
            {
                case "vless":
                case "vmess":
                case "trojan":
                case "ss":
                case "shadowsocks":
                    return StandardOutbound(route.Link);
                case "hysteria2":
                case "hy2":
                    return null;
            }

            Uri uri;
            try
            {
                uri = new Uri(stripped);
            }
            catch
            {
                return null;
            }

            switch (scheme)
            {
                case "tuic":
                    return TuicOutbound(uri);
                case "anytls":
                    return AnyTlsOutbound(uri);
                case "ssh":
                    return SshOutbound(uri);
                default:
                    return null;
            }
        }

        private static JObject StandardOutbound(string link)
        {
            try
            {
                var config = IRSpeedyVPN.Services.Xray.ConfigGenerator.GetConfig(link, 1);
                if (string.IsNullOrWhiteSpace(config))
                    return null;

                var root = JObject.Parse(config);
                var outbound = root["outbounds"]?
                    .OfType<JObject>()
                    .FirstOrDefault(x => string.Equals((string)x["tag"], "proxy", StringComparison.OrdinalIgnoreCase))
                    ?? root["outbounds"]?.OfType<JObject>().FirstOrDefault();

                if (outbound == null)
                    return null;

                outbound = (JObject)outbound.DeepClone();
                if (!NormalizeRealityPublicKey(outbound))
                    return null;
                ForceUseIpv4(outbound);
                return outbound;
            }
            catch
            {
                return null;
            }
        }

        private static JObject SocksOutbound(int port)
        {
            return BaseOutbound("socks", new JObject
            {
                ["address"] = "127.0.0.1",
                ["port"] = port
            });
        }

        private static JObject TuicOutbound(Uri uri)
        {
            if (!ValidEndpoint(uri))
                return null;

            var query = ParseQuery(uri);
            var credentials = DecodedUserInfo(uri).Split(new[] { ':' }, 2);
            if (credentials.Length == 0 || string.IsNullOrWhiteSpace(credentials[0]))
                return null;

            var settings = new JObject
            {
                ["address"] = uri.Host,
                ["port"] = uri.Port,
                ["uuid"] = credentials[0],
                ["congestionControl"] = First(query, "congestion_control") ?? "bbr",
                ["udpRelayMode"] = First(query, "udp_relay_mode") ?? "native"
            };

            if (credentials.Length == 2 && !string.IsNullOrWhiteSpace(credentials[1]))
                settings["password"] = credentials[1];
            if (Truthy(First(query, "zero_rtt_handshake", "zero-rtt", "0rtt")))
                settings["zeroRTTHandshake"] = true;

            var outbound = BaseOutbound("tuic", settings);
            var stream = TlsStream(query, uri.Host);
            ForceUseIpv4(stream);
            outbound["streamSettings"] = stream;
            return outbound;
        }

        private static JObject AnyTlsOutbound(Uri uri)
        {
            if (!ValidEndpoint(uri))
                return null;

            var query = ParseQuery(uri);
            var settings = new JObject
            {
                ["address"] = uri.Host,
                ["port"] = uri.Port,
                ["password"] = DecodedUserInfo(uri)
            };

            var outbound = BaseOutbound("anytls", settings);
            var stream = TlsStream(query, uri.Host);
            ForceUseIpv4(stream);
            outbound["streamSettings"] = stream;
            return outbound;
        }

        private static JObject SshOutbound(Uri uri)
        {
            if (!ValidEndpoint(uri))
                return null;

            var query = ParseQuery(uri);
            return BaseOutbound("ssh", new JObject
            {
                ["address"] = uri.Host,
                ["port"] = uri.Port,
                ["user"] = First(query, "u") ?? "root",
                ["password"] = DecodedUserInfo(uri),
                ["domainStrategy"] = "UseIPv4"
            });
        }

        private static JObject TlsStream(NameValueCollection query, string defaultServerName)
        {
            var tls = new JObject();
            var disableSni = Truthy(First(query, "disable_sni"));
            var sni = First(query, "sni", "peer");
            if (string.IsNullOrWhiteSpace(sni) && !disableSni)
                sni = defaultServerName;
            if (!string.IsNullOrWhiteSpace(sni) && !disableSni)
                tls["serverName"] = sni;
            if (Truthy(First(query, "insecure", "allowInsecure")))
                tls["allowInsecure"] = true;

            var alpn = First(query, "alpn");
            if (!string.IsNullOrWhiteSpace(alpn))
            {
                var values = new JArray(
                    alpn.Split(',')
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0));
                if (values.Count > 0)
                    tls["alpn"] = values;
            }

            return new JObject
            {
                ["security"] = "tls",
                ["tlsSettings"] = tls
            };
        }

        private static bool NormalizeRealityPublicKey(JObject outbound)
        {
            var stream = outbound["streamSettings"] as JObject;
            if (stream == null || !string.Equals((string)stream["security"], "reality", StringComparison.OrdinalIgnoreCase))
                return true;

            var reality = stream["realitySettings"] as JObject;
            if (reality == null)
                return false;

            var publicKey = (string)reality["publicKey"];
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                publicKey = (string)reality["password"];
                if (!string.IsNullOrWhiteSpace(publicKey))
                    reality["publicKey"] = publicKey;
            }
            reality.Remove("password");
            return !string.IsNullOrWhiteSpace(publicKey);
        }

        private static void ForceUseIpv4(JObject outboundOrStream)
        {
            var stream = outboundOrStream["streamSettings"] as JObject;
            if (stream == null)
            {
                if (outboundOrStream["security"] != null || outboundOrStream["tlsSettings"] != null)
                    stream = outboundOrStream;
                else
                {
                    stream = new JObject();
                    outboundOrStream["streamSettings"] = stream;
                }
            }

            var sockopt = stream["sockopt"] as JObject ?? new JObject();
            sockopt["domainStrategy"] = "UseIPv4";
            sockopt["tcpNoDelay"] = true;
            stream["sockopt"] = sockopt;
        }

        private static JObject BurstObservatory()
        {
            return new JObject
            {
                ["pingConfig"] = new JObject
                {
                    ["connectivity"] = "",
                    ["destination"] = "https://connectivitycheck.gstatic.com/generate_204",
                    ["httpMethod"] = "HEAD",
                    ["interval"] = ObservatoryInterval,
                    ["sampling"] = 5,
                    ["timeout"] = "5s"
                },
                ["subjectSelector"] = new JArray(ProxyPrefix)
            };
        }

        private static JArray Inbounds(string address, int port)
        {
            return new JArray
            {
                new JObject
                {
                    ["listen"] = address,
                    ["port"] = port,
                    ["protocol"] = "socks",
                    ["settings"] = new JObject
                    {
                        ["auth"] = "noauth",
                        ["udp"] = true
                    },
                    ["sniffing"] = new JObject
                    {
                        ["destOverride"] = new JArray("http", "tls", "quic"),
                        ["enabled"] = true,
                        ["metadataOnly"] = false,
                        ["routeOnly"] = true
                    },
                    ["tag"] = "socks"
                }
            };
        }

        private static JObject Routing(string fallbackTag)
        {
            var rules = new JArray
            {
                new JObject
                {
                    ["network"] = "udp",
                    ["outboundTag"] = "block",
                    ["port"] = "443",
                    ["type"] = "field"
                },
                new JObject
                {
                    ["balancerTag"] = BalancerTag,
                    ["domain"] = new JArray(
                        "domain:api.ipify.org",
                        "domain:ipify.org",
                        "domain:ipinfo.io",
                        "domain:ip-api.com",
                        "domain:ipwho.is",
                        "domain:ifconfig.me",
                        "domain:icanhazip.com",
                        "domain:ip.sb"),
                    ["type"] = "field"
                },
                IpRule("geoip:private", "direct"),
                DomainRule("geosite:private", "direct"),
                IpRule("geoip:ir", "direct"),
                DomainRule("geosite:category-ir", "direct"),
                new JObject
                {
                    ["balancerTag"] = BalancerTag,
                    ["network"] = "tcp,udp",
                    ["type"] = "field"
                }
            };

            return new JObject
            {
                ["balancers"] = new JArray
                {
                    new JObject
                    {
                        ["fallbackTag"] = fallbackTag,
                        ["selector"] = new JArray(ProxyPrefix),
                        ["strategy"] = new JObject
                        {
                            ["settings"] = new JObject
                            {
                                ["expected"] = 3,
                                ["maxRTT"] = "3s",
                                ["tolerance"] = 0.2
                            },
                            ["type"] = "leastLoad"
                        },
                        ["tag"] = BalancerTag
                    }
                },
                ["domainStrategy"] = "AsIs",
                ["rules"] = rules
            };
        }

        private static JObject IpRule(string value, string outboundTag)
        {
            return new JObject
            {
                ["ip"] = new JArray(value),
                ["outboundTag"] = outboundTag,
                ["type"] = "field"
            };
        }

        private static JObject DomainRule(string value, string outboundTag)
        {
            return new JObject
            {
                ["domain"] = new JArray(value),
                ["outboundTag"] = outboundTag,
                ["type"] = "field"
            };
        }

        private static JObject DirectOutbound()
        {
            return BaseOutbound("freedom", new JObject
            {
                ["domainStrategy"] = "UseIPv4"
            }, "direct");
        }

        private static JObject BlockOutbound()
        {
            return BaseOutbound("blackhole", new JObject
            {
                ["response"] = new JObject
                {
                    ["type"] = "http"
                }
            }, "block");
        }

        private static JObject PendingFallbackOutbound()
        {
            return BaseOutbound("blackhole", new JObject(), PendingFallbackTag);
        }

        private static JObject DnsOutbound()
        {
            return new JObject
            {
                ["protocol"] = "dns",
                ["tag"] = "dns-out"
            };
        }

        private static JObject BaseOutbound(string protocol, JObject settings, string tag = null)
        {
            var outbound = new JObject
            {
                ["protocol"] = protocol,
                ["settings"] = settings
            };
            if (!string.IsNullOrWhiteSpace(tag))
                outbound["tag"] = tag;
            return outbound;
        }

        private static bool ValidEndpoint(Uri uri)
        {
            return uri != null && !string.IsNullOrWhiteSpace(uri.Host) && uri.Port > 0;
        }

        private static string StripName(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return link;
            var index = link.IndexOf('#');
            return index < 0 ? link : link.Substring(0, index);
        }

        private static string DecodedUserInfo(Uri uri)
        {
            return Uri.UnescapeDataString(uri?.UserInfo ?? string.Empty);
        }

        private static NameValueCollection ParseQuery(Uri uri)
        {
            return HttpUtility.ParseQueryString((uri?.Query ?? string.Empty).TrimStart('?'));
        }

        private static string First(NameValueCollection values, params string[] keys)
        {
            if (values == null || keys == null)
                return null;
            foreach (var key in keys)
            {
                var value = values[key];
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return null;
        }

        private static bool Truthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
