using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Services;
using QRCoder;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Windows
{
    public partial class ShareVPNSetting : Window
    {
        public IVPNService Service { get; set; }
        private bool proxyBusy;
        private string proxyIp;
        private string proxyError;
        private bool proxyRefreshPending, proxyListenerActive;
        private int proxyNetworkVersion, proxySnapshotPort;
        private object proxySnapshotService;
        public ShareVPNSetting() { InitializeComponent(); }
        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeHotspotUi();
            RefreshProxyUi();
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (proxyBusy || hotspotBusy) e.Cancel = true;
            if (!e.Cancel) QrPopup.IsOpen = false;
            base.OnClosing(e);
        }
        private void ProxyTab_Click(object sender, RoutedEventArgs e) => SelectTab(false);
        private void DirectTab_Click(object sender, RoutedEventArgs e) => SelectTab(true);
        private void SelectTab(bool direct)
        {
            // Guard programmatic selection as well as the disabled mouse/keyboard tab.
            if (direct && !DirectTab.IsEnabled) direct = false;
            if ((DirectPanel.Visibility == Visibility.Visible) == direct) return;
            DirectPanel.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
            ProxyPanel.Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
            DirectTab.Tag = direct ? "Selected" : null;
            ProxyTab.Tag = direct ? null : "Selected";
            DirectTab.Background = direct ? Brushes.White : Brushes.Transparent;
            ProxyTab.Background = direct ? Brushes.Transparent : Brushes.White;
            DirectTab.Foreground = direct ? new SolidColorBrush(Color.FromRgb(20, 27, 51)) : new SolidColorBrush(Color.FromRgb(156, 163, 180));
            ProxyTab.Foreground = direct ? new SolidColorBrush(Color.FromRgb(156, 163, 180)) : new SolidColorBrush(Color.FromRgb(20, 27, 51));
            QrPopup.IsOpen = false;
            // Tab selection only changes presentation; network queries run on a worker.
            AnimatePanel(direct ? DirectPanel : ProxyPanel, direct ? -8 : 8);
        }
        private static void AnimatePanel(FrameworkElement panel, double offset)
        {
            panel.BeginAnimation(OpacityProperty, null);
            var shift = new TranslateTransform();
            panel.RenderTransform = shift;
            if (!SystemParameters.ClientAreaAnimation) return;
            var duration = TimeSpan.FromMilliseconds(180);
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, duration) { FillBehavior = FillBehavior.Stop });
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        private bool ProxyAvailable => Service is TunnelPlusService tunnel && tunnel.IsTunnelConnected
            && ReferenceEquals(AppServices.GlobalInfo?.CurrentService, Service);
        private bool ProxyListening() => ProxyAvailable && Service.IsShareActive && proxyListenerActive && !proxyBusy
            && ReferenceEquals(Service, proxySnapshotService) && Service.HttpPort == proxySnapshotPort;
        private async void RefreshProxyUi()
        {
            if (!IsLoaded || proxyBusy || proxyRefreshPending) return;
            var service = Service;
            int version = proxyNetworkVersion;
            bool available = ProxyAvailable;
            bool sharing = available && service.IsShareActive;
            int port = service?.HttpPort ?? 0;
            bool active = false;
            string address = null;
            proxyRefreshPending = true;
            try
            {
                if (sharing && port > 0)
                {
                    // Adapter/driver and listener enumeration can block; never do it on the dispatcher.
                    await Task.Run(() =>
                    {
                        address = GetInternetInterfaceIp();
                        if (string.IsNullOrEmpty(address)) return;
                        try
                        {
                            active = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port
                                && (IPAddress.Any.Equals(p.Address) || IPAddress.IPv6Any.Equals(p.Address) || p.Address.ToString() == address));
                        }
                        catch (NetworkInformationException) { }
                    });
                }
            }
            catch { active = false; }
            finally { proxyRefreshPending = false; }
            // A delayed snapshot must not overwrite a toggle or a changed VPN session.
            if (!IsLoaded || proxyBusy || version != proxyNetworkVersion || !ReferenceEquals(service, Service) || available != ProxyAvailable
                || sharing != (ProxyAvailable && Service.IsShareActive) || port != (Service?.HttpPort ?? 0)) return;
            proxyIp = address;
            proxyListenerActive = active;
            proxySnapshotService = service;
            proxySnapshotPort = port;
            btnStartStop.IsChecked = ProxyAvailable && Service.IsShareActive;
            btnStartStop.IsEnabled = ProxyAvailable && !hotspotBusy;
            pnlShowIP.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            ProxyMotion.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            ProxyOffHint.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            ProxyStepTwo.Opacity = active ? 1 : 0.45;
            HTTPAddress.Text = active ? proxyIp + " : " + Service.HttpPort : "";
            SOCKS5Address.Text = active ? proxyIp + " : " + Service.SocksPort : "";
            ProxyStatus.Text = !ProxyAvailable ? "ابتدا به سرویس سازگار متصل شوید" : proxyError ??
                (active ? "فعال" : Service.IsShareActive ? "آدرس شبکه یا پراکسی فعال تأیید نشد" : "غیرفعال");
            ProxyStatus.Foreground = active ? new SolidColorBrush(Color.FromRgb(23, 171, 119)) : Brushes.Gray;
        }
        private async void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (proxyBusy || hotspotBusy || !ProxyAvailable) { RefreshProxyUi(); return; }
            bool requested = btnStartStop.IsChecked == true;
            proxyBusy = true;
            proxyNetworkVersion++;
            proxyError = null;
            proxyListenerActive = false;
            btnStartStop.IsEnabled = false;
            pnlShowIP.Visibility = ProxyMotion.Visibility = Visibility.Collapsed;
            ProxyStatus.Text = "در حال اعمال…";
            RefreshHotspotUi();
            try
            {
                Service.IsShareActive = requested;
                await Task.Run(() => Service.ApplyShareSetting());
            }
            catch { proxyError = "اعمال تنظیم انجام نشد؛ دوباره تلاش کنید."; }
            finally { proxyBusy = false; RefreshProxyUi(); RefreshHotspotUi(); }
        }
        private string ProxyUri(string type) => type == "HTTP"
            ? "http://" + proxyIp + ":" + Service.HttpPort
            : "socks5://" + proxyIp + ":" + Service.SocksPort;
        private void CopyProxy_Click(object sender, RoutedEventArgs e)
        {
            if (!ProxyListening()) return;
            try { Clipboard.SetText(ProxyUri((string)((Button)sender).Tag)); ProxyStatus.Text = "کپی شد"; }
            catch { ProxyStatus.Text = "کپی انجام نشد؛ دوباره تلاش کنید."; }
        }
        private void Qr_Click(object sender, RoutedEventArgs e)
        {
            if (!ProxyListening()) return;
            var uri = ProxyUri((string)((Button)sender).Tag);
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q))
            using (var code = new BitmapByteQRCode(data))
            using (var stream = new MemoryStream(code.GetGraphic(8)))
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                QrImage.Source = image;
            }
            QrLabel.Text = uri;
            QrPopup.PlacementTarget = this;
            QrPopup.IsOpen = true;
        }
private static string GetInternetInterfaceIp()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                // Exclude virtual/tunnel/VPN adapters
                if (IsVirtualAdapter(nic))
                    continue;

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses == null || props.GatewayAddresses.Count == 0)
                    continue;

                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(g.Address));
                if (!hasGateway)
                    continue;

                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                       a.Address != IPAddress.Any &&
                                       !IPAddress.IsLoopback(a.Address));
                if (ipv4 != null)
                    return ipv4.Address.ToString();
            }
        }
        catch { }

        return null;
    }

        private static bool IsVirtualAdapter(NetworkInterface nic)
        {
            // Exclude common virtual/tunnel/VPN types
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Unknown)
                return true;

            // Check description/name for virtual/VPN keywords (case-insensitive)
            var desc = (nic.Description ?? "").ToLowerInvariant();
            var name = (nic.Name ?? "").ToLowerInvariant();

            string[] virtualKeywords = {
        "tap", "vpn", "virtual", "tunnel", "hyper-v", "vmware",
        "virtualbox", "docker", "wsl", "vEthernet", "pseudo"
            };

            return virtualKeywords.Any(kw => desc.Contains(kw) || name.Contains(kw));
        }


    }
}
