using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Resource;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SettingsHub : Window
    {
        public IVPNService Service { get; set; }
        public Func<string, string, Task<string>> ChangePasswordAsync { get; set; }

        public SettingsHub()
        {
            InitializeComponent();
            GameMode.IsOn = RegHelper.GetSettingValue("VGAURDGameMode") == "1";
            VodService.IsOn = RegHelper.GetSettingValue("VGAURDVodService") != "0";
            GameMode.IsEnabled = Environment.Is64BitOperatingSystem;
        }

        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void SavePreferences()
        {
            RegHelper.SetSettingValue("VGAURDVodService", VodService.IsOn ? "1" : "0");
            RegHelper.SetSettingValue("VGAURDGameMode", GameMode.IsOn ? "1" : "0");
            if (GameMode.IsOn)
            {
                RegHelper.SetSettingValue("VGAURDVPNMode", "1");
                RegHelper.SetSettingValue("VGAURDGlobalProxy", "0");
                RegHelper.SetSettingValue("VGAURDSystemProxy", "0");
                RegHelper.SetSettingValue("ProxifierTelegramRoute", "0");
            }
        }
        private void btnOK_Click(object sender, RoutedEventArgs e) { SavePreferences(); Close(); }
        private void Methods_Click(object sender, RoutedEventArgs e)
        {
            // Save hub switches before a child reads them, preventing a stale child save.
            SavePreferences();
            Window page = Service?.SettingType == typeof(SSRServiceSetting)
                ? (Window)new SSRServiceSetting() : new VGAURDServiceSetting();
            page.Owner = this;
            page.ShowDialog();
        }
        private void Shield_Click(object sender, RoutedEventArgs e) => new SpeedyShieldSetting { Owner = this }.ShowDialog();
        private void Share_Click(object sender, RoutedEventArgs e) => new ShareVPNSetting { Owner = this, Service = AppServices.GlobalInfo?.CurrentService }.ShowDialog();
        private void Password_Click(object sender, RoutedEventArgs e)
            => new SettingsPassword { Owner = this, ChangePasswordAsync = ChangePasswordAsync }.ShowDialog();
    }
}
