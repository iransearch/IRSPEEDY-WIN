using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.SingBox
{
	class Samples
	{
		public static string sg_clientSample = @"{
    ""dns"":{
        ""rules"":[
		{
				""action"": ""predefined"",
				""answer"": ""localhost. IN A 127.0.0.1"",
				""domain"": ""localhost"",
				""query_type"": ""A"",
				""rcode"": ""NOERROR""
			},
			{
				""action"": ""predefined"",
				""answer"": ""localhost. IN AAAA ::1"",
				""domain"": ""localhost"",
				""query_type"": ""AAAA"",
				""rcode"": ""NOERROR""
			}
],
        ""servers"":[
            {
                ""address"":""1.1.1.1"",
                ""address_resolver"":""dns-local"",
                ""detour"":""proxy"",
                ""tag"":""dns-remote"",
                ""strategy"": ""prefer_ipv4""
            },
            {
                ""address"":""1.1.1.1"",
                ""detour"":""direct"",
                ""tag"":""dns-direct""
            },
            {
                ""address"":""rcode://success"",
                ""tag"":""dns-block"",
                ""strategy"": ""prefer_ipv4""
            },
            {
                ""address"":""1.1.1.1"",
                ""detour"":""direct"",
                ""tag"":""dns-local"",
                ""strategy"": ""prefer_ipv4""

            }
        ]
    },    
    ""log"":{
        ""level"":""info""
    },
    ""outbounds"":[
        {            
            ""tag"":""proxy""           
        },
       
        {
            ""tag"":""direct"",
            ""type"":""direct""
        },
        {
          ""tag"": ""block"",
          ""type"": ""block""
        }

    ],
    ""route"":{
        ""auto_detect_interface"":true,
        ""final"":""proxy"",
        ""find_process"":false,
        ""rules"":[
            {
                ""inbound"":[
                    ""mixed-in"",
                    ""tun-in""
                ],
                ""action"":""sniff""
            },
            {
                ""protocol"":""dns"",
                ""action"":""hijack-dns""
            },
            {
                ""outbound"":""direct"",
                ""action"":""route"",
                ""rule_set"":[
                    ""ir_IP"",
                    ""category-ir_SITE""
                ]
            }
            
        ],
        ""rule_set"":[
            {
                ""format"":""binary"",
                ""path"":""geo/ir_IP.srs"",
                ""tag"":""ir_IP"",
                ""type"":""local""
            },
            {
                ""format"":""binary"",
                ""path"":""geo/category-ir_SITE.srs"",
                ""tag"":""category-ir_SITE"",
                ""type"":""local""
            }
        ]
    }
}";

		public static string sg_mixedInbound = @"{
			""listen"": ""127.0.0.1"",
			""listen_port"": 1080,		
			""tag"": ""mixed-in"",
			""type"": ""mixed""
		}";
		public static string sg_vpnInbound = @" {
      ""tag"": ""tun-in"",
      ""type"": ""tun"",
      ""interface_name"": ""irspeedy-tun"",
      ""auto_route"": true,
      ""mtu"": 1500,
      ""stack"": ""system"",
      ""strict_route"": true,
      ""address"": [""172.19.0.1/24""],
      ""route_exclude_address"": [""127.0.0.0/8"", ""10.0.0.0/8"", ""172.16.0.0/12"",
    ""192.168.0.0/16"", ""169.254.0.0/16"", ""224.0.0.0/4"", ""255.255.255.255/32""]
    }";
		public static string sg_vpnRouteRules = @"            
            {
                ""process_name"": [ 
                    ""core"",
                    ""core.exe"",
                    ""sguard32"",
                    ""sguard32.exe"",
                    ""sguard64"",
                    ""sguard64.exe"",
                    ""throne"",
                    ""throne.exe"",
                    ""hysteria-windows-386"",
                    ""hysteria-windows-386.exe"",
                    ""hysteria-windows-amd64"",
                    ""hysteria-windows-amd64.exe""
                ],
                ""action"": ""route"",
                ""outbound"": ""direct""
            }";

        public static string sg_ExcludeRouteRules = @"{
				""action"": ""route"",
				""outbound"": ""direct"",
				""process_path"": """"
			}";
        public static string sg_ExcludeDNSRules = @"{
				""action"": ""route"",
				""process_path"": [					
				],
				""server"": ""dns-direct"",
				""strategy"": """"
			}";

        public static string sg_localvpn = @"{
    ""dns"": {
        ""servers"": [
            {
                ""tag"": ""dns-direct"",
                ""address"": ""tls://8.8.8.8"",
                ""detour"": ""proxy""
            }
        ]
    },
    ""inbounds"": [
        {
            ""type"": ""tun"",
            ""interface_name"": ""irspeedy-tun"",
            ""inet4_address"": ""172.19.0.1/28"",
            
            ""mtu"": 9000,
            ""auto_route"": true,
            ""strict_route"": false,
            ""stack"": ""gvisor"",
            ""endpoint_independent_nat"": true,
            ""sniff"": true
        }
    ],
    ""outbounds"": [
        {
            ""type"": ""socks"",
            ""tag"": ""proxy"",
            ""udp_fragment"": true,
            ""server"": ""127.0.0.1"",
            ""server_port"": 1080
        },
        {
            ""type"": ""block"",
            ""tag"": ""block""
        },
        {
            ""type"": ""direct"",
            ""tag"": ""direct""
        },
        {
            ""type"": ""dns"",
            ""tag"": ""dns-out""
        }
    ],
    ""route"": {
        ""auto_detect_interface"": true,
        ""rules"": [
            {
                ""inbound"": ""dns-in"",
                ""outbound"": ""dns-out""
            },
            {
                ""network"": ""udp"",
                ""port"": [
                    135,
                    137,
                    138,
                    139,
                    5353
                ],
                ""outbound"": ""block""
            },
            {
                ""ip_cidr"": [
                    ""224.0.0.0/3"",
                    ""ff00::/8""
                ],
                ""outbound"": ""block""
            },
            {
                ""source_ip_cidr"": [
                    ""224.0.0.0/3"",
                    ""ff00::/8""
                ],
                ""outbound"": ""block""
            },
            {
                ""port"": 53,
                ""process_name"": [
                  ""libcore"",
                    ""libcore.exe"",
                    ""core"",
                    ""core.exe"",
                  ""sguard32"",
                    ""sguard32.exe"",
                    ""sguard64"",
                    ""sguard64.exe"",
					""vguard"",
					""vguard.exe""

                ],
                ""outbound"": ""dns-out""
            },
            {
                ""process_name"": [
                   ""libcore"",
                    ""libcore.exe"",
                    ""core"",
                    ""core.exe"",
                   ""sguard32"",
                    ""sguard32.exe"",
                    ""sguard64"",
                    ""sguard64.exe"",
					""sguard64_2"",
                    ""sguard64_2.exe"",
					""vguard"",
					""vguard.exe""
                ],
                ""outbound"": ""direct""
            }
            
            
        ]
    }
}";

		public static string sg_UrlTest = "{  \r\n    \"dns\": {\r\n        \"rules\": [\r\n        ],\r\n        \"servers\": [\r\n            {\r\n                \"domain_resolver\": \"dns-local\",\r\n                \"tag\": \"dns-direct\",\r\n                \"type\": \"local\"\r\n            },\r\n            {\r\n                \"tag\": \"dns-local\",\r\n                \"type\": \"local\"\r\n            }\r\n        ]\r\n    },\r\n    \"endpoints\": [\r\n    ],\r\n    \"log\": {\r\n        \"level\": \"info\"\r\n    },\r\n    \"outbounds\": [\r\n        \r\n        {\r\n            \"tag\": \"direct\",\r\n            \"type\": \"direct\"\r\n        }\r\n    ],\r\n    \"route\": {\r\n        \"auto_detect_interface\": true,\r\n        \"default_domain_resolver\": {\r\n            \"server\": \"dns-direct\",\r\n            \"strategy\": \"\"\r\n        }\r\n    }\r\n}";

		public static string sg_httpheaders=@"{
						
							""User-Agent"": ""Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.75 Safari/537.36"",
							""Accept-Encoding"": ""gzip, deflate"",
							""Connection"":""keep-alive"",
							""Pragma"": ""no-cache""
						}";
		public static string sg_wgOutbound = @"{
  ""type"": ""wireguard"",
  ""tag"": ""proxy"",

  ""server"": ""127.0.0.1"",
  ""server_port"": 1080,
  ""system_interface"": false,
  ""interface_name"": ""wg0"",
  ""local_address"": [
    ""10.8.0.39/32""
  ],
  ""private_key"": ""YNXtAzepDqRv9H52osJVDQnznT5AM11eCK3ESpwSt04="",
  ""peer_public_key"": ""Z1XXLsKYkYxuiYjJIkRvtIKFepCYHTgON+GwPq7SOV4="",
  ""pre_shared_key"": ""31aIhAPwktDGpH4JDhA8GNvjFXEf/a6+UaQRyOAiyfM="",
  ""reserved"": [0, 0, 0],
  ""workers"": 4,
  ""mtu"": 1408
}";

		public static string[] sg_gamblingDomains = new[]
		{
			"hotbetdonya.org",
			"bitcoin-haz.com",
			"tnnforu.casa",
			"iranshartbandi.com",
			"shirbet.info",
			"shartbaz.com",
			"pabllobttt.club",
			"gorgbet.info",
			"takbetaddress.com",
			"shart303.com",
			"jetbet.site",
			"topbetiran.vip",
			"shartbazi.com",
			"iranbet.games",
			"baxiran.com",
			"hivanews.com",
			"vbmgimir.com",
			"betmajic.com",
			"btl90.com",
			"bia.bet",
			"portobbt.club",
			"locoooooo90.pw",
			"bersoferesv.pw",
			"donresebar.xyz",
			"bia2rch.info",
			"kz102020.club",
			"cocoosuydusna.online",
			"aab18btt.info",
			"upoup.xyz",
			"s21.site",
			"cndifnigjiyjyyuu.casa",
			"nscdjunrfuj.fun",
			"shart90.com",
			"1xbet.com",
			"fa-1xbet.com",
			"betforward.com",
			"bet365.com",
			"berry-bet.net",
			"bakht.org",
			"melbet.com",
			"betwinner.com",
			"pinnacle.com",
			"williamhill.com",
			"betway.com",
			"22bet.com",
			"betfair.com",
			"parimatch.com",
			"cloudflare-ech"
		};

		public static string[] sg_gamblingDomainSuffix = new[]
		{
			"hotbetdonya.org",
			".bitcoin-haz.com",
			".tnnforu.casa",
			".iranshartbandi.com",
			".shirbet.info",
			".shartbaz.com",
			".pabllobttt.club",
			".gorgbet.info",
			".takbetaddress.com",
			".shart303.com",
			".jetbet.site",
			".topbetiran.vip",
			".shartbazi.com",
			".iranbet.games",
			".baxiran.com",
			".hivanews.com",
			".vbmgimir.com",
			".betmajic.com",
			".btl90.com",
			".bia.bet",
			".portobbt.club",
			".locoooooo90.pw",
			".bersoferesv.pw",
			".donresebar.xyz",
			".bia2rch.info",
			".kz102020.club",
			".cocoosuydusna.online",
			".aab18btt.info",
			".upoup.xyz",
			".s21.site",
			".cndifnigjiyjyyuu.casa",
			".nscdjunrfuj.fun",
			".shart90.com",
			".1xbet.com",
			".fa-1xbet.com",
			".betforward.com",
			".bet365.com",
			".berry-bet.net",
			".bakht.org",
			".melbet.com",
			".betwinner.com",
			".pinnacle.com",
			".williamhill.com",
			".betway.com",
			".22bet.com",
			".betfair.com",
			".parimatch.com",
			".cloudflare-ech"
		};

		public static string[] sg_vodDomains = new[]
		{
			"filimo.com",
			"namava.ir",
			"namava.tv",
			"tamashakhoneh.ir",
			"tmk.ir",
			"gapfilm.ir",
			"digitoon.tv",
			"filmnet.ir",
			"ipmyp.ir"
		};

		public static string[] sg_vodDomainSuffix = new[]
		{
			".filimo.com",
			".namava.ir",
			".namava.tv",
			".tamashakhoneh.ir",
			".tmk.ir",
			".gapfilm.ir",
			".digitoon.tv",
			".filmnet.ir",
			".ipmyp.ir"
		};
	}
}
