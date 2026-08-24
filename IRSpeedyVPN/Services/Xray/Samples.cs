using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Xray
{
    public  class Samples
    {
        // The multiObservatory block is generated at runtime by SmartIpRouting so the
        // VOD and AI observers only appear when those services are active.
        public static string BalancerConfig= @"{
			""inbounds"": [],
			""outbound"": [],
			""remarks"": ""SMART SERVER"",
			""routing"": {
				""balancers"": [
					{
						""fallbackTag"": ""smart-proxy-0"",
						""selector"": [
							""smart-proxy-""
						],
						""strategy"": {
							""settings"": {
								""observerTag"": ""smart-observer-1"",
								""expected"": 5,
								""maxRTT"": ""3s""
							},
							""type"": ""leastLoad""
						},
						""tag"": ""smart-balancer-1""
					}
				],
				""domainStrategy"": ""AsIs"",
				""rules"": [
					{
						""network"": ""udp"",
						""outboundTag"": ""block"",
						""port"": ""443"",
						""type"": ""field""
					},
					{
						""balancerTag"": ""smart-balancer-1"",
						""domain"": [
							""domain:api.ipify.org"",
							""domain:ipify.org"",
							""domain:ipinfo.io"",
							""domain:ip-api.com"",
							""domain:ipwho.is"",
							""domain:ifconfig.me"",
							""domain:icanhazip.com"",
							""domain:ip.sb""
						],
						""type"": ""field""
					},
					{
						""ip"": [
							""geoip:private""
						],
						""outboundTag"": ""direct"",
						""type"": ""field""
					},
					{
						""domain"": [
							""geosite:private""
						],
						""outboundTag"": ""direct"",
						""type"": ""field""
					},
					{
						""ip"": [
							""geoip:ir""
						],
						""outboundTag"": ""direct"",
						""type"": ""field""
					},
					{
						""domain"": [
							""geosite:category-ir""
						],
						""outboundTag"": ""direct"",
						""type"": ""field""
					},
					{
						""balancerTag"": ""smart-balancer-1"",
						""network"": ""tcp,udp"",
						""type"": ""field""
					}
				]
			}
		}";
    }
}
