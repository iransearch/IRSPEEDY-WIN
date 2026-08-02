using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using System.Windows.Shapes;

namespace IRSpeedyVPN.Windows
{
    /// <summary>
    /// Interaction logic for ShareVpnSetting.xaml
    /// </summary>
    public partial class ShareVPNSetting : Window
    {
        public IVPNService Service { get; set; }

        public ShareVPNSetting()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            btnStartStop.IsChecked = Service?.IsShareActive ?? false;
            pnlShowIP.Visibility = (Service?.IsShareActive ?? false) ? Visibility.Visible : Visibility.Collapsed;
            UpdatePortTexts();
            if (Service?.IsShareActive ?? false)
            {
                UpdateIpTexts();
            }
        }

        private void Close_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            btnStartStop_Checked(sender, e);
        }

        private void lblHttpQR_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!popHttpQR.IsOpen)
            {
                var uri = BuildHttpUri();
                imgHttpQR.Source = CreateQr(uri);
                popHttpQR.IsOpen = true;
            }
        }

        private void lblHttpCopy_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Clipboard.SetText(BuildHttpUri());
            ShowCopiedPopup((UIElement)sender);
        }

        private void popHttpQR_Opened(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => bdHttpPopupHost.Focus()));
        }

        private void popHttpQR_Closed(object sender, EventArgs e)
        {
            Task.Delay(250).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => { });
            });
        }

        private void lblSocksQR_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!popSocksQR.IsOpen)
            {
                var uri = BuildSocksUri();
                imgSocksQR.Source = CreateQr(uri);
                popSocksQR.IsOpen = true;
            }
        }

        private void lblSocksCopy_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Clipboard.SetText(BuildSocksUri());
            ShowCopiedPopup((UIElement)sender);
        }

        private void popSocksQR_Opened(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => bdSocksPopupHost.Focus()));
        }

        private void popSocksQR_Closed(object sender, EventArgs e)
        {
            Task.Delay(250).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => { });
            });
        }

        private void btnStartStop_Checked(object sender, RoutedEventArgs e)
        {
            ShowButtonSpinner(true);
            if (Service == null)
            {
                ShowButtonSpinner(false);
                return;
            }

            Service.IsShareActive = btnStartStop.IsChecked.GetValueOrDefault();
            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (Service.IsShareActive)
                    {
                        Dispatcher.Invoke(UpdateIpTexts);
                        Dispatcher.Invoke(UpdatePortTexts);
                    }
                    Service.ApplyShareSetting();
                    Dispatcher.Invoke(() =>
                    {
                        pnlShowIP.Visibility = Service.IsShareActive ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() => ShowButtonSpinner(false));
                }
            });
        }

        private string BuildHttpUri()
        {
            return $"http://{txtIp0.Text}:{txtPortHttp.Text}";
        }

        private string BuildSocksUri()
        {
            return $"socks5://{txtIp1.Text}:{txtPortSocks.Text}";
        }

        private void UpdatePortTexts()
        {
            var httpPort = Service?.HttpPort;
            var socksPort = Service?.SocksPort;
            txtPortHttp.Text = httpPort.HasValue ? httpPort.Value.ToString() : "1080";
            txtPortSocks.Text = socksPort.HasValue ? socksPort.Value.ToString() : "1080";
        }

        private void UpdateIpTexts()
        {
            var ip = GetInternetInterfaceIp();
            txtIp0.Text = ip;
            txtIp1.Text = ip;
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

        return "127.0.0.1";
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


    private void ShowButtonSpinner(bool show)
        {
            var storyboard = FindResource("SpinnerStoryboard") as System.Windows.Media.Animation.Storyboard;
            if (show)
            {
                LoadingSpinner.Visibility = Visibility.Visible;
                storyboard?.Begin(LoadingSpinner, true);
                btnStartStop.IsEnabled = false;
                btnStartStop.Content = "";
            }
            else
            {
                try
                {
                    storyboard?.Stop(LoadingSpinner);
                }
                catch
                {
                }
                LoadingSpinner.Visibility = Visibility.Collapsed;
                btnStartStop.IsEnabled = true;
                btnStartStop.ClearValue(ContentProperty);
            }
        }

        private void ShowCopiedPopup(UIElement target)
        {
            popCopied.PlacementTarget = target;
            popCopied.IsOpen = true;
            Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => popCopied.IsOpen = false);
            });
        }

        private ImageSource CreateQr(string text)
        {
            using (var generator = new QRCodeGenerator())
            {
                var data = generator.CreateQrCode(text ?? string.Empty, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new BitmapByteQRCode(data);
                byte[] qrBytes = qrCode.GetGraphic(8);

                var image = new BitmapImage();
                using (var ms = new MemoryStream(qrBytes))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze();
                }
                return image;
            }
        }
    }
}
