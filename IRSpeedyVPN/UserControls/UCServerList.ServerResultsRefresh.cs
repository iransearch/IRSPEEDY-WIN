using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services;
using System;
using System.Collections.Concurrent;
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
            // Stop/drain first. A canceled country test must restore its old
            // committed results before the explicit refresh clears those results.
            probesPaused = true;
            StopUrlTests();
            var cancellation = new CancellationTokenSource();
            manualRefreshCancellation = cancellation;
            var token = cancellation.Token;
            long generation = ++manualRefreshGeneration;
            bool refreshStarted = false;
            Func<bool> current = () => !token.IsCancellationRequested && generation == manualRefreshGeneration
                && ReferenceEquals(services, _currentServices) && ReferenceEquals(cache, probeCache)
                && globalInfo?.CurrentService == null && !probesRequireDisconnect;
            try
            {
                await probeTask;
                if (!current()) return;
                probeRestartRequested = false; probeWakeRequested = false;
                // Fetch and bind the latest API snapshot while this round owns
                // the pause. A failed fetch tests the old list without erasing
                // its committed results; cancellation still stops the round.
                bool listRefreshed = !isResidentialTab && RefreshServerListRequest != null && await RefreshServerListRequest(current, token);
                if (listRefreshed)
                {
                    services = _currentServices;
                    cache = probeCache;
                }
                if (!current()) return;
                refreshStarted = await Task.Run(() =>
                {
                    if (!current()) return false;
                    if (listRefreshed) return cache.ClearForManualRefresh(services);
                    cache.StopRound();
                    return true;
                });
                if (!refreshStarted) return;
                if (!current()) return;
                if (listRefreshed)
                {
                    foreach (var service in services.OfType<TunnelPlusService>()) service.ResetListTestResult();
                    picker.ResetTestResults();
                }
                picker.EndCheckCycleSetup();
                var plan = new ServerRefreshPlan(services);
                var finishedRows = new ConcurrentDictionary<IVPNService, bool>();
                var bestByRow = new ConcurrentDictionary<IVPNService, long>();
                UrlTestCoordinator.BeginBatch();
                LogHelper.WriteExLog("[ServerChecks] manual-refresh-begin candidates=" + plan.Total + " listRefreshed=" + listRefreshed);
                while (current() && plan.HasPending)
                {
                    var batch = plan.TakeBatch();
                    foreach (var row in batch.SelectMany(c => c.Owners).Select(o => o.Service).Distinct())
                        picker.SetGroupChecking(row, true);
                    await Task.Run(() =>
                    {
                        if (!current()) return;
                        var members = batch.ToDictionary(c => c.Probe);
                        Action<Url, long> progress = (url, latency) =>
                        {
                            ServerRefreshPlan.Candidate member;
                            if (latency <= 0 || !current() || !members.TryGetValue(url, out member) || member.Completed
                                || (member.BestSuccessfulLatency > 0 && latency >= member.BestSuccessfulLatency)) return;
                            member.BestSuccessfulLatency = latency;
                            // Progress is an actual successful probe, even when its
                            // primary batch or >500ms retry is still pending. Commit
                            // before making the row selectable so Connect/logout can
                            // cancel the remaining work without losing this ping.
                            foreach (var owner in member.Owners)
                                cache.RecordRefreshMember(owner.Service, owner.Url, latency, DateTime.UtcNow);
                            if (!listRefreshed) return;
                            foreach (var owner in member.Owners.Select(o => o.Service).Distinct())
                            {
                                bestByRow.AddOrUpdate(owner, latency, (s, old) => Math.Min(old, latency));
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    long best;
                                    if (current() && !finishedRows.ContainsKey(owner) && bestByRow.TryGetValue(owner, out best))
                                        picker.ShowGroupProgress(owner, best);
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                        };
                        Action<Url, long> complete = (url, latency) =>
                        {
                            ServerRefreshPlan.Candidate member;
                            if (!current() || !members.TryGetValue(url, out member) || member.Completed) return;
                            if (member.BestSuccessfulLatency > 0)
                                latency = latency > 0 ? Math.Min(latency, member.BestSuccessfulLatency) : member.BestSuccessfulLatency;
                            var rowsDone = plan.Complete(member);
                            foreach (var owner in member.Owners)
                                cache.RecordRefreshMember(owner.Service, owner.Url, latency, DateTime.UtcNow);
                            foreach (var row in rowsDone) { finishedRows[row] = true; cache.CompleteRefreshRow(row); }
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!current()) return;
                                foreach (var row in member.Owners.Select(o => o.Service).Distinct())
                                    (row as TunnelPlusService)?.UpdateListTestResult();
                                foreach (var row in rowsDone)
                                {
                                    picker.SetGroupChecking(row, false);
                                    picker.RefreshGroup(row);
                                }
                                TrySelectResidentialFastest();
                            }));
                        };
                        TunnelPlusService.TestRefreshBatch(batch.Select(c => c.Probe).ToArray(), globalInfo, token, progress, complete);
                        // A global RPC/config error may return without per-member
                        // results. It cannot leave the refresh spinning forever.
                        foreach (var member in batch.Where(c => !c.Completed)) complete(member.Probe, -1);
                        if (current()) cache.PersistQueue();
                    });
                }
                if (current()) await Task.Run(() => { if (current()) cache.FinishManualRefresh(services); });
                LogHelper.WriteExLog("[ServerChecks] manual-refresh-end canceled=" + !current());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            finally
            {
                // Persist every actual successful measurement accepted before
                // cancellation, including members whose batch/retry is unfinished.
                if (refreshStarted) await Task.Run(() => cache.PersistQueue());
                if (ReferenceEquals(services, _currentServices))
                {
                    foreach (var service in services)
                    {
                        picker.SetGroupChecking(service, false);
                        picker.RestoreTestProgress(service);
                        (service as TunnelPlusService)?.UpdateListTestResult();
                    }
                }
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


