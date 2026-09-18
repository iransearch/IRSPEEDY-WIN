using IRSpeedyVPN.Common;
using System;
using System.Diagnostics;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private readonly string diagnosticServiceId = Guid.NewGuid().ToString("N");
        private string diagnosticConnectionId = "none";
        private long diagnosticOutputWindow;
        private int diagnosticOutputCount;
        private readonly object diagnosticOutputLock = new object();

        private static int DiagnosticPid(Process process)
        {
            try { return process?.Id ?? -1; } catch { return -1; }
        }
        private void Diagnostic(string stage, string details = "")
        {
            try
            {
                ConnectionDiagnostics.Start();
                var active = gInfo?.CurrentService as TunnelPlusService;
                string activeMode = active == null ? "unknown-or-idle" : !active.IsConnected ? "not-connected"
                    : active.lastVpnMode ? "TUN" : "Proxy";
                ConnectionDiagnostics.ObserveMode(activeMode);
                ConnectionDiagnostics.Write(stage, "service=" + diagnosticServiceId
                    + " connection=" + diagnosticConnectionId + " countryId=" + ID + " countryIndex=" + CountryIndex
                    + " selectedMode=" + (RegHelper.GetSettingValue("VGAURDVPNMode") == "0" ? "Proxy" : "TUN")
                    + " gameMode=" + (RegHelper.GetSettingValue("VGAURDGameMode") == "1")
                    + " activeMode=" + activeMode + " activeConnection=" + (active?.diagnosticConnectionId ?? "none")
                    + " serviceConnected=" + IsConnected + " appliedMode=" + (IsConnected ? (lastVpnMode ? "TUN" : "Proxy") : "not-connected")
                    + " systemProxyRequested=" + useSystemProxy + " proxifierRule=" + (int)ProxifierRuleType
                    + " listenPort=" + lastListenPort + " corePid=" + DiagnosticPid(coreProcess)
                    + " coreOwned=" + coreOwned + " controlPort=" + CorePort + " " + details);
            }
            catch { }
        }
        private void ObserveCoreOutput(Process process, string source, string line)
        {
            try
            {
                if (string.IsNullOrEmpty(line)) return;
                string lower = line.ToLowerInvariant();
                string category = lower.Contains("network changed") ? "network-changed"
                    : lower.Contains("panic") ? "panic" : lower.Contains("fatal") ? "fatal"
                    : lower.Contains("interface") ? "interface" : lower.Contains("route") ? "route"
                    : lower.Contains("tun") ? "tun" : lower.Contains("error") ? "error" : null;
                if (category == null) return;
                lock (diagnosticOutputLock)
                {
                    long window = Stopwatch.GetTimestamp() / (5 * Stopwatch.Frequency);
                    if (window != diagnosticOutputWindow) { diagnosticOutputWindow = window; diagnosticOutputCount = 0; }
                    if (++diagnosticOutputCount > 10) return;
                }
                // Output may contain full configs or credentials: log only an allowlisted category and salted ID.
                Diagnostic("core-output", "pid=" + DiagnosticPid(process) + " source=" + source
                    + " category=" + category + " messageId=" + ConnectionDiagnostics.Fingerprint(line));
                ConnectionDiagnostics.RequestSnapshot();
            }
            catch { }
        }
        private void DiagnosticExit(Process process)
        {
            try
            {
                Diagnostic("core-exit", "pid=" + DiagnosticPid(process) + " exitCode=" + process.ExitCode
                    + " exitHex=0x" + unchecked((uint)process.ExitCode).ToString("X8")
                    + " suppressed=" + suppressCoreExit + " userCanceled=" + userCancelRequested
                    + " reconnecting=" + reconnecting);
                ConnectionDiagnostics.RequestSnapshot();
            }
            catch { }
        }
    }
}
