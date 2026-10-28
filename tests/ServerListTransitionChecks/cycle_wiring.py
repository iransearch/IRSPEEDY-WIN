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
start = background.index('        private async Task<bool> CheckServerAsync(')
check = background[start:background.index('        #endregion', start)]
assert check.index('picker.SetGroupChecking(service, true);') < check.index('await Task.Run(')
assert check.index('picker.SetGroupChecking(service, false);') < check.index('picker.RefreshGroup(service,')
assert 'allowSortAnimation: !token.IsCancellationRequested && !UrlTestCoordinator.AbortRequested' in check
progress = manual[manual.index('Action<Url, long> progress'):manual.index('Action<Url, long> complete')]
assert 'RefreshGroup(' not in progress and 'ShowGroupProgress(' in progress
complete = manual[manual.index('Action<Url, long> complete'):manual.index('TunnelPlusService.TestRefreshBatch(')]
assert 'foreach (var row in rowsDone)' in complete
assert complete.index('picker.SetGroupChecking(row, false);') < complete.index('picker.RefreshGroup(row);')
assert manual.index('picker.SetGroupChecking(row, true);') < manual.index('TunnelPlusService.TestRefreshBatch(')
print('PASS checking precedes probes; only completed country results publish sorting eligibility; canceled/partial results do not')

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

