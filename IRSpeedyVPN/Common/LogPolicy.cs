using System;
using System.Collections.Generic;

namespace IRSpeedyVPN.Common
{
    internal static class LogPolicy
    {
        private static readonly HashSet<string> RoutineStages = new HashSet<string>(StringComparer.Ordinal)
        {
            "ui-connection-dispatch", "ui-navigation", "ui-connect-click", "ui-connect-queued",
            "ui-connection-callback", "core-health-begin", "core-health-end",
            "probe-rpc-begin", "probe-rpc-end", "test-group-enter", "tests-drain-complete",
            "tests-cancel-request", "config-mode", "ai-setting-read", "ai-config-apply",
            "ai-members-built", "core-start-begin", "core-start-listener", "core-start-ready",
            "config-apply-begin", "config-apply-end", "proxy-change-begin"
        };

        internal static bool ShouldWrite(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (message.StartsWith("[ConnectionDiagnostic]", StringComparison.Ordinal))
                return UsefulDiagnostic(Field(message, "stage"), message);
            if (message.StartsWith("[LoginPerformance]", StringComparison.Ordinal)) return false;
            if (message.StartsWith("[ProbeDetail]", StringComparison.Ordinal))
                return Field(message, "result") != "success";
            if (message.StartsWith("[UrlTest]", StringComparison.Ordinal))
            {
                var stage = Field(message, "stage");
                return stage == "final" || HasFailure(stage, message) || stage == "candidate-rejected";
            }
            if (message.StartsWith("[NetworkMonitor]", StringComparison.Ordinal))
                return message.Contains("online=false") || message.Contains("exception=")
                    || message.Contains("error=");
            return true;
        }

        internal static bool UsefulDiagnostic(string stage, string fields)
        {
            // Explicit errors always take precedence over routine-stage filtering.
            if (HasFailure(stage, fields)) return true;
            if (stage == "network-snapshot" || stage == "runtime-snapshot")
                return string.Equals(Field(fields, "changed"), "true", StringComparison.OrdinalIgnoreCase);
            if (stage == "core-detail")
            {
                var coreEvent = Field(fields, "event");
                return coreEvent == "default-interface" || coreEvent == "hy2-reset"
                    || coreEvent == "pool-probes-ready";
            }
            if (stage == "core-output" || stage == "core-exit-tail")
            {
                var category = Field(fields, "category");
                return category == "error" || category == "warning" || category == "panic"
                    || category == "fatal" || category == "pool-selection-error"
                    || category == "ai-probe-error" || category == "network-changed"
                    || category == "showip-route" || category == "public-ip-route";
            }
            if (stage == "ui-connection-callback")
                return string.Equals(Field(fields, "connected"), "false", StringComparison.OrdinalIgnoreCase);
            return !RoutineStages.Contains(stage ?? "");
        }

        private static bool HasFailure(string stage, string fields)
        {
            stage = stage ?? "";
            if (stage.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || stage.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || stage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || stage.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var key in new[] { "error", "exception" })
            {
                var value = Field(fields, key).Trim('"', '<', '>');
                if (value.Length > 0 && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                    && value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return Field(fields, "result") == "failed" || Field(fields, "status") == "failed";
        }

        private static string Field(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var token = key + "=";
            int start = text.StartsWith(token, StringComparison.Ordinal) ? 0
                : text.IndexOf(" " + token, StringComparison.Ordinal);
            if (start < 0) return "";
            if (text[start] == ' ') start++;
            start += token.Length;
            int end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            return text.Substring(start, end - start);
        }
    }
}
