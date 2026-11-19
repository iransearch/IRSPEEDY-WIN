"""Exercise production manual refresh, from an API response to displayed row results."""
import sys
from run import run_checks

checks = r'''
    static Program View(string directory, string account, params IVPNService[] rows) {
        var view = new Program(directory, account) { probesPaused = true };
        view.Bind(rows);
        return view;
    }

    static void SaveSuccess(Program view, TunnelPlusService row, long ping) {
        var winner = row.urls[0];
        if (!view.probeCache.CommitDisplayRow(row, winner, new Dictionary<Url, long> { [winner] = ping }))
            throw new Exception("could not seed an eligible cached row");
    }

    static bool HasNoDisplayResult(IVPNService row) => row.GetServerUrls().All(url =>
        url.latency == 0 && url.latencychkTime == default(DateTime) &&
        url.LastSuccessfulLatency == 0 && url.LastSuccessfulCheckTime == default(DateTime));

    static async Task MainAsync(string directory) {
        await RecheckWithOnlyCachedSuccesses(directory);
        await RebindAfterFetch(directory);
        await RefreshWithoutReplacement(directory);
        await CancelFetch(directory);
        await WaitForOldWorker(directory);
        await CancelDuringFallback(directory);
        await RefreshResidential(directory);
    }

    static async Task RecheckWithOnlyCachedSuccesses(string directory) {
        var cached = S("DE", 60);
        var replacement = S("DE", 60);
        const string failedLink = "vless://fixture-retry@example.invalid";
        cached.urls.Add(new Url { url = failedLink });
        replacement.urls.Add(new Url { url = failedLink });
        var view = View(directory, "manual-all-healthy", cached);
        var oldWinner = cached.urls[0];
        if (!view.probeCache.CommitDisplayRow(cached, oldWinner,
            new Dictionary<Url, long> { [oldWinner] = 96, [cached.urls[1]] = -1 }))
            throw new Exception("could not seed the existing winner and failed link");
        view.RefreshServerListRequest = (current, token) => {
            view.Bind(new[] { replacement }, preserveManualRefresh: true);
            return Task.FromResult(true);
        };
        var entered = Signal();
        using (var release = new ManualResetEventSlim()) {
            TunnelPlusService.Probe = (url, target, token) => {
                entered.TrySetResult(true);
                if (!release.Wait(5000)) throw new TimeoutException("waiting to release manual display probe");
                return url.url == failedLink ? ServerProbeMeasurement.Failed : new ServerProbeMeasurement(142);
            };
            view.RefreshServerResults();
            var first = await Task.WhenAny(entered.Task, view.manualRefreshTask);
            try {
                Check(first == entered.Task, "manual refresh starts a probe even when every row has a valid cached result");
                Check(HasNoDisplayResult(replacement) && view.countryPicker.ResultResets == 1 &&
                      view.countryPicker.Checking.Contains(replacement),
                      "manual refresh clears the previous ping and failure before the first new result");
                Check(view.probeCache.FailedLastDisplayRound(replacement, replacement.urls[1]),
                      "the previous failed link still has scheduling priority");
                var reloaded = S("DE", 60);
                reloaded.urls.Add(new Url { url = failedLink });
                var cold = View(directory, "manual-all-healthy", reloaded);
                Check(HasNoDisplayResult(reloaded) && cold.probeCache.CompletedSuccessfulServices.Length == 0,
                      "a fresh cache instance cannot restore an old result while manual refresh is running");
            }
            finally { release.Set(); }
            await view.manualRefreshTask;
        }
        Check(Ping(replacement) == 142 && view.countryPicker.Published.SequenceEqual(new[] { replacement }),
              "the manual probe publishes the fresh result");
        TunnelPlusService.Probe = (url, target, token) => throw new Exception("automatic startup must keep manual success");
        var startup = new Program(directory, "manual-all-healthy");
        var restored = S("DE", 60);
        restored.urls.Add(new Url { url = failedLink });
        startup.Bind(new[] { restored });
        await startup.Settle();
        Check(Ping(restored) == 142 && !startup.countryPicker.Published.Any(),
              "automatic startup restores the new success without probing it again");
    }

    static async Task RebindAfterFetch(string directory) {
        var retained = S("DE", 61);
        var removed = S("US", 62);
        var view = View(directory, "manual-fresh", retained, removed);
        SaveSuccess(view, retained, 96);
        var same = S("DE", 61);
        var added = S("FI", 63);
        for (int index = 0; index < 9; index++)
            added.urls.Add(new Url { url = "vless://fixture-other-" + index + "@example.invalid" });
        var release = Signal();
        var calls = new ConcurrentQueue<string>();
        int fetches = 0;
        view.RefreshServerListRequest = async (current, token) => {
            fetches++;
            Check(current() && view.probesPaused, "manual fetch owns the paused, loaded list");
            await release.Task;
            if (!current()) return false;
            view.Bind(new[] { same, added }, preserveManualRefresh: true);
            Check(!current() && !token.IsCancellationRequested, "manual refresh owns its API rebind without canceling the round");
            return true;
        };
        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            url.latency = 999;
            return new ServerProbeMeasurement(url.url == same.urls[0].url ? 104 : 88);
        };
        view.RefreshServerResults();
        view.RefreshServerResults();
        Check(fetches == 1 && calls.IsEmpty && Ping(retained) == 96 && view.manualRefreshBusy &&
              view.countryPicker.CycleStarts == 1,
              "duplicate clicks share the fetch and preserve displayed results while it runs");
        release.SetResult(true);
        await view.manualRefreshTask;
        var winner = added.urls.Single(url => url.latency == 88);
        var skipped = added.urls.Where(url => !calls.Contains(url.url)).ToArray();
        Check(calls.Contains(winner.url) && calls.Count(link => link == same.urls[0].url) == 1 &&
              calls.All(link => link == same.urls[0].url || added.urls.Any(url => url.url == link)),
              "the fresh list rechecks the cached and new rows and excludes the removed row");
        Check(calls.Count(link => link != same.urls[0].url) <= ServerProbeScheduler.Capacity && skipped.Length > 0,
              "a good result skips the links that did not start");
        Check(skipped.All(url => url.latency == 0 && url.latencychkTime == default(DateTime).ToLocalTime() &&
                  !view.probeCache.FailedLastDisplayRound(added, url)),
              "skipped links have no measured ping, check time, or failure history");
        Check(Ping(same) == 104 && view.countryPicker.Retained.Length == 0 && view.countryPicker.ResultResets == 1 &&
              view.countryPicker.Published.Count == 2 && view.countryPicker.Published.Contains(same) &&
              view.countryPicker.Published.Contains(added) && !view.manualRefreshBusy,
              "manual refresh discards initial sorting evidence and publishes both fresh results on the UI thread");
    }

    static async Task RefreshWithoutReplacement(string directory) {
        var retained = S("DE", 64);
        var pending = S("FI", 65);
        var other = S("FI", 66, pending.urls[0].url);
        var view = View(directory, "manual-unavailable", retained, pending, other);
        SaveSuccess(view, retained, 97);
        view.selectedService = view.countryPicker.SelectedService = retained;
        view.RefreshServerListRequest = (current, token) => Task.FromResult(false);
        var calls = new ConcurrentQueue<string>();
        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            if (url.url != retained.urls[0].url) return new ServerProbeMeasurement(124);
            Check(HasNoDisplayResult(retained), "manual refresh also clears results when using the existing catalog");
            return new ServerProbeMeasurement(125);
        };
        view.RefreshServerResults();
        await view.manualRefreshTask;
        Check(calls.Count == 3 && calls.Count(link => link == pending.urls[0].url) == 2 &&
              calls.Contains(retained.urls[0].url) && Ping(pending) == 124 && Ping(other) == 124 &&
              Ping(retained) == 125 && !view.manualRefreshBusy && view.countryPicker.SelectedService == retained,
              "API failure rechecks the previous list and identical links in two rows are independent");

        TunnelPlusService.Probe = (url, target, token) => throw new Exception("empty list cannot probe");
        view.RefreshServerListRequest = (current, token) => {
            view.Bind(new IVPNService[0], preserveManualRefresh: true);
            return Task.FromResult(true);
        };
        view.RefreshServerResults();
        await view.manualRefreshTask;
        Check(view._currentServices.Length == 0 && !view.manualRefreshBusy,
              "empty fresh list completes without probing removed rows");
    }

    static async Task CancelFetch(string directory) {
        var row = S("DE", 67);
        var view = View(directory, "manual-cancel-fetch", row);
        SaveSuccess(view, row, 93);
        var download = Signal();
        view.RefreshServerListRequest = async (current, token) => {
            await download.Task;
            return current();
        };
        TunnelPlusService.Probe = (url, target, token) => throw new Exception("canceled fetch cannot probe");
        view.RefreshServerResults();
        view.PauseServerChecksForConnection();
        download.SetResult(true);
        await view.DrainServerChecksAsync();
        Check(view.probesPaused && view.probesRequireDisconnect && !view.manualRefreshBusy &&
              !view.countryPicker.Published.Any() && view.countryPicker.ResultResets == 0 && Ping(row) == 93,
              "canceling the catalog fetch leaves the previous result intact and cannot start a scan");
    }

    static async Task WaitForOldWorker(string directory) {
        var row = S("DE", 68);
        var view = new Program(directory, "manual-drain");
        var entered = Signal();
        var stopped = Signal();
        int count = 0, fetches = 0;
        using (var release = new ManualResetEventSlim()) {
            TunnelPlusService.Probe = (url, target, token) => {
                if (Interlocked.Increment(ref count) > 1) return new ServerProbeMeasurement(220);
                using (token.Register(() => stopped.TrySetResult(true))) {
                    entered.SetResult(true);
                    if (!release.Wait(5000)) throw new TimeoutException("waiting to release old display probe");
                    return new ServerProbeMeasurement(1);
                }
            };
            view.Bind(new[] { row });
            await entered.Task;
            view.RefreshServerListRequest = (current, token) => { fetches++; return Task.FromResult(false); };
            view.RefreshServerResults();
            await stopped.Task;
            Check(fetches == 0 && count == 1 && view.manualRefreshBusy,
                  "manual refresh cancels and drains the old worker before fetching or probing");
            release.Set();
            await view.manualRefreshTask;
            await view.Settle();
            Check(fetches == 1 && count == 2 && Ping(row) == 220 && view.countryPicker.Published.SequenceEqual(new[] { row }),
                  "after drain the manual round publishes only its own final measurement");
        }
    }

    static async Task CancelDuringFallback(string directory) {
        var completed = S("DE", 69);
        var pending = S("GR", 70);
        var view = View(directory, "manual-fallback", completed, pending);
        SaveSuccess(view, pending, 190);
        var retrying = Signal();
        var canceled = Signal();
        view.RefreshServerListRequest = (current, token) => Task.FromResult(false);
        using (var release = new ManualResetEventSlim()) {
            TunnelPlusService.Probe = (url, target, token) => {
                if (url.url == completed.urls[0].url) return new ServerProbeMeasurement(276);
                return UrlTestRetryPolicy.RunDisplay(target, (phase, remaining) => {
                    if (phase == target) return new ServerProbeMeasurement(501);
                    if (phase != UrlTestRetryPolicy.RetryUrl) throw new Exception("missing fallback");
                    using (token.Register(() => canceled.TrySetResult(true))) {
                        retrying.SetResult(true);
                        if (!release.Wait(5000)) throw new TimeoutException("waiting to release canceled fallback");
                        return new ServerProbeMeasurement(120);
                    }
                }, () => 0, token);
            };
            view.RefreshServerResults();
            await retrying.Task;
            await Until(() => view.countryPicker.Published.Count == 1);
            Check(Ping(completed) == 276 && Ping(pending) == 0 && view.countryPicker.Checking.Contains(pending),
                  "a completed country is published while another country's primary measurement still awaits fallback");
            view.PauseServerChecksForConnection();
            await canceled.Task;
            var drain = view.DrainServerChecksAsync();
            Check(!drain.IsCompleted, "connection waits for the canceled manual probe to exit");
            release.Set();
            await drain;
        }
        Check(HasNoDisplayResult(pending), "canceling a started manual scan does not bring cleared results back");
        var done = S("DE", 69);
        var missing = S("GR", 70);
        view.PrepareServerChecksForLogin();
        var calls = new ConcurrentQueue<string>();
        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            return new ServerProbeMeasurement(244);
        };
        view.Bind(new[] { done, missing });
        await view.Settle();
        Check(calls.SequenceEqual(new[] { missing.urls[0].url }) && Ping(done) == 276 && Ping(missing) == 244 &&
              !view.countryPicker.Checking.Any(), "login retains the completed country and retries the unfinished fallback");
    }

    static async Task RefreshResidential(string directory) {
        var original = new IRSpeedyVPN.Residential.ResidentialService { ID = 72, CountryCode = "CA" };
        original.urls.Add(new Url { url = "vless://residential-fixture@example.invalid" });
        var current = new IRSpeedyVPN.Residential.ResidentialService { ID = 72, CountryCode = "CA" };
        current.urls.Add(new Url { url = original.urls[0].url });
        var view = new Program(directory, "manual-residential") { probesPaused = true, isResidentialTab = true };
        view.Bind(new[] { original });
        SaveSuccess(view, original, 133);
        view.RefreshServerListRequest = (currentView, token) => throw new Exception("Residential should refresh its own catalog");
        int loads = 0;
        view.residential.Load = (force, token) => {
            Check(force && !token.IsCancellationRequested, "Residential manual refresh requests a fresh catalog");
            loads++;
            view.Bind(new[] { current }, preserveManualRefresh: true);
            return Task.FromResult(true);
        };
        var calls = new ConcurrentQueue<string>();
        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            Check(HasNoDisplayResult(current), "Residential clears the old result before probing its replacement");
            return new ServerProbeMeasurement(199);
        };
        view.RefreshServerResults();
        await view.manualRefreshTask;
        Check(loads == 1 && calls.SequenceEqual(new[] { current.urls[0].url }) && Ping(current) == 199 &&
              view.countryPicker.Published.SequenceEqual(new[] { current }),
              "Residential rechecks its cached success with the shared display scanner and publishes its new result");
    }
}
'''

if __name__ == '__main__':
    run_checks(checks, sys.argv[1] if len(sys.argv) > 1 else 'dotnet')
