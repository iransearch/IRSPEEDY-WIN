"""UI/old-path contracts and semantic comparison of unchanged idle row artwork."""
from pathlib import Path
import subprocess,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[2];folder=root/'IRSpeedyVPN/Components/ServerListControl'
w='{http://schemas.microsoft.com/winfx/2006/xaml/presentation}';x='{http://schemas.microsoft.com/winfx/2006/xaml}';l='{clr-namespace:IRSpeedyVPN.Components.ServerListControl}'
xml=ET.parse(folder/'ServerCountryPicker.xaml').getroot()
old=ET.fromstring(subprocess.check_output(['git','show','823c842:IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml'],cwd=root))
def row(tree):return next(e for e in tree.iter(w+'DataTemplate') if e.get(x+'Key')=='ServerRowContent')
a=row(xml);b=row(old)
for child in list(a[0]):
 if child.tag==w+'Grid.Style':a[0].remove(child)
for element in a.iter():
 if element.get(l+'ServerCometPart.Part') in ['Selection','Flag']:
  style=element.find(w+'Grid.Style')
  assert style is not None and any(t.get('Value')=='{x:Null}' for t in style.iter(w+'DataTrigger'))
  assert any(t.get('Property')=='Visibility' and t.get('Value')=='Hidden' for t in style.iter(w+'Setter'))
  element.remove(style)
 element.attrib.pop(l+'ServerCometPart.Part',None);element.attrib.pop('DataContext',None)
def canonical(e):return e.tag,sorted(e.attrib.items()),(e.text or '').strip(),[canonical(c) for c in e]
assert canonical(a)==canonical(b),'idle row artwork/layout/spacing/font/color/RTL changed'
print('PASS idle row artwork matches 1.4.20 including restored checking flag sheen')
text=(folder/'ServerCountryPicker.xaml').read_text()
assert text.count('<local:ServerListCometOverlay ')==1
assert all(name not in text for name in ['LightSweepRowTransition','SmartMosaicShift','SmartCardShine'])
assert not (folder/'LightSweepRowTransition.cs').exists() and not (folder/'LightSweepAnimationPlan.cs').exists()
print('PASS one paired overlay host; old reorder/crossfade and smart-card light paths remain removed')
parts=[e.get(l+'ServerCometPart.Part') for e in row(ET.parse(folder/'ServerCountryPicker.xaml').getroot()).iter() if e.get(l+'ServerCometPart.Part')]
assert sorted(parts)==sorted(['Name','Flag','Signal','Selection','Badge'])
c=(folder/'ServerListSweepAnimator.cs').read_text();frame=c[c.index('        private void Rendering('):c.index('        private void Reveal(')]
assert all(v not in frame for v in ['OrderBy','Sort(','Task.','await ','Invoke(','File.','UrlTest','GetServerUrls','BitmapImage','Thread.','lock ('])
assert c.count('CompositionTarget.Rendering +=')==1 and 'CompositionTarget.Rendering -=' in c and 'Stopwatch' in c
print('PASS measured existing groups and one elapsed-time callback without backend/ranking/I/O')
m=(root/'IRSpeedyVPN/UserControls/UCServerList.ServerResultsRefresh.cs').read_text()
assert m.index('countryPicker.BeginCheckCycle()')<m.index('manualRefreshTask = RefreshServerResultsAsync')
assert m.index('countryPicker.EndCheckCycleSetup();')<m.index('var plan = new ServerRefreshPlan')
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
