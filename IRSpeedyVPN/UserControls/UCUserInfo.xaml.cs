using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRSpeedyVPN.UserControls
{
    /// <summary>
    /// Interaction logic for UCUserInfo.xaml
    /// </summary>
    public partial class UCUserInfo : UserControl, IHasTitle
    {

        private IProxifier proxifier => AppServices.Proxifier;

        internal delegate void LoadingRequest(bool Show, string Message);
        internal event LoadingRequest OnLoadingRequest;
        public event EventHandler OnDisconnectRequest;
        public event EventHandler OnChangeServerRequest;
        Timer uiTimer;
        int timerTick;
        
        GlobalInfo globalInfo;

        public string Title => "اطلاعات اتصال";

        public UCUserInfo()
        {            
            InitializeComponent();
            uiTimer = new Timer(uiTimerCallback, null,int.MaxValue, int.MaxValue);            

        }
        public void uiTimerCallback(object state)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                txtConnectionTime.Text = (globalInfo.ConnectionTime - DateTime.Now).ToString(@"hh\:mm\:ss");
            }));
        }
        private void btn_ChangeServer_Click(object sender, RoutedEventArgs e)
        {
            if (OnChangeServerRequest != null)
                OnChangeServerRequest.Invoke(sender, e);
        }

        private void btnDisConnect_Click(object sender, RoutedEventArgs e)
        {
            uiTimer.Change(int.MaxValue, int.MaxValue);
            if (OnDisconnectRequest != null)
                OnDisconnectRequest.Invoke(sender, e);
        }

        private long?[] CheckPing(string[] sites)
        {
            long?[] result = new long?[sites.Length];

            System.Threading.Tasks.Parallel.For(0, sites.Length, i =>
            {
                try
                {
                    long delay = ServiceHelper.UrlTest(sites[i], 4000);
                    if (delay >= 0)
                        result[i] = delay;
                }
                catch
                {
                }
            });

            return result;
        }

        private void ConnectionTest_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            OnLoadingRequest?.Invoke(true, null);
            string[] sites = new[]
            {
                "https://www.google.com/generate_204",
                "https://www.youtube.com",
                "https://www.instagram.com",
                "https://telegram.org"
            };
            long?[] res = new long?[sites.Length];

            Action action = () => res = CheckPing(sites);
            action.BeginInvoke(ar =>
            {
                OnLoadingRequest?.Invoke(false, null);
                Dispatcher.Invoke(() =>
                {
                    var result = new PingResult
                    {
                        GoogleSpeed = res[0],
                        YoutubeSpeed = res[1],
                        InstaSpeed = res[2],
                        TelegramSpeed = res[3],
                        Owner = Window.GetWindow(this)
                    };
                    result.ShowDialog();
                });
            }, null);
        }

        private void ShareConnection_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new ShareVPNSetting
            {
                Owner = Window.GetWindow(this),
                Service = globalInfo?.CurrentService
            };
            dialog.ShowDialog();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {

            globalInfo = AppServices.GlobalInfo;
            timerTick = 0;
            uiTimer.Change(1000, 1000);
            txtCountry.Text = (globalInfo.CurrentService is ISmartFastConnection smart && smart.IsSmartFast)
                ? "سرور هوشمند"
                : globalInfo.CurrentService.Country;
            txtServiceName.Text = globalInfo.CurrentService.Name + (proxifier.IsAttached() && proxifier.ProxyType.GetDescription().Length > 0 ? " / " + proxifier.ProxyType.GetDescription() : "");
            txtConnectionTime.Text = "00:00:00";

            txtExpireDate.Text = (globalInfo.ExpiryDate != null) ? globalInfo.ExpiryDate.Value.ToPresianDate() : "اولین اتصال";
            txtRemainedTime.Text = (globalInfo.ExpiryDate != null) ? globalInfo.ExpiryDate.Value.TotalDays() : "اولین اتصال";

            
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                ClearHeaderIcons();
            }
            else
            {
                RegisterHeaderIcons();
            }
        }

        private void RegisterHeaderIcons()
        {
            var host = GetMainWindow();
            if (host == null) return;


            var icons = new List<HeaderIconRegistration>
            {
                new HeaderIconRegistration("", "تست سرویس", () => ConnectionTest_PreviewMouseDown(this, null)),
                new HeaderIconRegistration("", "اشتراک‌گذاری اتصال", () => ShareConnection_PreviewMouseDown(this, null))
            };
            if (File.Exists("./chainplus.txt") && string.IsNullOrWhiteSpace(TunnelPlusService.selectedChain))
            {
                icons.Insert(0,new HeaderIconRegistration("", "تنظیم بصورت سرویس پایه", RegisterBaseService));
            }
            host.SetHeaderIcons(this, icons);
        }

        private void ClearHeaderIcons()
        {
            var host = GetMainWindow();
            host?.ClearHeaderIcons(this);
        }

        private void RegisterBaseService()
        {
            var host = GetMainWindow();
            var link = (globalInfo?.CurrentService as TunnelPlusService)?.SelectedUrl;
            TunnelPlusService.selectedChain = link;
            host?.ShowHintPopup("سرویس پایه انتخاب شد");
            OnDisconnectRequest?.Invoke(this, EventArgs.Empty);
        }

        private MainWindow GetMainWindow()
        {
            return Window.GetWindow(this) as MainWindow ?? Application.Current?.MainWindow as MainWindow;
        }
    }
}
