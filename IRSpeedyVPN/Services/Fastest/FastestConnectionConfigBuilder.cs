using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Services.Fastest
{
    internal sealed class FastestRoute
    {
        public IVPNService Service { get; set; }
        public Url Url { get; set; }
        public string Tag { get; set; }
        public string RouteKey { get; set; }
        public int? HysteriaSocksPort { get; set; }
        public int? XraySocksPort { get; set; }
        public string XrayUser { get; set; }
        public string XrayPassword { get; set; }
        public string Link => Url?.url;
    }

    internal sealed class FastestBuildResult
    {
        public string CoreConfig { get; set; }
        public bool NeedXray { get; set; }
        public string XrayConfig { get; set; }
        public FastestRoute FallbackRoute { get; set; }
        public IReadOnlyDictionary<string, FastestRoute> TagToRoute { get; set; }
    }

    internal static class FastestConnectionConfigBuilder
    {
        internal const string ProxyPrefix = "smart-1-proxy-";
        internal const string SelectorTag = "smart-balancer-1";

        public static FastestBuildResult Build(
            IReadOnlyCollection<FastestRoute> routes,
            string username,
            int listenPort,
            bool vpnMode,
            bool isShareActive,
            string[] shieldFiles,
            string[] excludedProcessPaths)
        {
            if (routes == null || routes.Count == 0)
                throw new InvalidOperationException("No compatible FASTEST CONNECTION routes were found.");

            var fallback = FastestConnectionCache.SelectFallback(routes, username) ?? routes.First();
            var normalLinks = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var socksOverrides = new Dictionary<string, Tuple<int, string, string>>(StringComparer.OrdinalIgnoreCase);
            var xrayRoutes = new List<IRSpeedyVPN.Services.Xray.ConfigGenerator.XraySocksInfo>();
            var tagToRoute = new Dictionary<string, FastestRoute>(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routes)
            {
                if (route == null || string.IsNullOrWhiteSpace(route.Link))
                    continue;

                normalLinks[route.Link] = null;
                if (route.HysteriaSocksPort.HasValue)
                {
                    socksOverrides[route.Link] = Tuple.Create(route.HysteriaSocksPort.Value, string.Empty, string.Empty);
                }
                else if (route.XraySocksPort.HasValue)
                {
                    socksOverrides[route.Link] = Tuple.Create(
                        route.XraySocksPort.Value,
                        route.XrayUser ?? string.Empty,
                        route.XrayPassword ?? string.Empty);
                    xrayRoutes.Add(new IRSpeedyVPN.Services.Xray.ConfigGenerator.XraySocksInfo
                    {
                        Link = route.Link,
                        Tag = route.Tag,
                        Port = route.XraySocksPort.Value,
                        User = route.XrayUser,
                        Pass = route.XrayPassword
                    });
                }
            }

            if (normalLinks.Count == 0)
                throw new InvalidOperationException("No compatible FASTEST CONNECTION routes were found.");

            Dictionary<string, string> generatedTagToUrl;
            var generated = IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetUrlTestConfig(
                normalLinks,
                listenPort,
                out generatedTagToUrl,
                null,
                socksOverrides);
            var core = JObject.Parse(generated);

            var routeByUrl = routes
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Link))
                .GroupBy(x => x.Link, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var outbounds = core["outbounds"] as JArray ?? new JArray();
            var proxyTags = new JArray();
            foreach (var outbound in outbounds.OfType<JObject>())
            {
                var tag = (string)outbound["tag"];
                if (string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "direct", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!generatedTagToUrl.TryGetValue(tag, out var url) || !routeByUrl.TryGetValue(url, out var route))
                    continue;

                outbound["tag"] = route.Tag;
                proxyTags.Add(route.Tag);
                tagToRoute[route.Tag] = route;
            }

            if (proxyTags.Count == 0)
                throw new InvalidOperationException("FASTEST CONNECTION could not create any native core outbounds.");

            outbounds.Add(new JObject
            {
                ["type"] = "urltest",
                ["tag"] = SelectorTag,
                ["outbounds"] = proxyTags,
                ["url"] = "https://connectivitycheck.gstatic.com/generate_204",
                ["interval"] = "20m",
                ["tolerance"] = 200,
                ["idle_timeout"] = "30m",
                ["interrupt_exist_connections"] = false
            });

            core["outbounds"] = outbounds;
            var routeObject = core["route"] as JObject ?? new JObject();
            routeObject["final"] = SelectorTag;
            core["route"] = routeObject;

            var baseConfig = IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetConfig(
                "socks://127.0.0.1:1?host=127.0.0.1",
                listenPort,
                vpnMode,
                isShareActive,
                shieldFiles,
                null,
                new string[0],
                null,
                false,
                null,
                null,
                excludedProcessPaths);
            var baseRoot = JObject.Parse(baseConfig);
            core["inbounds"] = baseRoot["inbounds"]?.DeepClone();
            if (baseRoot["dns"] != null)
                core["dns"] = baseRoot["dns"]?.DeepClone();

            var baseRoute = baseRoot["route"] as JObject;
            if (baseRoute != null)
            {
                routeObject["rules"] = baseRoute["rules"]?.DeepClone();
                routeObject["rule_set"] = baseRoute["rule_set"]?.DeepClone();
                routeObject["auto_detect_interface"] = baseRoute["auto_detect_interface"] ?? true;
                routeObject["find_process"] = baseRoute["find_process"] ?? false;
                routeObject["final"] = SelectorTag;
            }

            var xrayConfig = xrayRoutes.Count > 0
                ? IRSpeedyVPN.Services.Xray.ConfigGenerator.GetUrlTestXrayConfig(xrayRoutes)
                : string.Empty;

            return new FastestBuildResult
            {
                CoreConfig = core.ToString(Formatting.None),
                NeedXray = xrayRoutes.Count > 0,
                XrayConfig = xrayConfig,
                FallbackRoute = fallback,
                TagToRoute = tagToRoute
            };
        }
    }
}
