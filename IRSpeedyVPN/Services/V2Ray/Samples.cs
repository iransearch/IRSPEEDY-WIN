using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using v2rayN;

namespace v2rayN
{
	internal class Samples
	{
		public static string v2raySampleClient = @"{
	""log"": {
		""access"": ""Vaccess.log"",
		""error"": ""Verror.log"",
		""loglevel"": ""warning""
	},
	""inbounds"": [{
			""tag"": ""tag1"",
			""port"": 10808,
			""protocol"": ""socks"",
			""listen"": ""127.0.0.1"",
			""settings"": {
				""auth"": ""noauth"",
				""udp"": true
			},
			""sniffing"": {
				""enabled"": true,
				""destOverride"": [
					""http"",
					""tls""
				]
			}
		},
		{
			""tag"": ""tag2"",
			""port"": 10809,
			""protocol"": ""http"",
			""listen"": ""127.0.0.1"",
			""settings"": {
				""allowTransparent"": false
			},
			""sniffing"": {
				""enabled"": true,
				""destOverride"": [
					""http"",
					""tls""
				]
			}
		},
		{
			""tag"": ""tag3"",
			""port"": 10809,
			""protocol"": ""http"",
			""listen"": ""127.0.0.1"",
			""settings"": {
				""allowTransparent"": false
			},
			""sniffing"": {
				""enabled"": true,
				""destOverride"": [
					""http"",
					""tls""
				]
			}
		}
	],
	""outbounds"": [{
			""tag"": ""proxy"",
			""protocol"": ""vmess"",
			""settings"": {
				""vnext"": [{
					""address"": ""v2ray.cool"",
					""port"": 10086,
					""users"": [{
						""id"": ""a3482e88-686a-4a58-8126-99c9df64b7bf"",
						""security"": ""auto""
					}]
				}],
				""servers"": [{
					""address"": ""v2ray.cool"",
					""method"": ""chacha20"",
					""ota"": false,
					""password"": ""123456"",
					""port"": 10086,
					""level"": 1
				}]
			},
			""streamSettings"": {
				""network"": ""tcp""
			},
			""mux"": {
				""enabled"": false
			}
		},
		{
			""protocol"": ""freedom"",
			""settings"": {},
			""tag"": ""direct""
		},
		{
			""protocol"": ""blackhole"",
			""tag"": ""block"",
			""settings"": {
				""response"": {
					""type"": ""http""
				}
			}
		}
	],
	""routing"": {
		""domainStrategy"": ""IPIfNonMatch"",
		""rules"": [
				{
                    ""inboundTag"": [""api""],
                    ""outboundTag"": ""api"",
                    ""type"": ""field""
                }
		]
	}
}";

		public static string v2raySampleServer = @"{
	""log"": {
		""access"": ""/var/log/v2ray/access.log"",
		""error"": ""/var/log/v2ray/error.log"",
		""loglevel"": ""warning""
	},
	""inbounds"": [{
		""port"": 10086,
		""protocol"": ""vmess"",
		""settings"": {
			""clients"": [{
				""id"": ""23ad6b10-8d1a-40f7-8ad0-e3e35cd38297"",
				""level"": 1,
				""email"": ""t@t.tt""
			}]
		},
		""streamSettings"": {
			""network"": ""tcp""
		}
	}],
	""outbounds"": [{
		""protocol"": ""freedom"",
		""settings"": {}
	}, {
		""protocol"": ""blackhole"",
		""settings"": {},
		""tag"": ""block""
	}],
	""routing"": {
		""domainStrategy"": ""IPIfNonMatch"",
		""rules"": []
	}
}";
		public static string v2raySampleHttprequestFileName = @"{""version"":""1.1"",""method"":""GET"",""path"":[$requestPath$],""headers"":{""Host"":[$requestHost$],""User-Agent"":[""Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.75 Safari/537.36"",""Mozilla/5.0 (iPhone; CPU iPhone OS 10_0_2 like Mac OS X) AppleWebKit/601.1 (KHTML, like Gecko) CriOS/53.0.2785.109 Mobile/14A456 Safari/601.1.46""],""Accept-Encoding"":[""gzip, deflate""],""Connection"":[""keep-alive""],""Pragma"":""no-cache""}}";
		public static string v2raySampleHttpresponseFileName = @"{""version"":""1.1"",""status"":""200"",""reason"":""OK"",""headers"":{""Content-Type"":[""application/octet-stream"",""video/mpeg""],""Transfer-Encoding"":[""chunked""],""Connection"":[""keep-alive""],""Pragma"":""no-cache""}}";
		public static string v2raySampleInbound = @"{
	""tag"": ""tag1"",
	""port"": 10808,
	""protocol"": ""socks"",
	""listen"": ""127.0.0.1"",
	""settings"": {
		""auth"": ""noauth"",
		""udp"": true,
		""allowTransparent"": false
	},
	""sniffing"": {
		""enabled"": true,
		""destOverride"": [
			""http"",
			""tls""
		]
	}
} ";
		public static string GetResource(string name)
        {
			if (name == Global.v2raySampleClient)
				return v2raySampleClient;
			else if (name == Global.v2raySampleServer)
				return v2raySampleServer;
			else if (name == Global.v2raySampleHttprequestFileName)
				return v2raySampleHttprequestFileName;
			else if (name == Global.v2raySampleHttpresponseFileName)
				return v2raySampleHttpresponseFileName;
			else if (name == Global.v2raySampleInbound)
				return v2raySampleInbound;
			return null;
        }
	}
}
