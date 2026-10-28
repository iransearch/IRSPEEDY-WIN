"""Offline model execution + actual WPF/.NET 4.8 API compile + XAML contracts.

Usage: run.py DOTNET CSC NET8_REFS NET48_REFS
No live subscription, credentials, network or installed WPF desktop is used.
"""
from pathlib import Path
import json, shutil, subprocess, sys, tempfile, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[2]
dotnet, csc, refs8, refs48 = sys.argv[1:]
base = root / 'IRSpeedyVPN'
models = [base/'Residential/ResidentialModels.cs', base/'Residential/ResidentialSubscription.cs', base/'Common/Json/JsonConvertibleAttribute.cs', base/'Common/Json/JsonPropertyAttribute.cs']
def compile_to(output, refs, sources, extra_refs=()):
    subprocess.run([dotnet,csc,'-nologo','-langversion:7.3','-nowarn:0649,0169,0067','-target:'+('exe' if output.name in ['checks.dll','state.dll','startup.dll'] else 'library'),'-out:'+str(output)]
        + ['-r:'+str(p) for p in Path(refs).glob('*.dll') if p.name not in ['System.EnterpriseServices.Wrapper.dll','System.EnterpriseServices.Thunk.dll']] + ['-r:'+str(p) for p in extra_refs] + [str(p) for p in sources], check=True)
with tempfile.TemporaryDirectory(prefix='residential-checks-') as tmp:
    tmp=Path(tmp)
    compile_to(tmp/'checks.dll', refs8, models+[Path(__file__).with_name('ModelChecks.cs')])
    (tmp/'checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([dotnet,str(tmp/'checks.dll')],check=True)
    picker_source=(base/'Components/ServerListControl/ServerCountryPicker.xaml.cs').read_text()
    group=picker_source[picker_source.index('    internal class GroupItem'):picker_source.index('    #endregion',picker_source.index('    internal class GroupItem'))]
    sig=picker_source[picker_source.index('    internal static class Sig'):picker_source.index('    #endregion',picker_source.index('    internal static class Sig'))]
    (tmp/'GroupItem.cs').write_text('using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Windows.Input;using System.Windows.Media;using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Models.NewService;namespace IRSpeedyVPN.Components.ServerListControl{'+sig+group+'}')
    status_source=(base/'UserControls/UCUserInfo.xaml.cs').read_text()
    status_methods=status_source[status_source.index('        private void RefreshServerDetail()'):status_source.index('        private void UserControl_IsVisibleChanged(')]
    (tmp/'StatusDetailProbe.cs').write_text('using System;using System.Collections.Generic;using System.Linq;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Models.NewService;class StatusDetailProbe{internal IRSpeedyVPN.Models.GlobalInfo globalInfo=new IRSpeedyVPN.Models.GlobalInfo();internal class Label{internal string Text;}internal Label txtServerDetail=new Label();private static string PersianDigits(string text)=>text;internal void Refresh()=>RefreshServerDetail();'+status_methods+'}')
    selection_source=(base/'UserControls/UCServerList.Residential.cs').read_text()
    start=selection_source.index('        private void TrySelectResidentialFastest()')
    selection_method=selection_source[start:selection_source.index('\n        }',start)+10]
    health_start=selection_source.index('        private bool ResidentialSelectionHasSuccess()')
    health_method=selection_source[health_start:selection_source.index(';',health_start)+1]
    (tmp/'DefaultSelectionProbe.cs').write_text('using System.Linq;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Residential;class DefaultSelectionProbe{readonly ResidentialViewModel residential;bool isResidentialTab=true,connectionConnecting,connectionConnected;internal IVPNService selectedService;IVPNService residentialSelection;class Picker{internal IVPNService SelectedService;internal System.Collections.Generic.HashSet<int> Checking=new System.Collections.Generic.HashSet<int>();internal bool IsServiceChecking(IVPNService service)=>Checking.Contains(service.ID);}readonly Picker residentialCountryPicker=new Picker();internal DefaultSelectionProbe(ResidentialViewModel vm){residential=vm;}void UpdateResidentialVisuals(){}internal void SetChecking(IVPNService service,bool value){if(value)residentialCountryPicker.Checking.Add(service.ID);else residentialCountryPicker.Checking.Remove(service.ID);}internal void Choose()=>TrySelectResidentialFastest();'+health_method+'internal bool CanConnect()=>ResidentialSelectionHasSuccess();'+selection_method+'}')
    start=picker_source.index('        private void Row_KeyDown(')
    handlers=picker_source[start:picker_source.index('        #endregion',start)]
    (tmp/'RowInputProbe.cs').write_text('using System;using System.Linq;using System.Collections.Generic;using System.Windows;using System.Windows.Input;using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Components.ServerListControl;class RowInputProbe{readonly List<GroupItem> _allGroups=new List<GroupItem>();internal IVPNService Selected;internal int Changes;internal RowInputProbe(GroupItem group,IVPNService selected){_allGroups.Add(group);Selected=selected;}bool PrepareCountryPool(GroupItem g,IVPNService s)=>g.IsSelectable;void Apply(IVPNService service,GroupItem group,SelectionKind kind){Selected=service;Changes++;}Action<IVPNService> ServerSelected;internal void Click(IVPNService service){Row_Click(new FrameworkElement{DataContext=new ServerDisplaySlot{DisplayedServer=new ServerItem{Identity=LastSuccessfulServer.Key(service)}}},new RoutedEventArgs());}internal void PressKey(IVPNService service,Key key){Row_KeyDown(new FrameworkElement{DataContext=new ServerDisplaySlot{DisplayedServer=new ServerItem{Identity=LastSuccessfulServer.Key(service)}}},new KeyEventArgs{Key=key});}'+handlers+'}')
    newtonsoft=Path(csc).resolve().parents[2]/'Newtonsoft.Json.dll'
    shutil.copy2(newtonsoft,tmp/'Newtonsoft.Json.dll')
    compile_to(tmp/'state.dll', refs8, models+[base/'Residential/ResidentialViewModel.cs',base/'Residential/ResidentialService.cs',base/'Services/ServerCheckCache.cs',Path(__file__).with_name('CompileBoundary.cs'),Path(__file__).with_name('StateBoundary.cs'),Path(__file__).with_name('StateChecks.cs'),Path(__file__).with_name('CacheChecks.cs'),Path(__file__).with_name('HttpChecks.cs'),tmp/'GroupItem.cs',tmp/'StatusDetailProbe.cs',tmp/'DefaultSelectionProbe.cs',tmp/'RowInputProbe.cs'],[newtonsoft])
    (tmp/'state.runtimeconfig.json').write_text((tmp/'checks.runtimeconfig.json').read_text())
    subprocess.run([dotnet,str(tmp/'state.dll')],check=True)
    recovery=(base/'Services/TunnelPlusService.NetworkRecovery.cs').read_text()
    start=recovery.index('        private async Task RecheckResidentialStartupPoolAsync(')
    method=recovery[start:recovery.index('        private Task<bool> QueuePoolAvailability(',start)]
    (tmp/'StartupRecoveryWorker.cs').write_text('using System;using System.Linq;using System.Threading;using System.Threading.Tasks;using IRSpeedyVPN.Services.Libcore;namespace IRSpeedyVPN.Services{internal partial class TunnelPlusService{'+method+'}}')
    startup_sources=[Path(__file__).with_name('StartupRecoveryChecks.cs'),tmp/'StartupRecoveryWorker.cs',Path(__file__).with_name('StartupRecoveryDtos.cs')]
    compile_to(tmp/'startup.dll',refs8,startup_sources)
    (tmp/'startup.runtimeconfig.json').write_text((tmp/'checks.runtimeconfig.json').read_text())
    subprocess.run([dotnet,str(tmp/'startup.dll')],check=True)
    compile_to(tmp/'startup-net48.dll',refs48,startup_sources)
    assert 'Task.Run(() => RecheckResidentialStartupPoolAsync(generation, startupToken))' in recovery
    assert 'if (this is Residential.ResidentialService && pooled)' in recovery
    print('PASS production Residential startup recovery worker compiles against .NET Framework 4.8')
    compile_to(tmp/'residential-net48.dll', refs48, models + [base/'Residential/ResidentialTrafficBar.cs',base/'Residential/ResidentialViewModel.cs',base/'Residential/ResidentialService.cs',Path(__file__).with_name('CompileBoundary.cs')])
    print('PASS Residential production C# against real .NET Framework 4.8 + WPF reference assemblies')
    ui_sources = list((base/'Residential').glob('*.cs')) + [p for p in (base/'Components/ServerListControl').glob('*.cs') if p.name!='CountryPickerSelector.cs'] + list((base/'UserControls').glob('UCServerList*.cs'))
    compile_to(tmp/'residential-ui-net48.dll', refs48, ui_sources + [base/'Common/Json/JsonConvertibleAttribute.cs',base/'Common/Json/JsonPropertyAttribute.cs',base/'Models/HeaderIconRegistration.cs',Path(__file__).with_name('UiCompileBoundary.cs')])
    print('PASS production picker, light sweep, traffic control, both UCServerList flows and async refresh compile against .NET 4.8/WPF')
for name in ['UserControls/UCServerList.xaml','Components/ServerListControl/ServerCountryPicker.xaml']:
    ET.parse(base/name)
print('PASS modified XAML is well-formed')
picker=(base/'Components/ServerListControl/ServerCountryPicker.xaml').read_text()
xml=ET.fromstring(picker)
parts=[v for e in xml.iter() for k,v in e.attrib.items() if k.endswith('ServerCometPart.Part')]
assert sorted(parts)==['0','1','2','3','4']
assert 'DisplayedServer.RowHeight' in picker and 'DisplayedServer.AutomationDescription' in picker
assert 'IsFastest' not in picker and 'سریع‌ترین' not in picker and 'Row_KeyDown' in picker
isp=next(e for e in xml.iter() if 'DisplayIsp' in e.attrib.get('Text',''))
assert isp.attrib['FlowDirection']=='LeftToRight' and isp.attrib['TextTrimming']=='CharacterEllipsis'
prefix=next(e for e in xml.iter() if any('LocationCity,' in child.attrib.get('Text','') for child in e))
separator=next(e for e in prefix if 'LocationSeparator' in e.attrib.get('Text',''))
assert separator.attrib['Foreground']=='#8793B3'
location=next(e for e in xml.iter() if 'LocationLine' in e.attrib.get('AutomationProperties.Name',''))
assert location.attrib['FlowDirection']=='LeftToRight' and location.attrib['HorizontalAlignment']=='Right'
assert [e.attrib['Width'] for e in location.find('{http://schemas.microsoft.com/winfx/2006/xaml/presentation}Grid.ColumnDefinitions')]==['Auto','*']
assert prefix.attrib['FlowDirection']=='LeftToRight' and 'TextTrimming' not in prefix.attrib and 'MaxWidth' not in prefix.attrib
assert prefix.attrib['Grid.Column']=='0' and isp.attrib['Grid.Column']=='1'
assert prefix.attrib['TextWrapping']==isp.attrib['TextWrapping']=='NoWrap'
assert 'Text=" · "' not in picker
print('PASS contiguous right-aligned LTR LocationLine: full city in Auto, only ISP ellipsizes, no unconditional separator')

assert 'More locations are coming soon.' in picker and 'Signal_Retry' not in picker and 'PreviewMouseLeftButtonDown' not in picker
uc=(base/'UserControls/UCServerList.xaml').read_text()
assert 'ShowSmartCard="False"' in uc and 'ServiceTab_KeyDown' in uc
for color in ['#1C2331','#2D3850','#3A5BC0','#2F4A9C','#A3ADC7']: assert color in uc
controller=(base/'UserControls/UCServerList.Residential.cs').read_text()
assert 'await DrainServerChecksAsync()' in controller and 'Disconnect()' not in controller
assert 'residentialConnected = connected && service is ResidentialService' in controller
for source in ['UCServerList.xaml.cs','UCServerList.ServerResultsRefresh.cs']:
    assert 'var picker = countryPicker;' in (base/'UserControls'/source).read_text()
main=(base/'MainWindow.xaml.cs').read_text()
assert '4000L - presentation.ElapsedMilliseconds' in main
assert 'uCServerList.ConfigureResidential(acc);' in main and main.count('uCServerList.ConfigureResidential(null);')>=2
fast=(base/'MainWindow.FastStartup.cs').read_text()
assert 'uCServerList.OnDisconnectRequest += UCUserInfo_OnDisconnectRequest;' in fast
assert 'uCServerList.ConfigureResidential(account);' in fast
cached=fast[fast.index('private async Task<bool> TryApplyCachedAccountAsync('):fast.index('private async Task<bool> TryApplyCachedAccountAsync(')+6000]
assert cached.index('serviceFactory.RenewServiceList(account.groups)') < cached.index('uCServerList.ConfigureResidential(account);') < cached.index('ShowLoginServerList();')
assert 'account.residential_vpn == null && account.UserAccount.residential_vpn == null' in cached
assert 'ValidateSessionAndRefreshServerList();' in cached and '_ = Task.Run(' in cached
print('PASS remembered-account startup configures Residential before showing the chooser; old cache refresh uses the gated API off the startup path')
success=main[main.index('uCServerList.MarkSuccessfulConnection(service);'):main.index('uCServerList.MarkSuccessfulConnection(service);')+200]
assert 'ShowControl(uCUserInfo);' in success and 'ResidentialService' not in success
assert 'TabKeyboardFocus' in uc and 'TargetName="TabFocus"' not in uc
assert 'Value="0.55"' in uc and 'TargetName="ConnectSurface" Property="Effect" Value="{x:Null}"' in uc
assert 'initializedProbeScopes.Contains(context)' in (base/'UserControls/UCServerList.xaml.cs').read_text()
traffic=(base/'Residential/ResidentialTrafficBar.cs').read_text()
# The border and vertical padding must leave the full 44-DIP ring viewport.
assert 'Padding = new Thickness(14, 9, 14, 9)' in traffic and 64-2-9*2==44
assert 'Margin = new Thickness(0.5)' in traffic
assert 'new Point(22, 3)' in traffic and 'new Size(19, 19)' in traffic
assert 22-19-5/2>=0 and 22+19+5/2<=44
signal=next(e for e in xml.iter() if e.attrib.get('DataContext')=='{Binding SignalServer, Mode=OneWay}')
assert signal.attrib['VerticalAlignment']=='Center' and not list(signal.iter('{http://schemas.microsoft.com/winfx/2006/xaml/presentation}TranslateTransform'))
print('PASS full traffic-ring viewport and stroke margins; Residential ping shares the flag center without a vertical offset')
assert 'Grid.SetColumn(ring, 2)' in traffic and 'Grid.SetColumn(badge, 0)' in traffic and 'percentage.Text' in traffic
assert 'ReferenceEquals(TransitionBox.Content, uCServerList) ? ""' in main
assert 'RetryRequested' not in controller and 'residentialChecksConsumed' not in controller
assert 'HasSuccessfulTest' not in (base/'Components/ServerListControl/ServerListViewModel.cs').read_text()
print('PASS no fastest row model/badge or row retry; shared success gate, refresh and disabled Connect')
print('PASS tabs, active connection identity, async picker ownership, shared row parts, keyboard/accessibility and existing 4-second loading wiring')

