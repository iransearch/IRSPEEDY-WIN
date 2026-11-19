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

    static async Task MainAsync(string directory) {
        await RebindAfterFetch(directory);
        await RefreshWithoutReplacement(directory);
        await CancelFetch(directory);
        await WaitForOldWorker(directory);
        await CancelDuringFallback(directory);
        await RefreshResidential(directory);
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
            return new ServerProbeMeasurement(88);
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
        Check(calls.Contains(winner.url) && calls.All(link => added.urls.Any(url => url.url == link)),
              "only the new row needs a probe and the winner is one of its measured links");
        Check(calls.Count <= ServerProbeScheduler.Capacity && skipped.Length > 0,
              "a good result skips the links that did not start");
        Check(skipped.All(url => url.latency == 0 && url.latencychkTime == default(DateTime).ToLocalTime() &&
                  !view.probeCache.FailedLastDisplayRound(added, url)),
              "skipped links have no measured ping, check time, or failure history");
        Check(Ping(same) == 96 && view.countryPicker.Retained.SequenceEqual(new[] { same }) &&
              view.countryPicker.Published.SequenceEqual(new[] { added }) && !view.manualRefreshBusy,
              "cached success still sorts and only the newly completed row is published on the UI thread");
    }

    static async Task RefreshWithoutReplacement(string directory) {
        var retained = S("DE", 64);
        var pending = S("FI", 65);
        var other = S("FI", 66, pending.urls[0].url);
        var view = View(directory, "manual-unavailable", retained, pending, other);
        SaveSuccess(view, retained, 97);
        view.RefreshServerListRequest = (current, token) => Task.FromResult(false);
        var calls = new ConcurrentQueue<string>();
        TunnelPlusService.Probe = (url, target, token) => {
            calls.Enqueue(url.url);
            return new ServerProbeMeasurement(124);
        };
        view.RefreshServerResults();
        await view.manualRefreshTask;
        Check(calls.Count == 2 && calls.All(link => link == pending.urls[0].url) &&
              Ping(pending) == 124 && Ping(other) == 124 && Ping(retained) == 97 && !view.manualRefreshBusy,
              "API failure retries only missing rows in the previous list and identical links in two rows are independent");

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
              !view.countryPicker.Published.Any() && Ping(row) == 0,
              "a late API response cannot start probes or unpause a pending connection");
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
        var original = S("DE", 71);
        var current = new IRSpeedyVPN.Residential.ResidentialService { ID = 72, CountryCode = "CA" };
        current.urls.Add(new Url { url = "vless://residential-fixture@example.invalid" });
        var view = new Program(directory, "manual-residential") { probesPaused = true, isResidentialTab = true };
        view.Bind(new[] { original });
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
            return new ServerProbeMeasurement(199);
        };
        view.RefreshServerResults();
        await view.manualRefreshTask;
        Check(loads == 1 && calls.SequenceEqual(new[] { current.urls[0].url }) && Ping(current) == 199 &&
              view.countryPicker.Published.SequenceEqual(new[] { current }),
              "Residential replacement uses the shared display scanner and publishes its completed result");
    }
}
'''

if __name__ == '__main__':
    run_checks(checks, sys.argv[1] if len(sys.argv) > 1 else 'dotnet')
