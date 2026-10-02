using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace IRSpeedyVPN.Services.Xray
{
    /// <summary>Each SOCKS bridge reaches exactly one Xray member. Selection and
    /// health belong to independent sing-box groups, never to an inner balancer.</summary>
    internal sealed class AutoSelectorPoolPlan : IDisposable
    {
        private readonly List<int> ports = new List<int>();
        private readonly Action<int> releasePort;
        private readonly List<JObject> main = new List<JObject>();
        private readonly List<JObject> ai = new List<JObject>();
        private bool disposed;
        internal string XrayConfig { get; private set; }
        internal string User { get; private set; }
        internal string Password { get; private set; }
        internal int PrimaryPort => (int)main[0]["server_port"];
        internal int MainMembers => main.Count;
        internal int AiMembers => ai.Count;
        internal int HysteriaMembers { get; private set; }
        internal bool AiEnabled { get; private set; }

        private AutoSelectorPoolPlan(Action<int> release) { releasePort = release; }

        internal static AutoSelectorPoolPlan Create(IEnumerable<string> links, IEnumerable<string> aiLinks,
            bool aiEnabled, Func<int> allocate, Action<int> release)
        {
            var plan = new AutoSelectorPoolPlan(release)
            {
                User = Guid.NewGuid().ToString("N"), Password = Guid.NewGuid().ToString("N"),
                AiEnabled = aiEnabled
            };
            try
            {
                bool policy;
                int members, hysteria;
                // Reuse the validated transport builders, refusal filters and Core DNS inheritance.
                var config = ConfigGenerator.GetSmartBalancerConfig(links, 1, plan.User, plan.Password,
                    aiLinks, out policy, out members, out hysteria, aiEnabled);
                if (string.IsNullOrWhiteSpace(config)) return null;
                var root = JObject.Parse(config);
                plan.HysteriaMembers = hysteria;
                var inbounds = new JArray();
                var rules = new JArray();
                foreach (var outbound in ((JArray)root["outbounds"]).OfType<JObject>())
                {
                    var tag = (string)outbound["tag"];
                    bool isMain = tag.StartsWith(SmartIpRouting.SmartProxyPrefix, StringComparison.Ordinal);
                    bool isAi = tag.StartsWith(SmartIpRouting.AiProxyPrefix, StringComparison.Ordinal);
                    if (!isMain && !isAi) continue;
                    int port = allocate();
                    // A duplicate bridge would silently send probes to the wrong member.
                    if (port < 1 || port > 65535 || plan.ports.Contains(port))
                        throw new InvalidOperationException("Invalid or duplicate pool bridge port.");
                    plan.ports.Add(port);
                    var inboundTag = "bridge-" + tag;
                    inbounds.Add(new JObject
                    {
                        ["tag"] = inboundTag, ["listen"] = "127.0.0.1", ["port"] = port,
                        ["protocol"] = "socks",
                        ["settings"] = new JObject
                        {
                            ["auth"] = "password", ["udp"] = true,
                            ["accounts"] = new JArray(new JObject { ["user"] = plan.User, ["pass"] = plan.Password })
                        }
                    });
                    rules.Add(new JObject
                    {
                        ["type"] = "field", ["inboundTag"] = new JArray(inboundTag), ["outboundTag"] = tag
                    });
                    var bridge = new JObject
                    {
                        ["type"] = "socks", ["tag"] = tag, ["server"] = "127.0.0.1",
                        ["server_port"] = port, ["version"] = "5",
                        ["username"] = plan.User, ["password"] = plan.Password
                    };
                    (isMain ? plan.main : plan.ai).Add(bridge);
                }
                root["inbounds"] = inbounds;
                root["routing"] = new JObject { ["domainStrategy"] = "AsIs", ["rules"] = rules };
                root.Remove("burstObservatory");
                root.Remove("observatory");
                plan.XrayConfig = root.ToString();
                return plan;
            }
            catch { plan.Dispose(); throw; }
        }

        private static JObject Group(string tag, IEnumerable<JObject> bridges)
        {
            var members = bridges.ToArray();
            return new JObject
            {
                ["type"] = "auto-selector", ["tag"] = tag,
                ["outbounds"] = new JArray(members.Select(b => (string)b["tag"])),
                ["url"] = "https://connectivitycheck.gstatic.com/generate_204",
                ["interval"] = "900s", ["bench_interval"] = "900s", ["watch_interval"] = "300s",
                ["active_size"] = Math.Min(8, members.Length), ["expected"] = Math.Min(3, members.Length),
                ["sampling"] = 10, ["tolerance"] = 300,
                ["fail_tolerance"] = 0.2,
                ["timeout"] = "5s", ["concurrency"] = 4, ["dial_retries"] = 2,
                ["balance"] = true, ["balance_mode"] = "connection",
                ["interrupt_exist_connections"] = false
                // Leave connectivity_url unset: a blocked fixed endpoint must not
                // declare the physical internet offline. Core retains its OS/error
                // classification and recovery probes through the actual members.
            };
        }

        internal string Apply(string singBoxConfig)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AutoSelectorPoolPlan));
            var root = JObject.Parse(singBoxConfig);
            var outbounds = root["outbounds"] as JArray;
            var primary = outbounds?.OfType<JObject>().SingleOrDefault(o => (string)o["tag"] == "proxy");
            var rules = root["route"]?["rules"] as JArray;
            if (primary == null || rules == null)
                throw new InvalidOperationException("Missing primary outbound or routing rules.");
            // Existing inbounds, DNS, Shield, VOD, geo rules and chain outbounds stay
            // in the base config. Preserve any front-chain detour on every bridge.
            var detour = primary["detour"];
            foreach (var bridge in main.Concat(ai))
            {
                var copy = (JObject)bridge.DeepClone();
                if (detour != null) copy["detour"] = detour.DeepClone();
                outbounds.Add(copy);
            }
            primary.Replace(Group("proxy", main));
            if (ai.Count > 0) outbounds.Add(Group("ai-proxy", ai));
            // Core/process bypasses have no user inbound. Restrict AI policy to
            // user traffic so server DNS and physical connectivity checks cannot
            // be routed back into an AI bridge.
            var userInbounds = (root["inbounds"] as JArray)?.OfType<JObject>()
                .Where(i => (string)i["type"] == "mixed" || (string)i["type"] == "tun"
                    || (string)i["type"] == "socks" || (string)i["type"] == "http")
                .Select(i => (string)i["tag"]).ToArray() ?? new string[0];
            if (userInbounds.Length == 0)
                throw new InvalidOperationException("Pool has no user inbound.");
            // Main and AI bridges have no nested routing. Keep QUIC/443 rejection
            // ahead of routing, including in proxy/share mode.
            rules.Insert(0, new JObject
            {
                ["inbound"] = new JArray(userInbounds), ["network"] = "udp",
                ["port"] = 443, ["action"] = "reject"
            });
            if (AiEnabled)
            {
                var aiRule = new JObject
                {
                    ["inbound"] = new JArray(userInbounds),
                    ["domain_suffix"] = new JArray(SmartIpRouting.AiDomains),
                    ["action"] = ai.Count > 0 ? "route" : "reject"
                };
                if (ai.Count > 0) aiRule["outbound"] = "ai-proxy";
                // Shield and protocol handling used to run before reaching Xray's
                // AI rules. Retain that ordering, with AI ahead of geo/VOD/default.
                var beforeAi = rules.OfType<JObject>().Where(r =>
                    (string)r["action"] == "sniff" || (string)r["action"] == "hijack-dns"
                    || (string)r["action"] == "reject" || (string)r["outbound"] == "block"
                    || r["process_path"] != null || r["process_name"] != null
                    || (r["ip_cidr"] != null && (string)r["outbound"] == "direct"
                        && r["ip_cidr"].ToString().Contains(IRSpeedyVPN.Services.InternetConnectivityMonitor.PingCidr)))
                    .ToArray();
                foreach (var rule in beforeAi) rule.Remove();
                int index = 0;
                foreach (var rule in beforeAi) rules.Insert(index++, rule);
                rules.Insert(index, aiRule);
            }
            return root.ToString();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var port in ports) releasePort(port);
            ports.Clear();
        }
    }
}
