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
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Windows
{
    public partial class ShareVPNSetting : Window
    {
        public IVPNService Service { get; set; }
        private bool proxyBusy;
        private string proxyIp;
        private string proxyError;
        public ShareVPNSetting() { InitializeComponent(); }
        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            proxyIp = GetInternetInterfaceIp();
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
            DirectPanel.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
            ProxyPanel.Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
            DirectTab.Background = direct ? Brushes.White : Brushes.Transparent;
            ProxyTab.Background = direct ? Brushes.Transparent : Brushes.White;
            DirectTab.Foreground = direct ? new SolidColorBrush(Color.FromRgb(20, 27, 51)) : Brushes.Gray;
            ProxyTab.Foreground = direct ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(20, 27, 51));
            QrPopup.IsOpen = false;
            proxyIp = GetInternetInterfaceIp();
            RefreshProxyUi();
        }
        private bool ProxyAvailable => Service is TunnelPlusService tunnel && tunnel.IsTunnelConnected
            && ReferenceEquals(AppServices.GlobalInfo?.CurrentService, Service);
        private bool ProxyListening()
        {
            if (!ProxyAvailable || Service.IsShareActive != true || string.IsNullOrEmpty(proxyIp)) return false;
            int port = Service.HttpPort ?? 0;
            if (port <= 0) return false;
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port
                    && (IPAddress.Any.Equals(p.Address) || IPAddress.IPv6Any.Equals(p.Address) || p.Address.ToString() == proxyIp));
            }
            catch (NetworkInformationException) { return false; }
        }
        private void RefreshProxyUi()
        {
            if (!IsLoaded || proxyBusy) return;
            bool active = ProxyListening();
            btnStartStop.IsChecked = ProxyAvailable && Service.IsShareActive;
            btnStartStop.IsEnabled = ProxyAvailable && !hotspotBusy;
            pnlShowIP.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            ProxyMotion.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            ProxyOffHint.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            HTTPAddress.Text = active ? proxyIp + ":" + Service.HttpPort : "";
            SOCKS5Address.Text = active ? proxyIp + ":" + Service.SocksPort : "";
            ProxyStatus.Text = !ProxyAvailable ? "ابتدا به سرویس سازگار متصل شوید" : proxyError ??
                (active ? "فعال" : Service.IsShareActive ? "آدرس شبکه یا پراکسی فعال تأیید نشد" : "غیرفعال");
            ProxyStatus.Foreground = active ? new SolidColorBrush(Color.FromRgb(23, 171, 119)) : Brushes.Gray;
        }
        private async void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (proxyBusy || hotspotBusy || !ProxyAvailable) { RefreshProxyUi(); return; }
            bool requested = btnStartStop.IsChecked == true;
            proxyBusy = true;
            proxyError = null;
            btnStartStop.IsEnabled = false;
            pnlShowIP.Visibility = ProxyMotion.Visibility = Visibility.Collapsed;
            ProxyStatus.Text = "در حال اعمال…";
            RefreshHotspotUi();
            try
            {
                Service.IsShareActive = requested;
                await Task.Run(() => Service.ApplyShareSetting());
                proxyIp = GetInternetInterfaceIp();
            }
            catch { proxyError = "اعمال تنظیم انجام نشد؛ دوباره تلاش کنید."; }
            finally { proxyBusy = false; RefreshProxyUi(); RefreshHotspotUi(); }
        }
        private string ProxyUri(string type) => type == "HTTP" ? "http://" + HTTPAddress.Text : "socks5://" + SOCKS5Address.Text;
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
