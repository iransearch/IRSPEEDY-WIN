using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class VpnTimeZoneRouting
    {
        internal static string Apply(string config)
        {
            var root = JObject.Parse(config);
            var rules = root["route"]?["rules"] as JArray;
            var outbounds = root["outbounds"] as JArray;
            var inbounds = (root["inbounds"] as JArray)?.OfType<JObject>()
                .Where(i => (string)i["type"] == "mixed" || (string)i["type"] == "http")
                .Select(i => (string)i["tag"]).Where(tag => !string.IsNullOrEmpty(tag)).ToArray();
            if (rules == null || inbounds == null || inbounds.Length == 0
                || outbounds == null || !outbounds.OfType<JObject>().Any(o => (string)o["tag"] == "proxy"))
                throw new InvalidOperationException("VPN IP lookup requires the primary proxy and an HTTP listener.");
            // Only this HTTPS request on the explicit user listener bypasses AI,
            // VOD, Game Mode and process exclusions. Core/DNS traffic is unaffected.
            rules.Insert(0, new JObject
            {
                ["inbound"] = new JArray(inbounds), ["domain"] = new JArray(Common.TimeZoneLookup.Host),
                ["network"] = "tcp", ["port"] = 443, ["action"] = "route", ["outbound"] = "proxy"
            });
            return root.ToString();
        }
    }
}
