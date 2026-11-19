"""Check the production cycle/completion wiring and animator handoff."""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
folder = root / 'IRSpeedyVPN/Components/ServerListControl'
picker = (folder / 'ServerCountryPicker.xaml.cs').read_text()
manual = (root / 'IRSpeedyVPN/UserControls/UCServerList.ServerResultsRefresh.cs').read_text()
background = (root / 'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
animator = (folder / 'ServerListSweepAnimator.cs').read_text()

load = picker[picker.index('        public void Load('):picker.index('        public void SetFilter(')]
assert load.count('_serverList.BeginCheckCycle();') == 1
assert 'if (!_checkCycleSetup) _serverList.BeginCheckCycle();' in load
assert manual.count('countryPicker.BeginCheckCycle();') == 1
assert manual.index('countryPicker.BeginCheckCycle();') < manual.index('manualRefreshTask = RefreshServerResultsAsync')
assert background.count('countryPicker.BeginCheckCycle();') == 1
print('PASS one cycle on initial load/disconnect/manual refresh; API rebind reuses the manual cycle')

checking = picker[picker.index('        public void SetGroupChecking('):picker.index('        public void ResetTestResults(')]
assert checking.index('if (checking) _serverList.MarkChecking(') < checking.index('group.IsChecking = checking;')
finished = picker[picker.index('        public void RefreshGroup('):picker.index('        public void MarkSuccessfulConnection(')]
assert finished.index('group.RefreshSignals();') < finished.index('ResortGroups();') < finished.index('RefreshDisplayedRows(')
assert 'completedIdentity: allowSortAnimation && !_checkCycleSetup ? LastSuccessfulServer.Key(group.Service) : null' in finished
start = manual.index('        private async Task<bool> RunDisplayScanAsync(')
refresh_handler, scan = manual[:start], manual[start:]
assert 'RunDisplayScanAsync(services, cache, token)' in background and 'RunDisplayScanAsync(services, cache, token)' in refresh_handler
assert scan.index('picker.SetGroupChecking(row, true);') < scan.index('ServerProbeScheduler.RunAsync(')
assert scan.index('if (!Current() || !cache.CommitDisplayRow(row, winner, measurements, token)) return;') < scan.index('Dispatcher.Invoke(')
assert scan.index('picker.SetGroupChecking(row, false);') < scan.index('picker.RefreshGroup(row, allowSortAnimation: Current());')
assert scan.count('picker.RefreshGroup(') == 1 and 'ShowGroupProgress(' not in scan
cleanup = scan[scan.index('                finally'):]
assert 'if (OwnsRows())' in cleanup and 'picker.RestoreTestProgress(row);' in cleanup
print('PASS checking precedes probes; only completed country results publish sorting eligibility; canceled/partial results do not')

refresh = background[background.index('        private void RefreshCountry('):background.index('        private static void AssignCountryIndices(')]
assert 'if (!preserveManualRefresh) countryPicker.RetainCompletedResults(probeCache.CompletedSuccessfulServices);' in refresh
assert refresh.index('countryPicker.Load(') < refresh.index('countryPicker.RetainCompletedResults(') < refresh.index('RunBackgroundUrlTests(services);')
restart = background[background.index('            if (probeRestartRequested)'):background.index('            var service = cache.NextService;')]
assert restart.index('cache.RestartFromFirstCountry();') < restart.index('picker.EndCheckCycleSetup();') < restart.index('picker.RetainCompletedResults(cache.CompletedSuccessfulServices);')
transitions = (folder / 'ServerCountryPicker.Transitions.cs').read_text()
retain = transitions[transitions.index('        public void RetainCompletedResults('):transitions.index('        private readonly List<GroupItem>')]
assert '_serverList.RetainCompletedChecks(services.Select(LastSuccessfulServer.Key));' in retain
assert 'RefreshDisplayedRows(instant: true);' in retain
assert refresh_handler.index('cache.RestartForLogin();') < refresh_handler.index('picker.RetainCompletedResults(cache.CompletedSuccessfulServices);') < refresh_handler.index('await RunDisplayScanAsync(')
print('PASS login, rebind, disconnect and manual refresh retain cached completion evidence before queue checks')

finish = animator[animator.index('        private void FinishPass('):animator.index('        private void StopIfIdle(')]
assert finish.index('_model.Finish(_pass);') < finish.index('_pass = null;') < finish.index('QueuePrepare();')
assert 'while ((_pass = _model.TryBeginPass()) != null)' in animator
assert '_model.Finish(_pass); _pass = null; _surfaces.Clear(); _overlay.Bands.Clear(); continue;' in animator
assert 'foreach (var assignment in _pass.Rows)' in animator
assert '_overlay.Bands.Add(band);' in animator
assert '_surfaces.Select(surface => surface.Centers).ToArray()' in animator
frame = animator[animator.index('        private void Rendering('):animator.index('        private void Reveal(')]
assert not any(token in frame for token in ('OrderBy', 'Task.', 'await ', 'Invoke(', 'File.', 'UrlTest', 'BitmapImage'))
print('PASS every changed row gets its existing band; visible and offscreen Finish paths immediately plan queued work outside rendering')

