using IRSpeedyVPN.Common;
using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Residential;
using IRSpeedyVPN.Resource;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCServerList
    {
        private ResidentialViewModel residential;
        private bool isResidentialTab, settingTab, connectionConnected, connectionConnecting, residentialConnected;
        private long tabGeneration;
        private string configuredAccount = "";
        private IVPNService vpnSelection, residentialSelection;
        private ServerCountryPicker countryPicker => isResidentialTab ? residentialCountryPicker : vpnCountryPicker;
        internal event EventHandler OnDisconnectRequest;
        private string ServiceKindSetting => "LastServiceKind." + ResidentialPresentation.Digest(globalInfo?.Username ?? "");

        private void InitializeResidential()
        {
            residential = new ResidentialViewModel(globalInfo);
            residential.PropertyChanged += (s, e) => UpdateResidentialVisuals();
            residential.CatalogChanged += () =>
            {
                if (!residential.Services.Contains(residentialSelection))
                {
                    string previous = LastSuccessfulServer.Key(residentialSelection);
                    residentialSelection = residential.Services.FirstOrDefault(s => LastSuccessfulServer.Key(s) == previous)
                        ?? residential.Services.FirstOrDefault(s => LastSuccessfulServer.Key(s) == LastSuccessfulServer.Read());
                }
                if (isResidentialTab)
                {
                    selectedService = residentialSelection;
                    RefreshCountry(null);
                }
            };
            residentialCountryPicker.ServerSelected += service =>
            {
                residentialSelection = service;
                if (isResidentialTab) selectedService = service;
                UpdateResidentialVisuals(); UpdateHeaderIcons();
            };
        }
        internal void ConfigureResidential(AccountInfoEx account)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ConfigureResidential(account)); return; }
            string nextAccount = account == null ? "" : ServiceKindSetting;
            bool changedAccount = configuredAccount != nextAccount;
            configuredAccount = nextAccount;
            if (changedAccount || account == null) tabGeneration++;
            residential.Configure(account);
            bool restore = account != null && residential.IsAllowed && (changedAccount
                ? RegHelper.GetSettingValue(ServiceKindSetting) == "Residential" : isResidentialTab);
            settingTab = true;
            isResidentialTab = restore;
            residentialTab.IsChecked = restore; vpnTab.IsChecked = !restore;
            settingTab = false;
            if (account == null)
            {
                vpnSelection = residentialSelection = selectedService = null;
                residentialConnected = connectionConnecting = connectionConnected = false;
                residentialCountryPicker.Load(new IVPNService[0], false);
            }
            else selectedService = restore ? residentialSelection : vpnSelection;
            residential.SetVisible(restore);
            UpdateResidentialVisuals();
        }
        private async void ServiceTab_Checked(object sender, RoutedEventArgs e)
        {
            if (settingTab || residential == null) return;
            bool next = ReferenceEquals(sender, residentialTab);
            long version = ++tabGeneration;
            if (next == isResidentialTab)
            {
                // A quick return to the current tab invalidates an earlier drain.
                ResumeServerChecksAfterCleanup();
                return;
            }
            try
            {
                PauseServerChecks();
                await DrainServerChecksAsync();
                if (version != tabGeneration) return;
                if (isResidentialTab) residentialSelection = selectedService; else vpnSelection = selectedService;
                isResidentialTab = next;
                selectedService = next ? residentialSelection : vpnSelection;
                probesPaused = false; _isLoading = false;
                if (next) RefreshCountry(null); else ResolveServiceAndProtocol();
                residential.SetVisible(next);
                UpdateResidentialVisuals(); UpdateHeaderIcons();
            }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
        }
        private void ServiceTab_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            ((RadioButton)sender).IsChecked = true; e.Handled = true;
        }
        private void UpdateResidentialVisuals()
        {
            if (residential == null) return;
            vpnCountryPicker.Visibility = isResidentialTab ? Visibility.Collapsed : Visibility.Visible;
            residentialCountryPicker.Visibility = isResidentialTab ? Visibility.Visible : Visibility.Collapsed;
            trafficBar.Update(residential.VisibleTraffic, residentialConnected, isResidentialTab);
            btnConnect.Content = connectionConnecting ? "در حال اتصال…" : connectionConnected ? "قطع اتصال" : "اتصال";
            System.Windows.Automation.AutomationProperties.SetName(btnConnect, (string)btnConnect.Content);
            btnConnect.IsEnabled = connectionConnecting || connectionConnected || !isResidentialTab || (residential.CanConnect && selectedService != null);
            if (connectionConnected)
                btnConnect.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#2FB77C"), (Color)ColorConverter.ConvertFromString("#1F8C5C"), 90);
            else btnConnect.SetResourceReference(BackgroundProperty, "AccentGradientBrush");
        }
        internal void UpdateConnectionState(IVPNService service, bool connecting, bool connected)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateConnectionState(service, connecting, connected)); return; }
            connectionConnecting = connecting; connectionConnected = connected;
            residentialConnected = connected && service is ResidentialService;
            residential.SetConnected(residentialConnected);
            if (connecting && service is ResidentialService) residential.Refresh();
            UpdateResidentialVisuals();
        }
        private void RecordServiceKind(IVPNService service)
        {
            try { RegHelper.SetSettingValue(ServiceKindSetting, service is ResidentialService ? "Residential" : "VPN"); }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
        }
    }
}
