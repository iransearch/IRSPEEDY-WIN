using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IRSpeedyVPN.WebServices
{
    internal sealed partial class CurlHelper
    {
        private const int ProcessCleanupTimeoutMs = 2000;
        private static readonly object PendingStopGate = new object();
        private static readonly List<Process> PendingStops = new List<Process>();

        private static bool HasStopped(Process process)
        { try { return process.HasExited; } catch { return false; } }

        private static void RememberPendingStop(Process process)
        { lock (PendingStopGate) if (!PendingStops.Contains(process)) PendingStops.Add(process); }

        private static bool IsPendingStop(Process process)
        { lock (PendingStopGate) return PendingStops.Contains(process); }

        private static void StopCurlProcess(Process process)
        {
            try { process.Kill(); } catch { }
            bool stopped = false;
            // The timed overload waits only for the process, not inherited pipe
            // handles. Never follow this with the unbounded overload.
            try { stopped = process.WaitForExit(ProcessCleanupTimeoutMs); }
            catch { stopped = HasStopped(process); }
            if (!stopped) RememberPendingStop(process);
        }

        internal static void EnsureCredentialCurlStopped()
        {
            lock (PendingStopGate)
            {
                for (int i = PendingStops.Count - 1; i >= 0; i--)
                {
                    var process = PendingStops[i];
                    if (!HasStopped(process)) continue;
                    PendingStops.RemoveAt(i);
                    process.Dispose();
                }
                // Retain the actual process/handle until confirmed dead, rather
                // than trusting a reusable PID. No reaper thread is started.
                if (PendingStops.Count != 0) throw new LoginTransportTimeoutException();
            }
        }

        private static void CloseCurlReaders(Process process)
        {
            try { process.CancelOutputRead(); } catch { }
            try { process.CancelErrorRead(); } catch { }
            try { process.StandardOutput.Close(); } catch { }
            try { process.StandardError.Close(); } catch { }
        }
    }
}
