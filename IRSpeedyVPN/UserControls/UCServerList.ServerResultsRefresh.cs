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

        private string DisplayTestTarget()
            => UrlTestRetryPolicy.DisplayTarget(globalInfo?.settings?.setting?.url_test);

        private async Task RunServerProbeRoundAsync(IVPNService[] pending, ServerCheckCache cache,
            Func<bool> current, CancellationToken token, bool showProgress)
        {
            if (pending.Length == 0 || !current()) return;
            var picker = countryPicker;
            var info = globalInfo;
            string target = DisplayTestTarget();
            var committed = new HashSet<IVPNService>(); // Only touched on the dispatcher.
            foreach (var row in pending) picker.SetGroupChecking(row, true);
            using (var round = CancellationTokenSource.CreateLinkedTokenSource(token, UrlTestCoordinator.DisplayCancellation))
            try
            {
                await Task.Run(async () =>
                {
                    if (!current()) return;
                    var plan = new ServerRefreshPlan(pending, failed: cache.WasDisplayFailure);
                    LogHelper.WriteExLog("[ServerChecks] display-round candidates=" + plan.Total + " rows=" + pending.Length);
                    cache.PersistQueue();
                    try
                    {
                        using (var session = new TunnelPlusService.DisplayProbeSession(info))
                        {
                            await plan.RunAsync((candidate, cancellation) => Task.Run(() =>
                            {
                                cancellation.ThrowIfCancellationRequested();
                                return TunnelPlusService.TestRefreshBatch(session, candidate.Service,
                                    candidate.Probe, target, cancellation, latency =>
                                {
                                    if (!cache.RememberDisplayProgress(candidate.Service, candidate.Original, latency,
                                        () => !round.IsCancellationRequested && current()) || !showProgress) return;
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        if (!round.IsCancellationRequested && current() && !committed.Contains(candidate.Service))
                                            picker.ShowGroupProgress(candidate.Service, cache.PeekDisplayPing(candidate.Service));
                                    }), System.Windows.Threading.DispatcherPriority.Background);
                                });
                            }, cancellation), (row, result) =>
                            {
                                if (!cache.CompleteDisplayRow(row, result.Winner, result.Latency, result.Failed,
                                    () => !round.IsCancellationRequested && current())) return;
                                Dispatcher.Invoke(new Action(() =>
                                {
                                    if (!current() || round.IsCancellationRequested) return;
                                    committed.Add(row);
                                    (row as TunnelPlusService)?.UpdateListTestResult();
                                    picker.SetGroupChecking(row, false);
                                    picker.RefreshGroup(row);
                                    TrySelectResidentialFastest();
                                    UpdateResidentialVisuals();
                                }));
                            }, round.Token);
                        }
                    }
                    finally { cache.PersistQueue(); }
                }, round.Token);
            }
            finally
            {
                // Invalidate queued previews even on a failed/canceled probe.
                foreach (var row in pending)
                {
                    picker.SetGroupChecking(row, false);
                    if (committed.Add(row)) picker.RestoreTestProgress(row);
                }
            }
        }

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
            // Stop/drain first. A canceled country test must restore its old
            // committed results before the explicit refresh clears those results.
            probesPaused = true;
            StopUrlTests();
            var cancellation = new CancellationTokenSource();
            manualRefreshCancellation = cancellation;
            var token = cancellation.Token;
            long generation = ++manualRefreshGeneration;
            Func<bool> current = () => !token.IsCancellationRequested && generation == manualRefreshGeneration
                && ReferenceEquals(services, _currentServices) && ReferenceEquals(cache, probeCache)
                && globalInfo?.CurrentService == null && !probesRequireDisconnect;
            try
            {
                await probeTask;
                if (!current()) return;
                probeRestartRequested = false; probeWakeRequested = false;
                // Rebind the latest API snapshot before admitting any probes.
                // Valid successes remain reusable, including on a manual round.
                bool listRefreshed = isResidentialTab
                    ? await residential.RefreshAsync(force: true, cancellation: token)
                    : RefreshServerListRequest != null && await RefreshServerListRequest(current, token);
                if (listRefreshed)
                {
                    services = _currentServices;
                    cache = probeCache;
                }
                if (!current()) return;
                cache.BindDisplay(services, DisplayTestTarget(), TunnelPlusService.selectedChain);
                await Task.Run(() => { if (current()) cache.RestartForLogin(); });
                if (!current()) return;
                picker.EndCheckCycleSetup();
                picker.RetainCompletedResults(cache.CompletedSuccessfulServices);
                UrlTestCoordinator.BeginBatch();
                var pending = cache.DisplayRoundServices.Where(services.Contains).ToArray();
                await RunServerProbeRoundAsync(pending, cache, current, token, listRefreshed);
                LogHelper.WriteExLog("[ServerChecks] manual-refresh-end canceled=" + !current());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            finally
            {
                // A connection/logout cancellation owns the pause until its
                // existing cleanup resumes it. Never restart an interrupted scan.
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
    }
}


