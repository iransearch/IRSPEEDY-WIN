using IRSpeedyVPN.Common;
using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCServerList : UserControl, IHasTitle
    {
        private ServiceFactory serviceFactory => AppServices.ServiceFactory;
        private GlobalInfo globalInfo => AppServices.GlobalInfo;

        public string Title => "لیست سرورها";

        internal delegate void LoadingRequest(bool Show, string Message);
        internal delegate void ConnectRequest(UCServerList sender, IVPNService service, string protocol);
        internal event LoadingRequest OnLoadingRequest;
        internal event ConnectRequest OnConnectRequest;

        internal IVPNService selectedService;
        private string selectedProtocol;
        private bool _isLoading = true;
        private bool _isUrlTestSupported;
        private CancellationTokenSource _urlTestCts;
        private IVPNService[] _currentServices;

        public UCServerList()
        {
            InitializeComponent();
            countryPicker.ServerSelected += svc =>
            {
                selectedService = svc;
                UpdateHeaderIcons();
            };
        }

        #region Lifecycle

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            if (serviceFactory.Services != null)
            {
                var items = serviceFactory.Services
                    .OrderBy(y => y.Order).GroupBy(x => x.Name).Select(x => x.Key).ToArray();

                cmbService.Items.Clear();
                cmbService.Items.AddRange(items);
                cmbService.SelectedItem = (_isLoading && globalInfo?.CurrentService != null)
                    ? globalInfo.CurrentService.Name
                    : items.FirstOrDefault();
            }

            UpdateHeaderIcons();
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) UpdateHeaderIcons();
            else ClearHeaderIcons();
        }

        #endregion

        #region Service / Protocol

        private void cmbService_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbService.SelectedItem == null) return;

            var serviceName = cmbService.SelectedItem.ToString();
            var protocols = serviceFactory.Services
                .Where(x => x.Name == serviceName)
                .SelectMany(i => i.Protocols).Distinct().ToArray();

            if (protocols.Length > 1)
            {
                cmbProtocol.Items.Clear();
                cmbProtocol.Items.AddRange(protocols);

                string preferred = null;
                if (_isLoading && globalInfo?.CurrentService != null)
                {
                    var cur = globalInfo.CurrentService;
                    preferred = !string.IsNullOrEmpty(cur.SelectedProtocol)
                        ? cur.SelectedProtocol
                        : protocols.FirstOrDefault(p => cur.Protocols != null && cur.Protocols.Contains(p));
                }

                cmbProtocol.SelectedItem = (preferred != null && protocols.Contains(preferred))
                    ? preferred : protocols[0];
                gProtocol.Visibility = Visibility.Visible;
            }
            else
            {
                gProtocol.Visibility = Visibility.Collapsed;
                selectedProtocol = null;
                RefreshCountry(null);
            }
        }

        private void cmbProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbProtocol.SelectedItem == null) return;
            selectedProtocol = cmbProtocol.SelectedItem.ToString();
            RefreshCountry(selectedProtocol);
        }

        #endregion

        #region Country picker

        private void RefreshCountry(string protocol)
        {
            if (cmbService.SelectedItem == null) return;

            var serviceName = cmbService.SelectedItem.ToString();
            var services = serviceFactory.Services
                .Where(x => x.Name == serviceName && (protocol == null || x.Protocols.Contains(protocol)))
                .OrderBy(x => x.Country).ToArray();

            // Preserve distinct API service records for the same CountryCode. If two
            // German records exist they are shown again as "آلمان 1" and "آلمان 2";
            // each row owns its own full URL pool and minimum-latency result.
            AssignCountryIndices(services);

            _isUrlTestSupported = services.Any(x => x.IsUrlTestSupported);
            ResolveSelectedService(services);

            _currentServices = services;
            countryPicker.Load(services, _isUrlTestSupported);
            countryPicker.SelectedService = selectedService;

            RunBackgroundUrlTests(services);

            _isLoading = false;
            UpdateHeaderIcons();
        }

        private static void AssignCountryIndices(IVPNService[] services)
        {
            foreach (var service in services)
                service.CountryIndex = 0;

            foreach (var service in services)
            {
                if (services.Count(x => x.CountryCode == service.CountryCode) > 1
                    && service.CountryIndex == 0)
                {
                    service.CountryIndex = (byte)(services.Count(x =>
                        x.CountryCode == service.CountryCode && x.CountryIndex > 0) + 1);
                }
            }
        }

        private void ResolveSelectedService(IVPNService[] services)
        {
            if (_isLoading && globalInfo?.CurrentService != null
                && services.Contains(globalInfo.CurrentService))
            {
                var current = globalInfo.CurrentService;

                // Global Smart/Fast has no row marker. A numbered country Smart pool
                // keeps SelectedServerUrl non-null only as its scope marker.
                selectedService = (current is ISmartFastConnection smart
                                   && smart.IsSmartFast
                                   && current.SelectedServerUrl == null)
                    ? null
                    : current;
            }
            else if (selectedService == null || !services.Contains(selectedService))
            {
                selectedService = null;
            }

            if (selectedService == null && !_isUrlTestSupported && services.Length > 0)
                selectedService = services[0];
        }

        #endregion

        #region Background URL tests

        /// <summary>
        /// A row is fresh only when every URL belonging to that service record has a
        /// recent test result. Failed URLs also receive latencychkTime, so they are not
        /// retried continuously during the five-minute freshness window.
        /// </summary>
        private static bool HasFreshResultsForAllUrls(IVPNService service)
        {
            var urls = service.GetServerUrls();
            if (urls == null || urls.Count == 0) return false;

            return urls.All(u => u != null
                && u.latencychkTime != default(DateTime)
                && !Sig.IsStale(u));
        }

        private void StopUrlTests()
        {
            UrlTestCoordinator.CancelAll();
            _urlTestCts?.Cancel();
        }

        /// <summary>
        /// Tests every URL of every visible numbered country row. The picker displays
        /// only the minimum positive latency for that row.
        /// </summary>
        private void RunBackgroundUrlTests(IVPNService[] services)
        {
            _urlTestCts?.Cancel();
            _urlTestCts = new CancellationTokenSource();
            var token = _urlTestCts.Token;

            UrlTestCoordinator.BeginBatch();

            Task.Run(() =>
            {
                Func<bool> cancelled = () => token.IsCancellationRequested || UrlTestCoordinator.AbortRequested;
                var schedule = new List<InitialCountryTest>();
                foreach (var service in services)
                {
                    if (cancelled()) return;
                    bool needsTest = !HasFreshResultsForAllUrls(service);
                    var tunnel = service as TunnelPlusService;
                    var tests = new List<Action<Action<long>>>();
                    if (needsTest)
                    {
                        if (tunnel != null)
                        {
                            // Preserve web-service URL order. Row sorting during the
                            // run must never change the round-robin schedule.
                            var urls = (service.GetServerUrls() ?? new List<Url>())
                                .Where(u => u != null)
                                .GroupBy(u => u.url, StringComparer.Ordinal)
                                .Select(group => group.First()).ToArray();
                            foreach (var url in urls)
                                tests.Add(report => tunnel.TestInitialUrl(url, report, cancelled));
                        }
                        else
                            tests.Add(report => report(service.UrlTest()));
                    }
                    schedule.Add(new InitialCountryTest(tests,
                        latency => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!cancelled()) countryPicker.ShowGroupProgress(service, latency);
                        })),
                        () =>
                        {
                            if (cancelled()) return;
                            if (needsTest && tunnel != null) tunnel.CompleteInitialUrlTests();
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!cancelled()) countryPicker.RefreshGroup(service);
                            }));
                        }));
                }
                InitialUrlTestSchedule.Run(schedule, cancelled,
                    ex => LogHelper.WriteExLog("[UrlTest] stage=member-failed exception=" + ex.GetType().Name));
            });
        }

        #endregion

        #region Connect

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (OnConnectRequest == null) return;

            if (selectedService != null)
            {
                // A numbered country row has already loaded ALL URLs belonging to that
                // row into ISmartFastConnection. Keep the mixed sing-box/Xray pool intact.
                StopUrlTests();
                OnConnectRequest.Invoke(this, selectedService, selectedProtocol);
            }
            else
            {
                ConnectToFastestServer();
            }
        }

        private void ConnectToFastestServer()
        {
            if (cmbService.SelectedItem == null) return;

            _urlTestCts?.Cancel();
            UrlTestCoordinator.BeginBatch();

            var serviceName = cmbService.SelectedItem.ToString();
            var services = serviceFactory.Services
                .Where(x => x.IsUrlTestSupported
                    && x.Name == serviceName
                    && (string.IsNullOrEmpty(selectedProtocol) || x.Protocols.Contains(selectedProtocol)))
                .Randomize().ToList();

            if (!services.Any()) return;

            Action action = () =>
            {
                OnLoadingRequest?.Invoke(true, "در حال یافتن سریعترین سرور");
                try
                {
                    services.First().DisconnectAll();
                    OnLoadingRequest?.Invoke(false, null);

                    var smartService = services.FirstOrDefault(x => x is ISmartFastConnection);

                    // Global Fast keeps every URL. Native protocols stay in sing-box;
                    // Xray-compatible links use its observatory/leastLoad pool.
                    var allUrls = services
                        .SelectMany(x => x.GetServerUrls() ?? new List<Url>())
                        .Where(u => u != null)
                        // hy2 first, so the balancer's fallback outbound is one that comes
                        // up quickly while its own probes are still warming up.
                        .OrderByHysteriaFirst()
                        .Select(u => u.url)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    if (smartService != null && allUrls.Length > 0)
                    {
                        smartService.SelectedServerUrl = null; // marks global Smart, not a country pool
                        ((ISmartFastConnection)smartService).SetSmartFastUrls(allUrls);
                        OnConnectRequest.Invoke(this, smartService, selectedProtocol ?? "");
                        return;
                    }

                    var fastest = services.Where(x => x.UrlTestSpeed > 0)
                        .OrderBy(x => x.UrlTestSpeed).FirstOrDefault();

                    if (fastest != null)
                        OnConnectRequest.Invoke(this, fastest, selectedProtocol ?? "");
                    else
                        Dispatcher.Invoke((Action)(() => GetMainWindow()?.ShowUserMessage("سرور یافت نشد")));
                }
                catch
                {
                    OnLoadingRequest?.Invoke(false, null);
                }
            };

            action.BeginInvoke(null, null);
        }

        #endregion

        #region Header icons

        private void UpdateHeaderIcons()
        {
            var host = GetMainWindow();
            if (host == null) return;

            var sService = selectedService ?? GetFallbackService();
            var icons = new List<HeaderIconRegistration>();

            if (!string.IsNullOrWhiteSpace(TunnelPlusService.selectedChain))
                icons.Add(new HeaderIconRegistration("", "حذف سرویس پایه", RemoveBaseService));
            if (sService?.SettingType != null)
                icons.Add(new HeaderIconRegistration("", "تنظیمات سرویس", OpenServiceSettings));
            if (sService?.ShowSpeedyShieldSetting == true)
                icons.Add(new HeaderIconRegistration("", "تنظیمات Speedy Shield", OpenSpeedyShieldSetting));

            host.SetHeaderIcons(this, icons);
        }

        private void ClearHeaderIcons() => GetMainWindow()?.ClearHeaderIcons(this);

        private void OpenServiceSettings()
        {
            var sService = selectedService ?? GetFallbackService();
            if (sService?.SettingType == null) return;

            Window setting;
            if (sService.SettingType == typeof(VGAURDServiceSetting))
                setting = new VGAURDServiceSetting();
            else if (sService.SettingType == typeof(SSRServiceSetting))
                setting = new SSRServiceSetting();
            else
                return;

            setting.Owner = Window.GetWindow(this);
            setting.ShowDialog();
        }

        private void OpenSpeedyShieldSetting()
        {
            var setting = new SpeedyShieldSetting { Owner = Window.GetWindow(this) };
            setting.ShowDialog();
        }

        private void RemoveBaseService()
        {
            TunnelPlusService.selectedChain = null;
            GetMainWindow()?.ShowHintPopup("سرویس پایه حذف شد");
            UpdateHeaderIcons();
        }

        #endregion

        #region Helpers

        private MainWindow GetMainWindow()
            => Window.GetWindow(this) as MainWindow ?? Application.Current?.MainWindow as MainWindow;

        private IVPNService GetFallbackService()
        {
            if (cmbService.SelectedItem == null) return null;
            return serviceFactory.Services?.FirstOrDefault(s => s.Name == cmbService.SelectedItem.ToString());
        }

        #endregion
    }
}

