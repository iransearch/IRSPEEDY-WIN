using System;
using System.Net;
using System.Net.Http;
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
    /// Lightweight connectivity watcher used only for the user-facing toast.
    /// It never blocks the dispatcher and never changes VPN/Core state.
    /// </summary>
    internal sealed class InternetConnectivityMonitor : IDisposable
    {
        private static readonly Uri PrimaryProbe =
            new Uri("https://connectivitycheck.gstatic.com/generate_204");
        private static readonly Uri CloudflareProbe =
            new Uri("https://cp.cloudflare.com/generate_204");
        private static readonly Uri MicrosoftProbe =
            new Uri("https://www.msftconnecttest.com/connecttest.txt");

        private readonly HttpClient client;
        private readonly SemaphoreSlim checkGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Timer timer;
        private int started;
        private bool? lastOnline;

        internal InternetConnectivityMonitor()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "IRSPEEDY-NetworkMonitor/1.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cache-Control", "no-cache");
        }

        internal event EventHandler<InternetAvailabilityChangedEventArgs> AvailabilityChanged;

        internal void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;

            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
            timer = new Timer(_ => QueueCheck(), null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        }

        private void NetworkChange_NetworkAvailabilityChanged(
            object sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                Publish(false, "windows-network-unavailable");
                return;
            }

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
                    Publish(false, "no-active-network-interface");
                    return;
                }

                if (await ProbeAsync(PrimaryProbe, lifetime.Token).ConfigureAwait(false))
                {
                    Publish(true, "primary-probe");
                    return;
                }

                // Do not report one transient HTTP failure as an outage. Let route/DNS
                // changes settle, then confirm against two independent endpoints.
                await Task.Delay(1200, lifetime.Token).ConfigureAwait(false);

                var cloudflare = ProbeAsync(CloudflareProbe, lifetime.Token);
                var microsoft = ProbeAsync(MicrosoftProbe, lifetime.Token);
                var results = await Task.WhenAll(cloudflare, microsoft).ConfigureAwait(false);
                var online = results[0] || results[1];

                Publish(online, online ? "fallback-probe" : "confirmed-probe-failure");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Common.LogHelper.WriteExLog(
                    "[NetworkMonitor] check exception=" + ex.GetType().Name);
            }
            finally
            {
                checkGate.Release();
            }
        }

        private async Task<bool> ProbeAsync(Uri uri, CancellationToken cancellationToken)
        {
            using (var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromMilliseconds(2500));

                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false))
                    {
                        var code = (int)response.StatusCode;
                        return code >= 200 && code < 400;
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void Publish(bool isOnline, string reason)
        {
            bool changed;
            lock (this)
            {
                changed = !lastOnline.HasValue || lastOnline.Value != isOnline;
                lastOnline = isOnline;
            }

            if (!changed)
                return;

            Common.LogHelper.WriteExLog(
                "[NetworkMonitor] online=" + isOnline + " reason=" + reason);

            AvailabilityChanged?.Invoke(
                this, new InternetAvailabilityChangedEventArgs(isOnline, reason));
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
            // A queued check may still be unwinding after cancellation. Keep the small
            // synchronization primitives alive until process teardown so its finally
            // block can release safely without racing ObjectDisposedException.
            client.Dispose();
        }
    }
}