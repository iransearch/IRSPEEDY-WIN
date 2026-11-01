using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    // Observational only. No raw exception messages, URLs, paths or config are written.
    internal sealed class ConnectionAttemptDiagnostic
    {
        private static readonly HashSet<string> Origins = new HashSet<string>(StringComparer.Ordinal)
        { "ui-request", "ui-callback", "ui-result", "ui-banner", "proxifier-start", "proxifier-result", "tunnel-start", "tunnel-connect" };
        private static readonly HashSet<string> Phases = new HashSet<string>(StringComparer.Ordinal)
        {
            "begin", "connect-click", "wait-gate", "drain-tests", "prepare-service", "disconnect-previous",
            "detach-proxifier", "validate-runtime", "invoke-connect", "connect-dispatched", "request-release",
            "service-callback", "ui-dispatch", "proxifier-attach", "process-result", "presentation-delay",
            "hide-loading", "success-message", "connection-clock", "mark-success", "show-status",
            "disconnect-cleanup", "show-list", "failure-message", "show-message", "complete",
            "pause-sharing", "read-settings", "permission-check", "stop-previous", "fixed-server-test",
            "prepare-config", "listen-port", "shield-files", "sni-runtime", "chain-config", "prepare-pool",
            "prepare-xray", "build-shell", "apply-pool", "start-core", "start-recovery", "ai-recheck",
            "resume-sharing", "apply-system-proxy", "notify-ui", "failure-cleanup", "write-profile",
            "quic-filter", "launch-proxifier", "watch-proxifier", "proxifier-notify", "proxifier-exit"
        };
        private static readonly HashSet<string> Outcomes = new HashSet<string>(StringComparer.Ordinal)
        { "start", "ok", "failed", "received", "displayed", "skipped", "dispatched" };
        private readonly string trace = Guid.NewGuid().ToString("N");
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly long requestVersion;
        private readonly string origin;
        private readonly Func<string> identity;
        private string phase = "begin";

        internal ConnectionAttemptDiagnostic(long version, string source, Func<string> serviceIdentity = null)
        {
            requestVersion = version;
            origin = Origins.Contains(source ?? "") ? source : "other";
            identity = serviceIdentity;
        }

        internal void Step(string next, string outcome = "start", string message = null)
        {
            try
            {
                phase = Phases.Contains(next ?? "") ? next : "other";
                ConnectionDiagnostics.Write("connect-attempt", Context()
                    + " outcome=" + (Outcomes.Contains(outcome ?? "") ? outcome : "other")
                    + MessageFields(message));
            }
            catch { }
        }

        internal void Failure(Exception exception, bool capture = false)
        {
            if (exception == null) return;
            try
            {
                string error = Guid.NewGuid().ToString("N"), context = Context() + " outcome=failed errorId=" + error;
                ConnectionDiagnostics.Write("connect-attempt-error", context);
                var pending = new Queue<Tuple<Exception, int>>();
                var seen = new List<Exception>();
                pending.Enqueue(Tuple.Create(exception, -1));
                while (pending.Count != 0 && seen.Count < 8)
                {
                    var entry = pending.Dequeue();
                    var current = entry.Item1;
                    if (seen.Any(value => ReferenceEquals(value, current))) continue;
                    int index = seen.Count;
                    seen.Add(current);
                    string fields = context + " exceptionIndex=" + index + " parentIndex=" + entry.Item2
                        + " exception=" + Name(current.GetType().FullName)
                        + " hresult=0x" + current.HResult.ToString("X8", CultureInfo.InvariantCulture);
                    var native = current as Win32Exception;
                    if (native != null) fields += " nativeError=" + native.NativeErrorCode.ToString(CultureInfo.InvariantCulture);
                    var socket = current as SocketException;
                    if (socket != null) fields += " socketError=" + socket.SocketErrorCode;
                    var web = current as WebException;
                    if (web != null) fields += " webStatus=" + web.Status;
                    fields += MessageFields(SafeMessage(current)) + " frames=" + Frames(current);
                    ConnectionDiagnostics.Write("connect-attempt-exception", fields);
                    var aggregate = current as AggregateException;
                    if (aggregate != null)
                    {
                        foreach (var child in aggregate.InnerExceptions.Take(8 - seen.Count))
                            pending.Enqueue(Tuple.Create(child, index));
                    }
                    else if (current.InnerException != null)
                        pending.Enqueue(Tuple.Create(current.InnerException, index));
                }
            }
            catch { }
            // Reuse the existing sanitized Sentry exception path for previously swallowed failures.
            // Callers which already report their exception leave capture=false to avoid duplicates.
            if (capture) try { ErrorReporting.Capture(exception, false); } catch { }
        }

        private string Context()
        {
            string service = null;
            try { service = identity?.Invoke(); } catch { }
            if (service == null || !Regex.IsMatch(service, @"\Aservice=[0-9a-f]{32} connection=(?:none|[0-9a-f]{32})\z"))
                service = "service=other connection=none";
            return "trace=" + trace + " requestVersion=" + requestVersion.ToString(CultureInfo.InvariantCulture)
                + " origin=" + origin + " phase=" + phase + " elapsedMs=" + clock.ElapsedMilliseconds
                + " " + service + (origin == "ui-banner" ? " caller=" + BannerCaller() : "");
        }

        private static string MessageFields(string message)
        {
            if (message == null) return "";
            return " messageId=" + ConnectionDiagnostics.Fingerprint(message)
                + " reason=" + NetworkFailureDiagnostic.CoreReason(message);
        }
        private static string SafeMessage(Exception exception)
        { try { return exception.Message; } catch { return ""; } }
        private static string Name(string value)
        {
            value = value ?? "unknown";
            return Regex.Replace(value.Substring(0, Math.Min(value.Length, 160)), @"[^A-Za-z0-9_.<>+`-]", "_");
        }
        private static string BannerCaller()
        {
            try
            {
                string value = ConnectionDiagnostics.Caller();
                return Regex.Replace(value.Substring(0, Math.Min(value.Length, 1024)), @"[^A-Za-z0-9_.<>+`-]", "_");
            }
            catch { return "unavailable"; }
        }
        private static string Frames(Exception exception)
        {
            try
            {
                return string.Join(">", (new StackTrace(exception, false).GetFrames() ?? new StackFrame[0]).Take(8)
                    .Select(frame => frame.GetMethod()).Where(method => method != null)
                    .Select(method => Name(method.DeclaringType?.FullName) + "." + Name(method.Name)));
            }
            catch { return "unavailable"; }
        }
    }
}
