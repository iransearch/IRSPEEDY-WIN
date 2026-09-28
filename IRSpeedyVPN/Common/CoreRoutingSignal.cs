using System;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    // Extract only generated tags, a request correlation ID and the user's fixed
    // test domain classification. Never retain raw destinations or core lines.
    internal static class CoreRoutingSignal
    {
        internal static bool TryRead(string line, out string category, out string details)
        {
            category = details = null;
            if (string.IsNullOrEmpty(line) || line.Length > 4096) return false;
            var route = Regex.Match(line,
                @"app/dispatcher: (?:Hit route rule: \[[^\]\r\n]*\] so )?taking detour \[((?:ai|smart)-proxy-[0-9]{1,5}|direct|block)\] for \[([^\]\r\n]+)\]");
            var defaultRoute = Regex.Match(line, @"app/dispatcher: default route for ([^\s\r\n]+)");
            if (route.Success || defaultRoute.Success)
            {
                string destination = route.Success ? route.Groups[2].Value : defaultRoute.Groups[1].Value;
                bool showIp = Regex.IsMatch(destination, @"\A(?:tcp|udp):(?:[a-zA-Z0-9-]+\.)*showip\.net\.?:[0-9]{1,5}\z", RegexOptions.IgnoreCase);
                string tag = route.Success ? route.Groups[1].Value : "default-handler";
                category = showIp ? "showip-route" : tag.StartsWith("ai-proxy-", StringComparison.Ordinal) ? "ai-route" : "pool-route";
                var request = Regex.Match(line, @"\[(?:Info|Warning|Debug|Error)\]\s+\[([0-9]{1,10})\]");
                details = "decision=" + (route.Success ? "detour" : "default-route")
                    + " outbound=" + tag + " target=" + (showIp ? "showip" : "other")
                    + " destinationId=" + ConnectionDiagnostics.Fingerprint(destination)
                    + " request=" + (request.Success ? request.Groups[1].Value : "unknown");
                return true;
            }
            if (line.Contains("least load: no qualified outbound") || line.Contains("cannot get observation")
                || line.Contains("observer is nil") || line.Contains("balancing strategy returns empty tag"))
            {
                category = "pool-selection-error";
                details = "decision=selection-unavailable pool=unknown";
                return true;
            }
            var failedProbe = Regex.Match(line, @"error ping \S+ with (ai-proxy-[0-9]{1,5}):");
            if (failedProbe.Success)
            {
                category = "ai-probe-error";
                details = "outbound=" + failedProbe.Groups[1].Value;
                return true;
            }
            return false;
        }
    }
}
