using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal sealed class InternetAvailabilityChangedEventArgs : EventArgs
    {
        internal InternetAvailabilityChangedEventArgs(bool isOnline, string reason)
        {
            IsOnline = isOnline;
            Reason = reason ?? string.Empty;
        }

        internal bool IsOnline { get; }
        internal string Reason { get; }
    }

    /// <summary>
    /// User-facing connectivity watcher.
    /// Every ten minutes it sends four ICMP pings to 185.143.233.200.
    /// Any successful reply marks the cycle online. Four failures trigger an
    /// offline notification on every failed cycle, even if the previous cycle
    /// was already offline. The watcher never changes VPN/Core state.
    /// </summary>
    internal sealed class InternetConnectivityMonitor : IDisposable
    {
        private const string PingHost = "185.143.233.200";
        private const int AttemptsPerCycle = 4;
        private const int PingTimeoutMs = 2500;

        private readonly SemaphoreSlim checkGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Timer timer;
        private int started;
        private bool? lastOnline;

        internal event EventHandler<InternetAvailabilityChangedEventArgs> AvailabilityChanged;

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
        }

        private void NetworkChange_NetworkAvailabilityChanged(
            object sender, NetworkAvailabilityEventArgs e)
        {
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
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    PublishOffline("no-active-network-interface");
                    return;
                }

                for (var attempt = 1; attempt <= AttemptsPerCycle; attempt++)
                {
                    if (lifetime.IsCancellationRequested)
                        return;

                    var success = await PingOnceAsync(attempt).ConfigureAwait(false);
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
                PublishOffline("irancell-ping-4-of-4-failed");
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

        private static async Task<bool> PingOnceAsync(int attempt)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(PingHost, PingTimeoutMs)
                        .ConfigureAwait(false);
                    var success = reply != null && reply.Status == IPStatus.Success;

                    Common.LogHelper.WriteExLog(
                        "[NetworkMonitor] host=" + PingHost +
                        " attempt=" + attempt + "/" + AttemptsPerCycle +
                        " status=" + (reply == null ? "null" : reply.Status.ToString()) +
                        (success ? " rttMs=" + reply.RoundtripTime : string.Empty));

                    return success;
                }
            }
            catch (Exception ex)
            {
                Common.LogHelper.WriteExLog(
                    "[NetworkMonitor] host=" + PingHost +
                    " attempt=" + attempt + "/" + AttemptsPerCycle +
                    " error=" + ex.GetType().Name);
                return false;
            }
        }

        private void PublishOnline(string reason)
        {
            bool changed;
            lock (this)
            {
                changed = !lastOnline.HasValue || !lastOnline.Value;
                lastOnline = true;
            }

            Common.LogHelper.WriteExLog(
                "[NetworkMonitor] online=true reason=" + reason);

            // Online is state-based: it only needs to close an existing toast once.
            if (changed)
            {
                AvailabilityChanged?.Invoke(
                    this, new InternetAvailabilityChangedEventArgs(true, reason));
            }
        }

        private void PublishOffline(string reason)
        {
            lock (this)
            {
                lastOnline = false;
            }

            Common.LogHelper.WriteExLog(
                "[NetworkMonitor] online=false reason=" + reason);

            // Offline is cycle-based by request: every confirmed failed cycle
            // produces a notification, including consecutive failed cycles.
            AvailabilityChanged?.Invoke(
                this, new InternetAvailabilityChangedEventArgs(false, reason));
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
        }
    }
}