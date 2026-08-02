using IRSpeedyVPN.Resource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IRSpeedyVPN.Windows
{
    /// <summary>
    /// Interaction logic for SSRServiceSetting.xaml
    /// </summary>
    public partial class VPNPlusServiceSetting : Window
    {
        public VPNPlusServiceSetting()
        {
            InitializeComponent();
            GlobalProxy.IsChecked = RegHelper.GetSettingValue("VPNPlusGlobalProxy") != "0";
            SmartRouteProxy.IsChecked = RegHelper.GetSettingValue("ProxifierSmartRoute") == "1";
            TelegramRouteProxy.IsChecked = RegHelper.GetSettingValue("ProxifierSmartRoute") == "2";
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            RegHelper.SetSettingValue("VPNPlusGlobalProxy", GlobalProxy.IsChecked ? "1" : "0");
            RegHelper.SetSettingValue("ProxifierSmartRoute", SmartRouteProxy.IsChecked ? "1" : (TelegramRouteProxy.IsChecked ? "2" : "0"));
            this.Close();
        }

        private void GlobalProxy_Checked(object sender, RoutedEventArgs e)
        {
            if (SmartRouteProxy != null && TelegramRouteProxy != null)
                SmartRouteProxy.IsChecked = TelegramRouteProxy.IsChecked = false;
        }

        private void SmartRouteProxy_Checked(object sender, RoutedEventArgs e)
        {
            if (GlobalProxy != null && TelegramRouteProxy != null)
                GlobalProxy.IsChecked = TelegramRouteProxy.IsChecked = false;
        }

        private void TelegramRouteProxy_Checked(object sender, RoutedEventArgs e)
        {
            if (GlobalProxy != null && SmartRouteProxy!=null)
                GlobalProxy.IsChecked = SmartRouteProxy.IsChecked = false;
        }
    }
}
