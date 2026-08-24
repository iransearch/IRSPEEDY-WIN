using IRSpeedyVPN.Resource;
using System;
using System.Windows;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class VGAURDServiceSetting : Window
    {
        private bool _isUpdating;

        private const string KEY_VPN = "VGAURDVPNMode";
        private const string KEY_GLOBAL_PROXY = "VGAURDGlobalProxy";
        private const string KEY_SYSTEM_PROXY = "VGAURDSystemProxy";
        private const string KEY_TELEGRAM_PROXY = "ProxifierTelegramRoute"; // 1 = on, 0 = off
        private const string KEY_VOD = "VGAURDVodService";

        public VGAURDServiceSetting()
        {
            InitializeComponent();

            if (!Environment.Is64BitOperatingSystem && rowVpn != null)
                rowVpn.Visibility = Visibility.Collapsed;

            LoadSettings();
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e) => Close();

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            RegHelper.SetSettingValue(KEY_VPN, IsOn(VPNMode) ? "1" : "0");
            RegHelper.SetSettingValue(KEY_GLOBAL_PROXY, IsOn(GlobalProxy) ? "1" : "0");
            RegHelper.SetSettingValue(KEY_SYSTEM_PROXY, IsOn(SystemProxy) ? "1" : "0");
            RegHelper.SetSettingValue(KEY_TELEGRAM_PROXY, IsOn(TelegramRouteProxy) ? "1" : "0");

            // VOD/AI is independent
            RegHelper.SetSettingValue(KEY_VOD, IsOn(VodService) ? "1" : "0");

            Close();
        }

        // --------------------------
        // Load settings
        // --------------------------
        private void LoadSettings()
        {
            _isUpdating = true;
            try
            {
                SetChecked(VPNMode, RegHelper.GetSettingValue(KEY_VPN) != "0");
                SetChecked(GlobalProxy, RegHelper.GetSettingValue(KEY_GLOBAL_PROXY) == "1");
                SetChecked(SystemProxy, RegHelper.GetSettingValue(KEY_SYSTEM_PROXY) == "1");
                SetChecked(TelegramRouteProxy, RegHelper.GetSettingValue(KEY_TELEGRAM_PROXY) == "1");
                SetChecked(VodService, RegHelper.GetSettingValue(KEY_VOD) == "1");
            }
            finally
            {
                _isUpdating = false;
            }

            NormalizeExclusiveGroup();
        }

        // --------------------------
        // Mutual exclusivity (except VOD/AI)
        // --------------------------
        private void NormalizeExclusiveGroup()
        {
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                // Priority (if old configs enabled multiple):
                // VPN > Global > System > Telegram
                if (IsOn(VPNMode))
                    TurnOffOthers(VPNMode);
                else if (IsOn(GlobalProxy))
                    TurnOffOthers(GlobalProxy);
                else if (IsOn(SystemProxy))
                    TurnOffOthers(SystemProxy);
                else if (IsOn(TelegramRouteProxy))
                    TurnOffOthers(TelegramRouteProxy);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void ExclusiveSwitchTurnedOn(object keepOn)
        {
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                TurnOffOthers(keepOn);
                // VOD/AI intentionally untouched
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void TurnOffOthers(object keepOn)
        {
            SetCheckedIfNot(keepOn, VPNMode, false);
            SetCheckedIfNot(keepOn, GlobalProxy, false);
            SetCheckedIfNot(keepOn, SystemProxy, false);
            SetCheckedIfNot(keepOn, TelegramRouteProxy, false);
        }

        private static void SetCheckedIfNot(object keepOn, ToggleSwitch.HorizontalToggleSwitch target, bool value)
        {
            if (target == null) return;
            if (ReferenceEquals(keepOn, target)) return;
            try { target.IsChecked = value; } catch { }
        }

        // --------------------------
        // Event handlers (exclusive)
        // --------------------------
        private void VPNMode_Checked(object sender, RoutedEventArgs e) => ExclusiveSwitchTurnedOn(VPNMode);
        private void GlobalProxy_Checked(object sender, RoutedEventArgs e) => ExclusiveSwitchTurnedOn(GlobalProxy);
        private void SystemProxy_Checked(object sender, RoutedEventArgs e) => ExclusiveSwitchTurnedOn(SystemProxy);
        private void TelegramRouteProxy_Checked(object sender, RoutedEventArgs e) => ExclusiveSwitchTurnedOn(TelegramRouteProxy);

        private void VPNMode_Unchecked(object sender, RoutedEventArgs e) { }
        private void GlobalProxy_Unchecked(object sender, RoutedEventArgs e) { }
        private void SystemProxy_Unchecked(object sender, RoutedEventArgs e) { }
        private void TelegramRouteProxy_Unchecked(object sender, RoutedEventArgs e) { }

        // VOD/AI independent
        private void VodService_Checked(object sender, RoutedEventArgs e) { }
        private void VodService_Unchecked(object sender, RoutedEventArgs e) { }

        // --------------------------
        // Helpers
        // --------------------------
        private static bool IsOn(ToggleSwitch.HorizontalToggleSwitch toggle)
        {
            try { return toggle != null && toggle.IsChecked == true; }
            catch { return false; }
        }

        private static void SetChecked(ToggleSwitch.HorizontalToggleSwitch toggle, bool value)
        {
            if (toggle == null) return;
            try { toggle.IsChecked = value; } catch { }
        }
    }
}
