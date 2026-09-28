using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services.SplitTunneling;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SettingsHub : Window
    {
        public IVPNService Service { get; set; }
        public Func<bool> IsConnected { get; set; }
        public Func<bool> IsConnectionBusy { get; set; }
        private readonly System.Windows.Threading.DispatcherTimer stateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool Connected => IsConnected?.Invoke() == true;
        private bool PreferencesLocked => Connected || IsConnectionBusy?.Invoke() == true;
        private void RefreshAccess()
        {
            bool connected = Connected;
            bool locked = PreferencesLocked;
            ShareButton.IsEnabled = connected && IsConnectionBusy?.Invoke() != true;
            MethodsButton.IsEnabled = ShieldButton.IsEnabled = PasswordButton.IsEnabled = !locked;
            GameMode.IsEnabled = !locked && Environment.Is64BitOperatingSystem;
            VodService.IsEnabled = SaveButton.IsEnabled = !locked;
            GameRow.Opacity = AiRow.Opacity = locked ? 0.45 : 1;
            foreach (var control in new System.Windows.UIElement[] { ShareButton, MethodsButton, ShieldButton, PasswordButton, SaveButton })
                control.Opacity = control.IsEnabled ? 1 : 0.45;
        }
        public Func<string, string, Task<string>> ChangePasswordAsync { get; set; }
        public Action<string> PasswordChangeAccepted { get; set; }

        public SettingsHub()
        {
            InitializeComponent();
            GameMode.IsOn = RegHelper.GetSettingValue("VGAURDGameMode") == "1";
            VodService.IsOn = Services.Xray.SmartIpRouting.IsEnabled();
            Loaded += (s, e) => { RefreshAccess(); stateTimer.Start(); };
            stateTimer.Tick += (s, e) => RefreshAccess();
            Closed += (s, e) => stateTimer.Stop();
        }

        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private bool SavePreferences()
        {
            if (PreferencesLocked) return false;
            if (GameMode.IsOn)
            {
                try
                {
                    var split = SplitTunnelStore.Load();
                    if (split.Enabled) { split.Enabled = false; SplitTunnelStore.Save(split); }
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "تقسیم تونل"); return false; }
            }
            Services.Xray.SmartIpRouting.SetEnabled(VodService.IsOn);
            RegHelper.SetSettingValue("VGAURDGameMode", GameMode.IsOn ? "1" : "0");
            if (GameMode.IsOn)
            {
                RegHelper.SetSettingValue("VGAURDVPNMode", "1");
                RegHelper.SetSettingValue("VGAURDGlobalProxy", "0");
                RegHelper.SetSettingValue("VGAURDSystemProxy", "0");
                RegHelper.SetSettingValue("ProxifierTelegramRoute", "0");
            }
            return true;
        }
        private void btnOK_Click(object sender, RoutedEventArgs e) { if (SavePreferences()) Close(); }
        private void Methods_Click(object sender, RoutedEventArgs e)
        {
            if (PreferencesLocked) return;
            // Save hub switches before a child reads them, preventing a stale child save.
            if (!SavePreferences()) return;
            Window page = Service?.SettingType == typeof(SSRServiceSetting)
                ? (Window)new SSRServiceSetting() : new VGAURDServiceSetting { CanSavePreferences = () => !PreferencesLocked };
            page.Owner = this;
            page.ShowDialog();
            // A child can enable split tunneling and disable game mode. Do not
            // overwrite that decision with the hub's older toggle values.
            GameMode.IsOn = RegHelper.GetSettingValue("VGAURDGameMode") == "1";
            VodService.IsOn = Services.Xray.SmartIpRouting.IsEnabled();
        }
        private void Shield_Click(object sender, RoutedEventArgs e)
        { if (!PreferencesLocked) new SpeedyShieldSetting { Owner = this }.ShowDialog(); }
        private void Share_Click(object sender, RoutedEventArgs e)
        { if (Connected) new ShareVPNSetting { Owner = this, Service = AppServices.GlobalInfo?.CurrentService }.ShowDialog(); }
        private void Password_Click(object sender, RoutedEventArgs e)
        {
            if (PreferencesLocked) return;
            new SettingsPassword
            {
                Owner = this,
                ChangePasswordAsync = ChangePasswordAsync,
                PasswordChangeAccepted = password =>
                {
                    Close();
                    PasswordChangeAccepted?.Invoke(password);
                }
            }.ShowDialog();
        }
    }
}
