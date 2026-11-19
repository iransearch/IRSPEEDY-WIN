using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCServerList
    {
        private bool manualRefreshBusy;
        private Task manualRefreshTask = Task.CompletedTask;
        private CancellationTokenSource manualRefreshCancellation;
        private long manualRefreshGeneration;
        private bool manualRefreshExternalPause;

        private void CancelManualRefresh() => manualRefreshCancellation?.Cancel();

        private void RefreshServerResults()
        {
            if (!IsLoaded || !IsVisible || manualRefreshBusy || globalInfo?.CurrentService != null
                || _currentServices == null || probeCache == null || !_isUrlTestSupported) return;
            countryPicker.BeginCheckCycle();
            manualRefreshBusy = true;
            manualRefreshExternalPause = false;
            // A deliberate refresh grants a new scan even after a failed connect
            // consumed the normal scan. Connection startup can pause it again.
            probesRequireDisconnect = false;
            UpdateHeaderIcons();
            manualRefreshTask = RefreshServerResultsAsync(_currentServices, probeCache);
        }

        private async Task RefreshServerResultsAsync(IVPNService[] services, ServerCheckCache cache)
        {
            var picker = countryPicker;
            // The current scan owns its workers until every probe has drained.
            probesPaused = true;
            StopUrlTests();
            var cancellation = new CancellationTokenSource();
            manualRefreshCancellation = cancellation;
            var token = cancellation.Token;
            long generation = ++manualRefreshGeneration;
            bool Current() => !token.IsCancellationRequested && generation == manualRefreshGeneration
                && ReferenceEquals(services, _currentServices) && ReferenceEquals(cache, probeCache)
                && globalInfo?.CurrentService == null && !probesRequireDisconnect;
            try
            {
                await probeTask;
                if (!Current()) return;
                probeRestartRequested = false;
                probeWakeRequested = false;
                bool listRefreshed = isResidentialTab
                    ? await residential.RefreshAsync(force: true, cancellation: token)
                    : RefreshServerListRequest != null && await RefreshServerListRequest(Current, token);
                if (listRefreshed)
                {
                    services = _currentServices;
                    cache = probeCache;
                }
                if (!Current()) return;

                await Task.Run(() => { if (Current()) cache.RestartForLogin(); });
                if (!Current()) return;
                picker.EndCheckCycleSetup();
                picker.RetainCompletedResults(cache.CompletedSuccessfulServices);
                UrlTestCoordinator.BeginBatch();
                await RunDisplayScanAsync(services, cache, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            finally
            {
                if (!manualRefreshExternalPause && globalInfo?.CurrentService == null && !probesRequireDisconnect)
                    probesPaused = false;
                if (manualRefreshCancellation == cancellation) manualRefreshCancellation = null;
                cancellation.Dispose();
                picker.EndCheckCycleSetup();
                manualRefreshBusy = false;
                UpdateHeaderIcons();
                if (!ReferenceEquals(services, _currentServices) && !probesPaused) RunBackgroundUrlTests(_currentServices);
            }
        }

        private async Task<bool> RunDisplayScanAsync(IVPNService[] services, ServerCheckCache cache, CancellationToken cancellation)
        {
            var picker = countryPicker;
            var info = globalInfo;
            var testUrl = cache.TestUrl;
            bool OwnsRows() => ReferenceEquals(services, _currentServices) && ReferenceEquals(cache, probeCache);
            using (var scan = CancellationTokenSource.CreateLinkedTokenSource(cancellation, UrlTestCoordinator.DisplayCancellation))
            {
                var token = scan.Token;
                bool Current() => !token.IsCancellationRequested && OwnsRows()
                    && info?.CurrentService == null && !probesRequireDisconnect;
                var eligible = cache.PendingServices.Where(services.Contains).ToArray();
                var plan = await Task.Run(() => ServerRefreshPlan.Countries(eligible, cache.FailedLastDisplayRound, new Random()));
                if (!Current()) return false;

                var checking = new HashSet<IVPNService>(eligible);
                foreach (var row in checking) picker.SetGroupChecking(row, true);
                try
                {
                    await Task.Run(async () =>
                    {
                        cache.PersistQueue();
                        await ServerProbeScheduler.RunAsync(plan, (candidate, probeCancellation) => Task.Run(() =>
                        {
                            return candidate.Owners[0].Service is CiscoService
                                ? CiscoService.TestDisplayServer(candidate.Probe, probeCancellation)
                                : TunnelPlusService.TestRefreshBatch(candidate.Probe, info, testUrl, probeCancellation);
                        }, probeCancellation), (row, winner, measurements) =>
                        {
                            if (!Current() || !cache.CommitDisplayRow(row, winner, measurements, token)) return;
                            Dispatcher.Invoke(() =>
                            {
                                if (!OwnsRows()) return;
                                (row as TunnelPlusService)?.UpdateListTestResult();
                                (row as CiscoService)?.UpdateListTestResult();
                                checking.Remove(row);
                                picker.SetGroupChecking(row, false);
                                picker.RefreshGroup(row, allowSortAnimation: Current());
                                TrySelectResidentialFastest();
                                UpdateResidentialVisuals();
                            });
                        }, token).ConfigureAwait(false);
                    });
                    return Current();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
                finally
                {
                    if (OwnsRows())
                        foreach (var row in checking)
                        {
                            picker.SetGroupChecking(row, false);
                            picker.RestoreTestProgress(row);
                        }
                }
            }
        }
    }
}


