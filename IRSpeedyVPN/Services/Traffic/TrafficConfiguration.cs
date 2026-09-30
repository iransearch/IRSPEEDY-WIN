using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IRSpeedyVPN.Services.Traffic
{
    internal static class TrafficConfiguration
    {
        public static string Enable(string config)
        {
            var root = JObject.Parse(config);
            var experimental = root["experimental"] as JObject;
            if (experimental == null) root["experimental"] = experimental = new JObject();
            // Same internal tracker switch as Throne. No external controller/listener.
            if (!(experimental["clash_api"] is JObject))
                experimental["clash_api"] = new JObject { ["default_mode"] = "" };
            var route = root["route"] as JObject;
            if (route == null) root["route"] = route = new JObject();
            route["find_process"] = true;
            return root.ToString(Formatting.None);
        }
    }
}
