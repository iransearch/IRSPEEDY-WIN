using IRSpeedyVPN.Common;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private readonly ConnectionHealthState pathHealth = new ConnectionHealthState();
        private CancellationTokenSource healthCancellation;
        private int healthRecoveryAttempts;
        internal string VerifiedPublicIp => pathHealth.Address;
        internal string PathHealthStatus => pathHealth.Status;

        private void StartPathMonitor()
        {
            StopPathMonitor();
            var cancellation = new CancellationTokenSource();
            healthCancellation = cancellation;
            NetworkChange.NetworkAddressChanged += PathNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += PathAvailabilityChanged;
            long generation = Interlocked.Read(ref connectionGeneration);
            Task.Run(() => MonitorPathAsync(cancellation, generation));
        }

        private void StopPathMonitor()
        {
            NetworkChange.NetworkAddressChanged -= PathNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= PathAvailabilityChanged;
            var cancellation = Interlocked.Exchange(ref healthCancellation, null);
            // The worker owns disposal after the last request has completed.
            if (cancellation != null) cancellation.Cancel();
            pathHealth.Invalidate("در حال بررسی اتصال");
        }

        private void PathNetworkChanged(object sender, EventArgs e)
        {
            pathHealth.Invalidate("شبکه تغییر کرده؛ در حال بررسی اتصال");
        }
        private void PathAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            PathNetworkChanged(sender, e);
        }

        private bool IsCurrentHealthSession(CancellationTokenSource cancellation, long generation)
        {
            return !cancellation.IsCancellationRequested && !userCancelRequested && !applicationExiting
                && ReferenceEquals(healthCancellation, cancellation)
                && ReferenceEquals(gInfo.CurrentService, this)
                && generation == Interlocked.Read(ref connectionGeneration);
        }

        private static bool HasNetworkGateway()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                && !string.Equals(n.Name, "irspeedy-tun", StringComparison.OrdinalIgnoreCase)
                && n.GetIPProperties().GatewayAddresses.Any(g =>
                    !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)));
        }

        private async Task MonitorPathAsync(CancellationTokenSource cancellation, long generation)
        {
            long appliedRevision = -1;
            try
            {
                while (IsCurrentHealthSession(cancellation, generation))
                {
                    long revision = pathHealth.Revision;
                    bool available = false;
                    string ip = null;
                    try
                    {
                        available = HasNetworkGateway();
                        if (available)
                        {
                            // Serialize mutations with disconnect. A late monitor can
                            // never re-enable the proxy after the user pressed Disconnect.
                            lock (connectionLifecycleGate)
                            {
                                if (!IsCurrentHealthSession(cancellation, generation)) return;
                                if (useSystemProxy && !lastVpnMode &&
                                    (appliedRevision != revision || !IsExpectedSystemProxy()))
                                {
                                    WinINet.SetIEProxy(true, true, "http://127.0.0.1:" + lastListenPort, null);
                                    Diagnostic("health-proxy-reapplied", "networkRevision=" + revision);
                                }
                                appliedRevision = revision;
                            }
                            ip = await ProbePathAsync(lastListenPort, cancellation.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) return; }
                    catch (Exception ex) { Diagnostic("health-probe-error", "exception=" + ex.GetType().Name); }

                    lock (connectionLifecycleGate)
                    {
                        if (!IsCurrentHealthSession(cancellation, generation)) return;
                        bool recover = pathHealth.Complete(revision, ip, available);
                        if (revision == pathHealth.Revision)
                        {
                            Diagnostic("health-result", "healthy=" + (ip != null) + " networkAvailable=" + available);
                            if (ip != null) healthRecoveryAttempts = 0;
                            if (recover)
                            {
                                if (++healthRecoveryAttempts > 3)
                                {
                                    StopPathMonitor();
                                    onConnectDisconnect?.Invoke(this, false, 0, "مسیر اتصال پاسخ نمی‌دهد؛ دوباره متصل شوید.");
                                }
                                else TryReconnect();
                                return;
                            }
                        }
                    }
                    await Task.Delay(ip == null ? 3000 : 10000, cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Disconnect/reconnect may already have installed a newer monitor.
                lock (connectionLifecycleGate)
                {
                    if (ReferenceEquals(healthCancellation, cancellation))
                    {
                        healthCancellation = null;
                        NetworkChange.NetworkAddressChanged -= PathNetworkChanged;
                        NetworkChange.NetworkAvailabilityChanged -= PathAvailabilityChanged;
                    }
                    cancellation.Dispose();
                }
            }
        }

        private bool IsExpectedSystemProxy()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                string server = Convert.ToString(key?.GetValue("ProxyServer"));
                return Convert.ToInt32(key?.GetValue("ProxyEnable", 0)) == 1
                    && (server == "127.0.0.1:" + lastListenPort || server == "http://127.0.0.1:" + lastListenPort);
            }
        }

        internal static async Task<string> ProbePathAsync(int port, CancellationToken cancellation)
        {
            // Explicit proxy; never use the OS proxy, a PAC script, or a direct retry.
            using (var handler = new HttpClientHandler
            {
                Proxy = new WebProxy("http://127.0.0.1:" + port), UseProxy = true,
                AllowAutoRedirect = false, UseCookies = false
            })
            using (var client = new HttpClient(handler)
            { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 128 })
            {
                foreach (var url in new[] { "https://api.ipify.org", "https://checkip.amazonaws.com" })
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        using (var response = await client.GetAsync(url, cancellation).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            var value = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                            IPAddress address;
                            if (IPAddress.TryParse(value, out address)) return address.ToString();
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
                }
                return null;
            }
        }
    }
}
