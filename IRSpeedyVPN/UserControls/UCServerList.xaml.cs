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

        // The service picker (§ "بخش انتخاب سرویس") was removed from the UI; the app
        // now always connects through this service by name, matched case-insensitively
        // against whatever the backend returns (group title / server.Service). If the
        // backend has no service by this name, the first available one is used instead
        // -- see ResolveServiceAndProtocol().
        private const string DefaultServiceName = "xfast";

        internal delegate void LoadingRequest(bool Show, string Message);
        internal delegate void ConnectRequest(UCServerList sender, IVPNService service, string protocol,
            Func<IVPNService> prepareService);
        internal event LoadingRequest OnLoadingRequest;
        internal event ConnectRequest OnConnectRequest;

        internal IVPNService selectedService;
        private string selectedProtocol;
        private string _selectedServiceName;
        private bool _isLoading = true;
        private bool _isUrlTestSupported;
        private CancellationTokenSource _urlTestCts;
        private IVPNService[] _currentServices;

        public UCServerList()
        {
            InitializeComponent();
            probeTimer.Tick += (s, e) => RunBackgroundUrlTests(_currentServices);
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
            txtSearch.Text = "";
            ResolveServiceAndProtocol();
            UpdateHeaderIcons();
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                UpdateHeaderIcons();
                // Loaded may not fire again after restoring a hidden window.
                Dispatcher.BeginInvoke(new Action(() => RunBackgroundUrlTests(_currentServices)));
            }
            else { ClearHeaderIcons(); }
        }

        #endregion

        #region Service / Protocol (no UI -- resolved automatically)

        /// <summary>
        /// Picks the default service ("xfast", case-insensitive; falls back to the
        /// currently-connected service on reload, then to the first service the
        /// backend returned) and its protocol, then loads the country/server list for
        /// it. This replaces the old cmbService/cmbProtocol combo boxes.
        /// </summary>
        private void ResolveServiceAndProtocol()
        {
            if (serviceFactory.Services == null || serviceFactory.Services.Count == 0)
            {
                _selectedServiceName = null;
                selectedProtocol = null;
                return;
            }

            var names = serviceFactory.Services
                .OrderBy(y => y.Order).GroupBy(x => x.Name).Select(x => x.Key).ToArray();

            _selectedServiceName = names.FirstOrDefault(n => string.Equals(n, DefaultServiceName, StringComparison.OrdinalIgnoreCase))
                ?? ((_isLoading && globalInfo?.CurrentService != null && names.Contains(globalInfo.CurrentService.Name))
                    ? globalInfo.CurrentService.Name
                    : names.FirstOrDefault());

            var protocols = serviceFactory.Services
                .Where(x => x.Name == _selectedServiceName)
                .SelectMany(i => i.Protocols).Distinct().ToArray();

            if (protocols.Length > 1)
            {
                string preferred = null;
                if (_isLoading && globalInfo?.CurrentService != null && globalInfo.CurrentService.Name == _selectedServiceName)
                {
                    var cur = globalInfo.CurrentService;
                    preferred = !string.IsNullOrEmpty(cur.SelectedProtocol)
                        ? cur.SelectedProtocol
                        : protocols.FirstOrDefault(p => cur.Protocols != null && cur.Protocols.Contains(p));
                }

                selectedProtocol = (preferred != null && protocols.Contains(preferred)) ? preferred : protocols[0];
            }
            else
            {
                selectedProtocol = null;
            }

            RefreshCountry(selectedProtocol);
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            countryPicker.SetFilter(txtSearch.Text);
        }

        #endregion

        #region Country picker

        private void RefreshCountry(string protocol)
        {
            if (_selectedServiceName == null) return;

            var services = serviceFactory.Services
                .Where(x => x.Name == _selectedServiceName && (protocol == null || x.Protocols.Contains(protocol)))
                .OrderBy(x => x.Country).ToArray();

            // Preserve distinct API service records for the same CountryCode. If two
            // German records exist they are shown again as "آلمان 1" and "آلمان 2";
            // each row owns its own full URL pool and minimum-latency result.
            AssignCountryIndices(services);

            _isUrlTestSupported = services.Any(x => x.IsUrlTestSupported);
            ResolveSelectedService(services);

            // Retire an old API-list scan before rebinding replacement service objects.
            _urlTestCts?.Cancel();
            _currentServices = services;
            string context = (globalInfo?.Username ?? "") + "\n" + _selectedServiceName + "\n" + (protocol ?? "");
            if (probeCache == null || probeCacheContext != context)
            {
                probeCache = new ServerCheckCache(globalInfo?.Username ?? "", _selectedServiceName + "\n" + (protocol ?? ""));
                probeCacheContext = context;
                probeSchedule.RestartNow();
            }
            probeCache.Bind(services);
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

        private readonly System.Windows.Threading.DispatcherTimer probeTimer =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        private Task probeTask = Task.CompletedTask;
        private bool probesPaused;
        private bool probeRunning;
        private bool probeWakeRequested;
        private ServerCheckCache probeCache;
        private string probeCacheContext;
        private readonly CountryProbeSchedule probeSchedule = new CountryProbeSchedule();

        internal void PauseServerChecks()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(PauseServerChecks); return; }
            probesPaused = true;
            probeTimer.Stop();
            StopUrlTests();
        }

        internal async Task DrainServerChecksAsync()
        {
            PauseServerChecks();
            await probeTask;
        }

        internal void PrepareServerChecksForLogin()
        {
            // Called on the UI thread after successful login and connection cleanup,
            // before Loaded restores this account's saved results and resumes its country turn.
            probeTimer.Stop();
            probeSchedule.RestartNow();
            probesPaused = false;
        }

        internal void ResumeServerChecksAfterCleanup()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ResumeServerChecksAfterCleanup); return; }
            probesPaused = false;
            probeSchedule.RestartNow();
            RunBackgroundUrlTests(_currentServices);
        }

        private void StopUrlTests()
        {
            UrlTestCoordinator.CancelAll();
            _urlTestCts?.Cancel();
        }

        private async void RunBackgroundUrlTests(IVPNService[] services)
        {
            if (probesPaused || services == null || probeCache == null
                || globalInfo?.CurrentService != null) return;
            if (probeRunning) { probeWakeRequested = true; return; }
            probeTimer.Stop();
            var remaining = probeSchedule.Remaining(DateTime.UtcNow);
            if (remaining > TimeSpan.Zero)
            {
                probeTimer.Interval = remaining;
                probeTimer.Start();
                return;
            }
            var cache = probeCache;
            string country = cache.NextCountry;
            var group = services.Where(s => s.IsUrlTestSupported && ServerCheckCache.CountryKey(s) == country)
                .OrderBy(s => s.ID).ToArray();
            if (group.Length == 0) return;
            _urlTestCts?.Dispose();
            _urlTestCts = new CancellationTokenSource();
            var token = _urlTestCts.Token;
            probeRunning = true;
            probeWakeRequested = false;
            UrlTestCoordinator.BeginBatch();
            bool completed = false;
            var countryTask = CheckCountryAsync(services, group, cache, country, token);
            probeTask = countryTask;
            try { completed = await countryTask; }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            finally { probeRunning = false; }
            if (completed && !token.IsCancellationRequested && ReferenceEquals(services, _currentServices))
            {
                if (cache.InitialScanCompleted) probeSchedule.Completed(DateTime.UtcNow);
                else probeSchedule.RestartNow();
            }
            if (probesPaused || globalInfo?.CurrentService != null) return;
            if (probeWakeRequested || token.IsCancellationRequested || (completed && !cache.InitialScanCompleted))
            {
                // A reload/resume arrived while the previous canceled worker was draining.
                RunBackgroundUrlTests(_currentServices);
                return;
            }
            probeTimer.Interval = probeSchedule.Remaining(DateTime.UtcNow);
            if (probeTimer.Interval <= TimeSpan.Zero) probeTimer.Interval = TimeSpan.FromMinutes(3);
            probeTimer.Start();
        }

        private async Task<bool> CheckCountryAsync(IVPNService[] services, IVPNService[] group,
            ServerCheckCache cache, string country, CancellationToken token)
        {
            foreach (var service in group)
            {
                if (token.IsCancellationRequested || UrlTestCoordinator.AbortRequested) return false;
                int acceptingProgress = 1;
                countryPicker.SetGroupChecking(service, true);
                try
                {
                    await Task.Run(() =>
                    {
                        DateTime started = DateTime.Now;
                        try
                        {
                            if (service is TunnelPlusService tunnel)
                                tunnel.UrlTestFull(null, false, latency =>
                                {
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        // A queued partial result must not overwrite a final/rolled-back
                                        // result, a replacement API row, or a newly connected session.
                                        if (Volatile.Read(ref acceptingProgress) != 0
                                            && !token.IsCancellationRequested && !probesPaused
                                            && !UrlTestCoordinator.AbortRequested
                                            && ReferenceEquals(services, _currentServices)
                                            && globalInfo?.CurrentService == null)
                                            countryPicker.ShowGroupProgress(service, latency);
                                    }));
                                }, () => token.IsCancellationRequested);
                            else service.UrlTest();
                        }
                        catch (Exception ex) { LogHelper.WriteLog(ex); }
                        finally { Interlocked.Exchange(ref acceptingProgress, 0); }

                        if (token.IsCancellationRequested || UrlTestCoordinator.AbortRequested)
                            cache.Restore(service);
                        else
                            cache.Record(service, started);
                    });
                }
                finally
                {
                    // Always stop the indicator, including cancellation and cache/write errors.
                    countryPicker.SetGroupChecking(service, false);
                }

                // This continuation runs on the UI thread. Apply the committed result
                // (or restored cache on cancellation) before advancing/draining the worker.
                // Cancellation must not discard this refresh as it did in the queued path.
                if (ReferenceEquals(services, _currentServices))
                    countryPicker.RefreshGroup(service);
                if (token.IsCancellationRequested || UrlTestCoordinator.AbortRequested) return false;
            }
            await Task.Run(() =>
            {
                if (!token.IsCancellationRequested && !UrlTestCoordinator.AbortRequested)
                    cache.CompleteCountry(country);
            });
            return !token.IsCancellationRequested && !UrlTestCoordinator.AbortRequested;
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
                OnConnectRequest.Invoke(this, selectedService, selectedProtocol, null);
            }
            else
            {
                ConnectToFastestServer();
            }
        }

        private void ConnectToFastestServer()
        {
            if (_selectedServiceName == null) return;

            var serviceName = _selectedServiceName;
            var protocol = selectedProtocol;
            var services = serviceFactory.Services.ToArray();

            // Show Connecting now; enumerate links and prepare the smart pool only
            // after the background checks have drained, on a worker thread.
            OnConnectRequest?.Invoke(this, null, protocol ?? "",
                () => PrepareFastestServer(services, serviceName, protocol));
        }

        private IVPNService PrepareFastestServer(IVPNService[] candidates, string serviceName, string protocol)
        {
            try
            {
                var services = candidates.Where(x => x.IsUrlTestSupported
                    && x.Name == serviceName
                    && (string.IsNullOrEmpty(protocol) || x.Protocols.Contains(protocol)))
                    .Randomize().ToList();
                var smartService = services.FirstOrDefault(x => x is ISmartFastConnection);
                var allUrls = services
                    .SelectMany(x => x.GetServerUrls() ?? new List<Url>())
                    .Where(u => u != null)
                    .OrderByHysteriaFirst()
                    .Select(u => u.url)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (smartService != null && allUrls.Length > 0)
                {
                    smartService.SelectedServerUrl = null;
                    ((ISmartFastConnection)smartService).SetSmartFastUrls(allUrls);
                    return smartService;
                }

                var fastest = services.Where(x => x.UrlTestSpeed > 0)
                    .OrderBy(x => x.UrlTestSpeed).FirstOrDefault();
                return fastest;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                return null;
            }
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
                icons.Add(new HeaderIconRegistration("", "حذف سرویس پایه", RemoveBaseService));
            icons.Add(new HeaderIconRegistration("\uf2f5", "خروج از حساب", () => host.LogoutFromSettings()));
            icons.Add(new HeaderIconRegistration("", "تنظیمات سرویس", OpenServiceSettings));

            host.SetHeaderIcons(this, icons);
        }

        private void ClearHeaderIcons() => GetMainWindow()?.ClearHeaderIcons(this);

        private void OpenServiceSettings()
        {
            GetMainWindow()?.OpenSettings(selectedService ?? GetFallbackService());
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
            if (_selectedServiceName == null) return null;
            return serviceFactory.Services?.FirstOrDefault(s => s.Name == _selectedServiceName);
        }

        #endregion
    }
}
