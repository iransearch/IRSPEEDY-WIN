using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Hotspot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService : IHotspotSource
    {
        private int hotspotTunReady;
        private long hotspotGeneration;
        private bool PauseSharingBeforeCoreRestart(bool preserveRequest = true)
        {
            // Invalidate first, then synchronously drain Start/Stop under the coordinator gate.
            // This must remain outside RPC try/catch blocks that swallow Stop failures.
            return DirectHotspot.Controller.Pause(this, preserveRequest, () =>
            {
                Volatile.Write(ref hotspotTunReady, 0);
                Interlocked.Increment(ref hotspotGeneration);
            }, sharedCore: true);
        }
        private void ResumeSharingAfterCoreStart()
        {
            if (!ReferenceEquals(gInfo.CurrentService, this)) return;
            Volatile.Write(ref hotspotTunReady, lastVpnMode ? 1 : 0);
            if (lastVpnMode)
                DirectHotspot.Controller.Ready(this);
            else
                DirectHotspot.Controller.Pause(this, false);
        }
        TunContext IHotspotSource.CaptureTun()
        {
            long generation = Interlocked.Read(ref hotspotGeneration);
            if (Volatile.Read(ref hotspotTunReady) == 0 || !IsConnected || !lastVpnMode ||
                !ReferenceEquals(gInfo.CurrentService, this)) return null;
            try
            {
                // The RPC listener can be reused by another service instance. The diagnostic
                // registry tracks that listener's process, never the UI or an arbitrary name match.
                int pid = CoreDiagnosticMetadata.Pid(CorePort);
                if (pid <= 0) return null;
                using (var process = Process.GetProcessById(pid))
                {
                    if (process.HasExited) return null;
                    var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(a =>
                        a.OperationalStatus == OperationalStatus.Up &&
                        string.Equals(a.Name, "irspeedy-tun", StringComparison.OrdinalIgnoreCase)).ToArray();
                    Guid id;
                    if (adapters.Length != 1 || !Guid.TryParse(adapters[0].Id, out id)) return null;
                    var result = new TunContext { Id = id, Pid = pid,
                        StartedUtcTicks = process.StartTime.ToUniversalTime().Ticks, Generation = generation };
                    if (Volatile.Read(ref hotspotTunReady) == 0 || generation != Interlocked.Read(ref hotspotGeneration)) return null;
                    return result;
                }
            }
            catch { return null; }
        }
    }
}
