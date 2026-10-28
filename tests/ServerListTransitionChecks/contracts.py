"""UI/old-path contracts for the current shared VPN/Residential template."""
from pathlib import Path
import subprocess,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[2];folder=root/'IRSpeedyVPN/Components/ServerListControl'
w='{http://schemas.microsoft.com/winfx/2006/xaml/presentation}';x='{http://schemas.microsoft.com/winfx/2006/xaml}';l='{clr-namespace:IRSpeedyVPN.Components.ServerListControl}'
xml=ET.parse(folder/'ServerCountryPicker.xaml').getroot()
def row(tree):return next(e for e in tree.iter(w+'DataTemplate') if e.get(x+'Key')=='ServerRowContent')
a=row(xml)
# The shared template now has Residential metadata and sizing. Check the VPN
# defaults and retained artwork invariants without requiring an obsolete commit.
parts_by_id={e.get(l+'ServerCometPart.Part'):e for e in a.iter() if e.get(l+'ServerCometPart.Part') is not None}
for key in ['4','3']:
 style=parts_by_id[key].find(w+'Grid.Style')
 assert style is not None and any(t.get('Value')=='{x:Null}' for t in style.iter(w+'DataTrigger'))
 assert any(t.get('Property')=='Visibility' and t.get('Value')=='Hidden' for t in style.iter(w+'Setter'))
model=(folder/'ServerListViewModel.cs').read_text()
assert 'RowHeight => IsResidential ? 66 : 52' in model and 'FlagSize => IsResidential ? 34 : 30' in model
country=next(e for e in a.iter(w+'TextBlock') if any('CountryName' in child.get('Text','') for child in e))
assert country.get('FontSize')=='14' and country.get('Foreground')=='{DynamicResource RowTitleBrush}'
ping=next(e for e in a.iter(w+'TextBlock') if 'SignalText' in e.get('Text',''))
assert ping.get('FontSize')=='12.5' and ping.get('FontWeight')=='Bold'
assert not any('IsFastest' in value for e in a.iter() for value in e.attrib.values())
assert len([e for e in a.iter(w+'Run') if 'CountryName' in e.get('Text','')])==1
print('PASS current shared template preserves VPN sizing, country/ping styling, slot guards and has no fastest row badge')
text=(folder/'ServerCountryPicker.xaml').read_text()
assert text.count('<local:ServerListCometOverlay ')==1
assert all(name not in text for name in ['LightSweepRowTransition','SmartMosaicShift','SmartCardShine'])
assert not (folder/'LightSweepRowTransition.cs').exists() and not (folder/'LightSweepAnimationPlan.cs').exists()
print('PASS one multi-band overlay host; old reorder/crossfade and smart-card light paths remain removed')
parts=[e.get(l+'ServerCometPart.Part') for e in row(ET.parse(folder/'ServerCountryPicker.xaml').getroot()).iter() if e.get(l+'ServerCometPart.Part')]
assert sorted(parts)==['0','1','2','3','4'],'deferred XAML must use stable row-part IDs, not enum names that SmartAssembly can rename'
c=(folder/'ServerListSweepAnimator.cs').read_text();frame=c[c.index('        private void Rendering('):c.index('        private void Reveal(')]
assert all(v not in frame for v in ['OrderBy','Sort(','Task.','await ','Invoke(','File.','UrlTest','GetServerUrls','BitmapImage','Thread.','lock ('])
assert c.count('CompositionTarget.Rendering +=')==1 and 'CompositionTarget.Rendering -=' in c and 'Stopwatch' in c
print('PASS measured existing groups and one elapsed-time callback without backend/ranking/I/O')
m=(root/'IRSpeedyVPN/UserControls/UCServerList.ServerResultsRefresh.cs').read_text()
assert m.index('countryPicker.BeginCheckCycle()')<m.index('manualRefreshTask = RefreshServerResultsAsync')
assert m.index('picker.EndCheckCycleSetup();')<m.index('var plan = new ServerRefreshPlan')
assert 'RefreshGroup(' not in m[m.index('            finally'):]
picker=(folder/'ServerCountryPicker.xaml.cs').read_text();completed=picker[picker.index('        public void RefreshGroup('):picker.index('        public void MarkSuccessfulConnection(')]
assert completed.index('ResortGroups();')<completed.index('RefreshDisplayedRows(allowReorder:')
assert 'CacheOption = BitmapCacheOption.OnLoad' in picker and '.Freeze()' in picker
print('PASS reset has no light; country completion publishes existing order; no all-finished animation; flags preloaded/frozen')

background=(root/'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
restart=background[background.index('        internal void ResumeServerChecksAfterCleanup'):background.index('        private void StopUrlTests()')]
assert 'countryPicker.BeginCheckCycle();' in restart
assert 'countryPicker.EndCheckCycleSetup();' in background
assert 'allowSortAnimation: !token.IsCancellationRequested && !UrlTestCoordinator.AbortRequested' in background
print('PASS explicit disconnect starts a new presentation cycle; canceled old completions request no sorting effect')

flag_source=(folder/'ServerCountryPicker.ProbeShine.cs').read_text()
assert 'LightSweepRowTransition' not in flag_source
assert 'SystemParameters.ClientAreaAnimation' in flag_source and 'ProbeShine_Changed' in text
sheen=next(e for e in row(ET.parse(folder/'ServerCountryPicker.xaml').getroot()).iter(w+'Rectangle') if e.get('Loaded')=='ProbeShine_Changed')
assert any('IsChecking' in e.get('Binding','') and e.get('Value')=='True' for e in sheen.iter(w+'DataTrigger'))
assert 'DrawRoundedRectangle' not in (folder/'ServerListCometOverlay.cs').read_text()
print('PASS checking sheen is clipped inside its flag; white content rectangles remain removed')

vm=(folder/'ServerListViewModel.cs').read_text()
transitions=(folder/'ServerCountryPicker.Transitions.cs').read_text()
assert '_serverList.BeginCheckCycle();' in transitions
assert 'completedIdentity: allowSortAnimation && !_checkCycleSetup' in completed
assert '!item.IsChecking && _completedChecks.Contains(item.Identity)' in vm
assert 'var target = PresentationOrder()' in vm
assert 'rows.Add(new ServerSortRow(i, Slots[i], target[i]))' in vm
assert 'var displaced' not in vm
assert 'foreach (var assignment in _pass.Rows)' in c and 'new ServerCometBand' in c
assert '_overlay.Bands.Add(band)' in c
assert 'var pinned = latest.FirstOrDefault(item => item.IsLastSuccessfulConnection)' in vm
assert 'membershipChanged || pinChanged' in vm
print('PASS ordinary sort requires completion; pin priority bypasses eligibility and cancels stale targets')

