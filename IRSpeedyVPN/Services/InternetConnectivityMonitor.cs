using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal sealed class InternetAvailabilityChangedEventArgs : EventArgs
    {
        internal InternetAvailabilityChangedEventArgs(bool isOnline, string reason, bool notifyUser = true,
            bool affectsRecovery = true)
        {
            IsOnline = isOnline;
            Reason = reason ?? string.Empty;
            NotifyUser = notifyUser;
            AffectsRecovery = affectsRecovery;
        }

        internal bool IsOnline { get; }
        internal string Reason { get; }
        internal bool NotifyUser { get; }
        internal bool AffectsRecovery { get; }
    }

    /// <summary>
    /// User-facing connectivity watcher.
    /// Every ten minutes it sends four ICMP pings to 185.143.233.200.
    /// Any successful reply marks the cycle online. Four failures trigger an
    /// offline notification on every failed cycle. A lightweight fifteen-second
    /// signal while connected/offline also detects WAN recovery without an OS
    /// adapter event. Fast signals do not produce additional notifications.
    /// </summary>
    internal sealed class InternetConnectivityMonitor : IDisposable
    {
        internal const string PingHost = "185.143.233.200";
        internal const string PingCidr = "185.143.233.200/32";
        private const int AttemptsPerCycle = 4;
        private const int PingTimeoutMs = 2500;

        private readonly SemaphoreSlim checkGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Timer timer;
        private Timer recoveryTimer;
        private readonly Func<bool> needsFastChecks;
        private readonly Func<int, Task<bool>> pingOnce;
        private readonly Func<bool> networkAvailable;
        private readonly Func<Task<bool>> confirmUsablePath;
        private int fastFailures;
        private int networkRevision;
        private int started;
        private bool? lastOnline;
        private static int knownOnline = -1;
        internal static bool? LastKnownOnline
        {
            get { int value = Volatile.Read(ref knownOnline); return value < 0 ? (bool?)null : value == 1; }
        }

        internal event EventHandler<InternetAvailabilityChangedEventArgs> AvailabilityChanged;

        internal InternetConnectivityMonitor(Func<bool> needsFastChecks = null,
            Func<int, Task<bool>> pingOnce = null, Func<bool> networkAvailable = null,
            Func<Task<bool>> confirmUsablePath = null)
        {
            this.needsFastChecks = needsFastChecks ?? (() => false);
            this.pingOnce = pingOnce ?? PingOnceAsync;
            this.networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
            this.confirmUsablePath = confirmUsablePath ?? (() => Task.FromResult(false));
        }

        internal void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;

            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;

            // Run once at startup, then exactly once every ten minutes.
            timer = new Timer(
                _ => QueueCheck(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(10));
            recoveryTimer = new Timer(_ => Task.Run(CheckFastAsync), null,
                TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        private void NetworkChange_NetworkAvailabilityChanged(
            object sender, NetworkAvailabilityEventArgs e)
        {
            Interlocked.Increment(ref networkRevision);
            if (!e.IsAvailable)
            {
                PublishOffline("windows-network-unavailable");
                return;
            }

            // A network adapter just came back. Verify 185.143.233.200 instead of
            // assuming that local network availability means internet access.
            QueueCheck();
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            Interlocked.Increment(ref networkRevision);
            QueueCheck();
        }

        private void QueueCheck()
        {
            if (lifetime.IsCancellationRequested)
                return;

            Task.Run(CheckAsync);
        }

        private async Task CheckAsync()
        {
            if (!checkGate.Wait(0))
                return;

            try
            {
                int revision = Volatile.Read(ref networkRevision);
                if (!networkAvailable())
                {
                    PublishOffline("no-active-network-interface");
                    return;
                }

                for (var attempt = 1; attempt <= AttemptsPerCycle; attempt++)
                {
                    if (lifetime.IsCancellationRequested)
                        return;

                    var success = await pingOnce(attempt).ConfigureAwait(false);
                    if (lifetime.IsCancellationRequested || revision != Volatile.Read(ref networkRevision)) return;
                    if (success)
                    {
                        PublishOnline("irancell-ping-success-" + attempt);
                        return;
                    }

                    if (attempt < AttemptsPerCycle)
                        await Task.Delay(350, lifetime.Token).ConfigureAwait(false);
                }

                // Important: unlike normal state-change publishing, a failed
                // four-ping cycle is intentionally raised every ten minutes.
                bool usablePath = needsFastChecks() && await confirmUsablePath().ConfigureAwait(false);
                if (lifetime.IsCancellationRequested || revision != Volatile.Read(ref networkRevision)) return;
                if (usablePath)
                {
                    // Retain the requested four-ping notification, but a fixed
                    // ICMP target cannot suspend a VPN path verified by TLS.
                    PublishOnline("external-path-confirmed");
                    AvailabilityChanged?.Invoke(this, new InternetAvailabilityChangedEventArgs(
                        false, "irancell-ping-4-of-4-failed", true, false));
                }
                else PublishOffline("irancell-ping-4-of-4-failed");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Common.LogHelper.WriteExLog(
                    "[NetworkMonitor] cycle exception=" + ex.GetType().Name);
            }
            finally
            {
                checkGate.Release();
            }
        }

        internal async Task CheckFastAsync()
        {
            if (lifetime.IsCancellationRequested || (!needsFastChecks() && LastKnownOnline != false)
                || !checkGate.Wait(0)) return;
            try
            {
                int revision = Volatile.Read(ref networkRevision);
                if (!networkAvailable())
                {
                    PublishOffline("fast-no-network-interface", false);
                    return;
                }
                bool success = await pingOnce(0).ConfigureAwait(false);
                if (lifetime.IsCancellationRequested || revision != Volatile.Read(ref networkRevision)) return;
                if (success)
                {
                    fastFailures = 0;
                    PublishOnline("fast-direct-ping-success");
                }
                else if (++fastFailures >= AttemptsPerCycle)
                {
                    bool usable = await confirmUsablePath().ConfigureAwait(false);
                    if (lifetime.IsCancellationRequested || revision != Volatile.Read(ref networkRevision)) return;
                    if (usable) PublishOnline("external-path-confirmed");
                    else PublishOffline("fast-direct-ping-4-failed", false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Common.LogHelper.WriteExLog("[NetworkMonitor] fast-check exception=" + ex.GetType().Name);
            }
            finally { checkGate.Release(); }
        }

        private static async Task<bool> PingOnceAsync(int attempt)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(PingHost, PingTimeoutMs)
                        .ConfigureAwait(false);
                    var success = reply != null && reply.Status == IPStatus.Success;

                    if (attempt != 0) Common.LogHelper.WriteExLog(
                        "[NetworkMonitor] host=" + PingHost +
                        " attempt=" + attempt + "/" + AttemptsPerCycle +
                        " status=" + (reply == null ? "null" : reply.Status.ToString()) +
                        (success ? " rttMs=" + reply.RoundtripTime : string.Empty));

                    return success;
                }
            }
            catch (Exception ex)
            {
                if (attempt != 0) Common.LogHelper.WriteExLog(
                    "[NetworkMonitor] host=" + PingHost +
                    " attempt=" + attempt + "/" + AttemptsPerCycle +
                    " error=" + ex.GetType().Name);
                return false;
            }
        }

        private void PublishOnline(string reason)
        {
            fastFailures = 0;
            bool changed;
            lock (this)
            {
                changed = !lastOnline.HasValue || !lastOnline.Value;
                lastOnline = true;
                Volatile.Write(ref knownOnline, 1);
            }

            if (changed) Common.LogHelper.WriteExLog(
                "[NetworkMonitor] online=true reason=" + reason);

            // Online is state-based: it only needs to close an existing toast once.
            if (changed)
            {
                AvailabilityChanged?.Invoke(
                    this, new InternetAvailabilityChangedEventArgs(true, reason));
            }
        }

        private void PublishOffline(string reason, bool notifyUser = true)
        {
            bool changed;
            lock (this)
            {
                changed = !lastOnline.HasValue || lastOnline.Value;
                lastOnline = false;
                Volatile.Write(ref knownOnline, 0);
            }

            if (changed || notifyUser) Common.LogHelper.WriteExLog(
                "[NetworkMonitor] online=false reason=" + reason);

            // Offline is cycle-based by request: every confirmed failed cycle
            // produces a notification, including consecutive failed cycles.
            if (changed || notifyUser) AvailabilityChanged?.Invoke(
                this, new InternetAvailabilityChangedEventArgs(false, reason, notifyUser));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref started, 0) != 0)
            {
                NetworkChange.NetworkAvailabilityChanged -=
                    NetworkChange_NetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
            }

            lifetime.Cancel();
            timer?.Dispose();
            timer = null;
            recoveryTimer?.Dispose();
            recoveryTimer = null;
        }
    }
}
