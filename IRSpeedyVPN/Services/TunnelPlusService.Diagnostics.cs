using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private readonly string diagnosticServiceId = Guid.NewGuid().ToString("N");
        private string diagnosticConnectionId = "none";
        private string diagnosticXrayConfigId = "none";
        private long diagnosticOutputWindow;
        private int diagnosticOutputCount;
        private readonly Dictionary<string, int> diagnosticCategoryCounts = new Dictionary<string, int>();
        private readonly Queue<string> diagnosticTail = new Queue<string>();
        private readonly object diagnosticOutputLock = new object();

        private string telemetryOutputCategory = "unknown", telemetryOutputReason = "unknown";
        private long telemetryOutputAt, telemetryOutputGeneration = -1;

        private void AnnotateCoreStartup(Exception failure, CoreRuntimeIdentity runtime, string config,
            string xrayConfig, bool needXray, string stage)
        {
            try
            {
                var process = recoveryCoreProcess ?? coreProcess;
                string state = "unknown", exit = "-1";
                int processId = runtime.Pid;
                try
                {
                    if (process != null)
                    {
                        processId = process.Id;
                        state = process.HasExited ? "exited" : "alive";
                        if (state == "exited") exit = process.ExitCode.ToString();
                    }
                }
                catch { state = "unavailable"; }
                ErrorReporting.Annotate(failure, "core.lifecycle_stage", stage,
                    "core.config_id", ConnectionDiagnostics.Fingerprint(config),
                    "core.xray_config_id", needXray ? ConnectionDiagnostics.Fingerprint(xrayConfig) : "none",
                    "core.runtime_sha256", runtime.Sha256, "core.runtime_commit", runtime.Commit,
                    "core.runtime_version", runtime.Version, "core.runtime_arch", runtime.Architecture,
                    "core.process_state", state, "core.pid", processId.ToString(), "core.runtime_pid", runtime.Pid.ToString(),
                    "core.runtime_matches_process", process == null || runtime.Pid < 1 ? "unknown" : processId == runtime.Pid ? "true" : "false",
                    "core.exit_code", exit,
                    "core.owned", coreOwned ? "true" : "false", "core.cancel_requested", userCancelRequested ? "true" : "false",
                    "core.need_xray", needXray ? "true" : "false", "core.main_members", (autoSelectorPlan?.MainMembers ?? 0).ToString(),
                    "core.ai_members", (autoSelectorPlan?.AiMembers ?? 0).ToString(),
                    "core.connection_generation", Interlocked.Read(ref connectionGeneration).ToString(), "core.connection_id", diagnosticConnectionId);
                lock (diagnosticOutputLock)
                {
                    if (telemetryOutputGeneration == Interlocked.Read(ref connectionGeneration))
                        ErrorReporting.Annotate(failure, "core.last_output_category", telemetryOutputCategory,
                            "core.last_output_reason", telemetryOutputReason,
                            "core.last_output_age_ms", Math.Max(0, (Stopwatch.GetTimestamp() - telemetryOutputAt) / (Stopwatch.Frequency / 1000)).ToString());
                }
            }
            catch { }
        }

        internal string DiagnosticIdentity => "service=" + diagnosticServiceId + " connection=" + diagnosticConnectionId;
        private string ActiveDiagnosticState()
        {
            var active = gInfo?.CurrentService as TunnelPlusService;
            if (active == null) return "activeService=none activeConnected=False";
            return "activeService=" + active.diagnosticServiceId + " activeConnection=" + active.diagnosticConnectionId
                + " activeXrayConfigId=" + active.diagnosticXrayConfigId
                + " activeCountryId=" + active.ID + " activeCountryIndex=" + active.CountryIndex
                + " activeConnected=" + active.IsConnected + " activeProxyRequested=" + active.useSystemProxy
                + " activeListenPort=" + active.lastListenPort + " activeAppliedMode=" + (active.IsConnected ? (active.lastVpnMode ? "TUN" : "Proxy") : "not-connected")
                + " activeCancelRequested=" + active.userCancelRequested + " activeReconnecting=" + active.reconnecting;
        }
        private static int DiagnosticPid(Process process)
        {
            try { return process?.Id ?? -1; } catch { return -1; }
        }
        private void DiagnosticStartFailure(string stage, string error, string phase)
        {
            Diagnostic(stage, "startupPhase=" + phase
                + " reason=" + NetworkFailureDiagnostic.CoreReason(error)
                + " " + CoreStartFailureDiagnostic.Fields(error)
                + " messageId=" + ConnectionDiagnostics.Fingerprint(error)
                + " text=\"" + ConnectionDiagnostics.SafeCoreStartText(error) + "\"");
            ConnectionDiagnostics.RequestSnapshot();
        }
        private void Diagnostic(string stage, string details = "")
        {
            try
            {
                ConnectionDiagnostics.Start();
                ConnectionDiagnostics.SetActiveStateReader(ActiveDiagnosticState);
                var active = gInfo?.CurrentService as TunnelPlusService;
                string activeMode = active == null ? "unknown-or-idle" : !active.IsConnected ? "not-connected"
                    : active.lastVpnMode ? "TUN" : "Proxy";
                ConnectionDiagnostics.ObserveMode(activeMode);
                if (active != null && active.lastListenPort > 0) Volatile.Write(ref CoreDiagnosticMetadata.ProxyPort, active.lastListenPort);
                ConnectionDiagnostics.Write(stage, ActiveDiagnosticState() + " service=" + diagnosticServiceId
                    + " connection=" + diagnosticConnectionId + " countryId=" + ID + " countryIndex=" + CountryIndex
                    + " selectedMode=" + (RegHelper.GetSettingValue("VGAURDVPNMode") == "0" ? "Proxy" : "TUN")
                    + " gameMode=" + (RegHelper.GetSettingValue("VGAURDGameMode") == "1")
                    + " activeMode=" + activeMode
                    + " serviceConnected=" + IsConnected + " appliedMode=" + (IsConnected ? (lastVpnMode ? "TUN" : "Proxy") : "not-connected")
                    + " systemProxyRequested=" + useSystemProxy + " proxifierRule=" + (int)ProxifierRuleType
                    + " listenPort=" + lastListenPort + " corePid=" + CoreDiagnosticMetadata.Pid(CorePort)
                    + " coreOwned=" + coreOwned + " controlPort=" + CorePort + " " + details);
            }
            catch { }
        }
        private void ObserveCoreOutput(Process process, string source, string line)
        {
            try
            {
                if (string.IsNullOrEmpty(line)) return;
                string structured;
                if (CoreDiagnosticMetadata.TryParse(line, out structured))
                {
                    ConnectionDiagnostics.Write("core-detail", ActiveDiagnosticState() + " readerService=" + diagnosticServiceId + " readerPid=" + DiagnosticPid(process) + " " + structured);
                    return;
                }
                string lower = line.ToLowerInvariant();
                string routingCategory, routingDetails;
                bool routingSignal = CoreRoutingSignal.TryRead(line, out routingCategory, out routingDetails);
                string category = lower.Contains("panic") ? "panic" : lower.Contains("fatal") ? "fatal"
                    : routingSignal ? routingCategory
                    : lower.Contains("error") || lower.Contains("failed") || lower.Contains("timeout") ? "error"
                    : lower.Contains("warn") ? "warning" : lower.Contains("network changed") ? "network-changed"
                    : lower.Contains("interface") ? "interface" : lower.Contains("route") ? "route"
                    : lower.Contains("tun") ? "tun" : "other";
                if (category == null) return;
                string failureReason = NetworkFailureDiagnostic.CoreReason(line);
                if (category == "error" || category == "pool-selection-error" || category == "panic" || category == "fatal" || category == "warning")
                {
                    var active = gInfo?.CurrentService as TunnelPlusService;
                    if (active != null && process != null && (ReferenceEquals(process, active.recoveryCoreProcess) || ReferenceEquals(process, active.coreProcess)))
                        lock (active.diagnosticOutputLock)
                        {
                            active.telemetryOutputCategory = category; active.telemetryOutputReason = failureReason;
                            active.telemetryOutputAt = Stopwatch.GetTimestamp();
                            active.telemetryOutputGeneration = Interlocked.Read(ref active.connectionGeneration);
                        }
                }
                if (category == "error" || category == "pool-selection-error")
                {
                    // The process reader may be a separate test-service instance.
                    // Read the active connection, without starting another probe.
                    if (failureReason != "none" && failureReason != "canceled" && failureReason != "unclassified")
                        (gInfo?.CurrentService as TunnelPlusService)?.RequestConnectionState("core-dial-error");
                }
                string safeText = ConnectionDiagnostics.SafeCoreText(line);
                lock (diagnosticOutputLock)
                {
                    diagnosticTail.Enqueue("outputPid=" + DiagnosticPid(process) + " outputUtc=" + DateTime.UtcNow.ToString("O") + " source=" + source + " category=" + category + " text=\"" + safeText + "\"");
                    while (diagnosticTail.Count > 40) diagnosticTail.Dequeue();
                    long window = Stopwatch.GetTimestamp() / (5 * Stopwatch.Frequency);
                    if (window != diagnosticOutputWindow)
                    {
                        if (diagnosticOutputCount > 0) ConnectionDiagnostics.Write("core-output-suppressed", "readerPid=" + DiagnosticPid(process) + " count=" + diagnosticOutputCount);
                        diagnosticOutputWindow = window; diagnosticOutputCount = 0; diagnosticCategoryCounts.Clear();
                    }
                    int count; diagnosticCategoryCounts.TryGetValue(category, out count);
                    diagnosticCategoryCounts[category] = count + 1;
                    // Route chatter must never consume the error category's budget.
                    if (count >= 10 && category != "panic" && category != "fatal") { diagnosticOutputCount++; return; }
                }
                // Keep diagnostic vocabulary only; URLs, identifiers, credentials and configs are removed.
                Diagnostic("core-output", "pid=" + DiagnosticPid(process) + " source=" + source
                    + " category=" + category + " messageId=" + ConnectionDiagnostics.Fingerprint(line)
                    + " failureReason=" + failureReason
                    + (category == "error" || category == "pool-selection-error" ? " failureMembers=" + CoreRoutingSignal.FailureMembers(line) : "")
                    + (routingSignal ? " " + routingDetails : "")
                    + " text=\"" + safeText + "\"");
                if (category == "network-changed" || category == "interface" || category == "fatal" || category == "panic")
                    ConnectionDiagnostics.RequestSnapshot();
            }
            catch { }
        }
        private void DiagnosticExit(Process process)
        {
            try
            {
                string[] tail;
                lock (diagnosticOutputLock) tail = diagnosticTail.ToArray();
                for (int i = 0; i < tail.Length; i++)
                    ConnectionDiagnostics.Write("core-exit-tail", "readerPid=" + DiagnosticPid(process) + " tailIndex=" + i + " " + tail[i]);
                Diagnostic("core-exit", "pid=" + DiagnosticPid(process) + " exitCode=" + process.ExitCode
                    + " exitHex=0x" + unchecked((uint)process.ExitCode).ToString("X8")
                    + " suppressed=" + suppressCoreExit + " userCanceled=" + userCancelRequested
                    + " reconnecting=" + reconnecting + " " + CoreDiagnosticMetadata.ProcessState(CorePort));
                ConnectionDiagnostics.RequestSnapshot();
            }
            catch (Exception ex)
            {
                ConnectionDiagnostics.Write("core-exit-read-error", "readerService=" + diagnosticServiceId
                    + " pid=" + DiagnosticPid(process) + " exception=" + ex.GetType().Name + " " + ActiveDiagnosticState());
            }
        }
    }
}

