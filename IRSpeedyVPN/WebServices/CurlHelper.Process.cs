using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    internal sealed partial class CurlHelper
    {
        private const int ProcessCleanupTimeoutMs = 2000;
        private static readonly object PendingStopGate = new object();
        private sealed class PendingStop
        {
            internal readonly Process Process;
            internal bool OwnerReleased;
            internal PendingStop(Process process) { Process = process; }
        }
        private static readonly List<PendingStop> PendingStops = new List<PendingStop>();
        private static Timer pendingStopTimer;
        private static bool pendingStopShutdown;

        static CurlHelper()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => ShutdownPendingCurlStops();
            AppDomain.CurrentDomain.DomainUnload += (sender, args) => ShutdownPendingCurlStops();
        }

        private static bool HasStopped(Process process)
        { try { return process.HasExited; } catch { return false; } }

        private static void RememberPendingStop(Process process)
        {
            lock (PendingStopGate)
            {
                if (pendingStopShutdown) return; // The executing owner still disposes in finally.
                if (!PendingStops.Exists(entry => ReferenceEquals(entry.Process, process)))
                    PendingStops.Add(new PendingStop(process));
            }
        }

        // Called only after ExecuteCurl detaches callbacks and closes readers.
        // The timer must never dispose a process still owned by that finally.
        private static void ReleaseCurlProcess(Process process)
        {
            lock (PendingStopGate)
            {
                var entry = PendingStops.Find(item => ReferenceEquals(item.Process, process));
                if (entry == null) { DisposeCurlProcess(process); return; }
                entry.OwnerReleased = true;
                PrunePendingStops();
                if (PendingStops.Count != 0 && pendingStopTimer == null)
                    pendingStopTimer = new Timer(_ => CleanupPendingCurlStops(), null, 1000, 1000);
            }
        }

        private static void DisposeCurlProcess(Process process)
        { try { process.Dispose(); } catch { } }

        // All callers hold PendingStopGate. One shared ThreadPool timer exists
        // only while handles are retained; no dedicated polling thread is used.
        private static void PrunePendingStops()
        {
            for (int i = PendingStops.Count - 1; i >= 0; i--)
            {
                var entry = PendingStops[i];
                if (!entry.OwnerReleased || !HasStopped(entry.Process)) continue;
                PendingStops.RemoveAt(i);
                DisposeCurlProcess(entry.Process);
            }
            if (PendingStops.Count == 0 && pendingStopTimer != null)
            {
                var timer = pendingStopTimer;
                pendingStopTimer = null;
                timer.Dispose();
            }
        }

        private static void CleanupPendingCurlStops()
        { lock (PendingStopGate) PrunePendingStops(); }

        internal static void ShutdownPendingCurlStops()
        {
            lock (PendingStopGate)
            {
                pendingStopShutdown = true;
                if (pendingStopTimer != null)
                {
                    pendingStopTimer.Dispose();
                    pendingStopTimer = null;
                }
                foreach (var entry in PendingStops)
                    if (entry.OwnerReleased) DisposeCurlProcess(entry.Process);
                // Active owners still close their readers and Dispose in finally;
                // late timeout registrations cannot reintroduce a retained handle.
                PendingStops.Clear();
            }
        }

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
                PrunePendingStops();
                // Retain the actual process/handle until confirmed dead, rather
                // than trusting a reusable PID. No reaper thread is started.
                if (pendingStopShutdown || PendingStops.Count != 0) throw new LoginTransportTimeoutException();
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
