"""Offline model execution + actual WPF/.NET 4.8 API compile + XAML contracts.

Usage: run.py DOTNET CSC NET8_REFS NET48_REFS
No live subscription, credentials, network or installed WPF desktop is used.
"""
from pathlib import Path
import json, re, shutil, subprocess, sys, tempfile, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[2]
dotnet, csc, refs8, refs48 = sys.argv[1:]
base = root / 'IRSpeedyVPN'
models = [base/'Residential/ResidentialModels.cs', base/'Residential/ResidentialSubscription.cs', base/'Common/Json/JsonConvertibleAttribute.cs', base/'Common/Json/JsonPropertyAttribute.cs']
def compile_to(output, refs, sources, extra_refs=()):
    subprocess.run([dotnet,csc,'-nologo','-langversion:7.3','-nowarn:0649,0169,0067','-target:'+('exe' if output.name in ['checks.dll','state.dll'] else 'library'),'-out:'+str(output)]
        + ['-r:'+str(p) for p in Path(refs).glob('*.dll') if p.name not in ['System.EnterpriseServices.Wrapper.dll','System.EnterpriseServices.Thunk.dll']] + ['-r:'+str(p) for p in extra_refs] + [str(p) for p in sources], check=True)
with tempfile.TemporaryDirectory(prefix='residential-checks-') as tmp:
    tmp=Path(tmp)
    # Execute the production card's pure formatting/color decisions without a WPF desktop.
    traffic_source=(base/'Residential/ResidentialTrafficBar.cs').read_text()
    threshold=re.search(r'private const double LOW_TRAFFIC_THRESHOLD = [^;]+;',traffic_source).group()
    methods=[]
    for name in ['RemainingLabel','RemainingColor','RemainingPercentage']:
        start=traffic_source.index('        private static string '+name+'(')
        end=traffic_source.index('\n        private ',start+1)
        methods.append(traffic_source[start:end].replace('private static','internal static'))
    (tmp/'TrafficCardProbe.cs').write_text('using System;using System.Globalization;using IRSpeedyVPN.Residential;internal static class TrafficCardProbe{'+threshold+''.join(methods)+'}')
    tab_source=(base/'UserControls/UCServerList.Residential.cs').read_text()
    start=tab_source.index('        private void ServiceTab_KeyDown(')
    tab_handler=tab_source[start:tab_source.index('        private void UpdateResidentialVisuals()',start)]
    (tmp/'TabInputProbe.cs').write_text('''using System.Windows.Controls;using System.Windows.Input;
namespace System.Windows.Input {internal enum Key {Left,Right,Enter,Space,Tab}internal class KeyEventArgs {internal Key Key;internal bool Handled;}}
namespace System.Windows.Controls {internal class RadioButton {internal bool IsEnabled=true,Focused;internal bool? IsChecked;internal bool Focus(){Focused=true;return true;}}}
internal class TabInputProbe {internal readonly RadioButton vpnTab=new RadioButton(),residentialTab=new RadioButton();
internal bool Press(bool residential,Key key){var e=new KeyEventArgs{Key=key};ServiceTab_KeyDown(residential?residentialTab:vpnTab,e);return e.Handled;}
'''+tab_handler+'}')
    compile_to(tmp/'checks.dll', refs8, models+[Path(__file__).with_name('ModelChecks.cs'),tmp/'TrafficCardProbe.cs',tmp/'TabInputProbe.cs'])
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
    assert 'ResidentialService' not in recovery and 'residential-startup' not in recovery
    assert 'RecheckResidentialStartupPoolAsync' not in recovery
    print('PASS both tabs use the same recovery path without a Residential-only startup retry')
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
ns='{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
xns='{http://schemas.microsoft.com/winfx/2006/xaml}'
ucxml=ET.fromstring(uc)
tab_style=next(e for e in ucxml.iter(ns+'Style') if e.attrib.get(xns+'Key')=='ServiceTab')
setters={e.attrib['Property']:e.attrib.get('Value') for e in tab_style.findall(ns+'Setter')}
assert setters['Height']=='36' and setters['FontSize']=='14' and setters['FontWeight']=='Medium'
assert setters['Background']=='#1C2331' and setters['Foreground']=='#A3ADC7' and setters['BorderThickness']=='0'
assert not list(tab_style.iter(ns+'LinearGradientBrush')) and not list(tab_style.iter(ns+'DropShadowEffect'))
surface=next(e for e in tab_style.iter(ns+'Border') if e.attrib.get(xns+'Name')=='TabSurface')
assert surface.attrib['CornerRadius']=='8'
checked=next(e for e in tab_style.iter(ns+'Trigger') if e.attrib.get('Property')=='IsChecked')
assert checked.attrib['Value']=='True'
assert any(e.attrib.get('TargetName')=='TabSurface' and e.attrib.get('Value')=='#3A5BC0' for e in checked)
assert any(e.attrib.get('Property')=='FontWeight' and e.attrib.get('Value')=='SemiBold' for e in checked)
tabs=next(e for e in ucxml.iter(ns+'Grid') if any(c.attrib.get(xns+'Name')=='vpnTab' for c in e))
assert tabs.attrib['FlowDirection']=='LeftToRight' and tabs.attrib['Height']=='36'
assert tabs.attrib['KeyboardNavigation.TabNavigation']=='Local' and len(tabs.find(ns+'Grid.ColumnDefinitions'))==2
assert not tabs.findall(ns+'Border')
vpn_tab=next(e for e in tabs if e.attrib.get(xns+'Name')=='vpnTab')
res_tab=next(e for e in tabs if e.attrib.get(xns+'Name')=='residentialTab')
for tab,column,label in [(vpn_tab,'0','VPN'),(res_tab,'1','RESIDENTIAL')]:
    assert tab.attrib['Grid.Column']==column and tab.attrib['TabIndex']==column and tab.attrib['IsTabStop']=='True'
    assert tab.attrib['AutomationProperties.Name']==label and any(e.attrib.get('Text')==label for e in tab.iter(ns+'TextBlock'))
    assert tab.attrib['PreviewKeyDown']=='ServiceTab_KeyDown' and tab.attrib['GroupName']=='ServiceKind'
assert float(vpn_tab.attrib['Margin'].split(',')[2])+float(res_tab.attrib['Margin'].split(',')[0])==6
focus=next(e for e in ucxml.iter(ns+'Style') if e.attrib.get(xns+'Key')=='TabKeyboardFocus')
focus_border=next(focus.iter(ns+'Border'))
assert focus_border.attrib['BorderThickness']=='1.5' and focus_border.attrib['BorderBrush']=='#AAB4FF' and focus_border.attrib['Margin']=='-2'
assert setters['FocusVisualStyle']=='{StaticResource TabKeyboardFocus}'
print('PASS flat independent LTR tabs: fixed positions, six-DIP gap, native selected state, keyboard-only focus and Tab order')
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
assert 'BeginAccountSession(verified: false);' in cached
assert cached.index('ShowLoginServerList();') < cached.index('BeginAccountSession(verified: false);') < cached.index('StartSessionMaintenance();')
assert '_ = Task.Run(' not in cached[:cached.index('return true;')]
print('PASS remembered-account startup configures Residential before showing the chooser; one immediate scheduled account validation refreshes old metadata off the startup path')
success=main[main.index('uCServerList.MarkSuccessfulConnection(service);'):main.index('uCServerList.MarkSuccessfulConnection(service);')+200]
assert 'ShowControl(uCUserInfo);' in success and 'ResidentialService' not in success
assert 'TabKeyboardFocus' in uc and 'TargetName="TabFocus"' not in uc
assert 'Value="0.55"' in uc and 'TargetName="ConnectSurface" Property="Effect" Value="{x:Null}"' in uc
assert 'initializedProbeScopes.Contains(context)' in (base/'UserControls/UCServerList.xaml.cs').read_text()
traffic=(base/'Residential/ResidentialTrafficBar.cs').read_text()
# The reference's 44-DIP ring + 12-DIP vertical padding + border needs 70 DIPs.
assert 'CardHeight = 70' in traffic and 'MinHeight = 68' in traffic and 'ExpandedHeight = CardHeight + EntranceGap' in traffic
assert 'Padding = new Thickness(14, 12, 14, 12)' in traffic and 70-2-12*2>=44
assert 'RingSize = 44' in traffic and 'RingRadius = 19' in traffic and 'RingStroke = 5' in traffic
assert 44/2-19-5/2>0 and 44/2+19+5/2<44
assert 'Width = RingSize, Height = RingSize' in traffic and 'new Size(RingRadius, RingRadius)' in traffic
assert 'LOW_TRAFFIC_THRESHOLD = 0.15' in traffic and 'arc.Data = MakeArc(traffic.Ratio)' in traffic
assert 'RemainingLabel(traffic)' in traffic and 'usedValue.Text = traffic.UsedText' in traffic and 'totalValue.Text = traffic.TotalText' in traffic
assert 'new GridLength(1.3, GridUnitType.Star)' in traffic and traffic.count('stats.ColumnDefinitions.Add(')==3
assert all('Stat("'+label+'"' in traffic for label in ['LEFT','USED','TOTAL'])
assert 'new Thickness(0, 0, 14, 0)' in traffic and 'new Thickness(12, 0, 12, 0)' in traffic
assert 'new Thickness(1, 0, 0, 0)' in traffic and 'new Thickness(0, 0, 12, 0)' in traffic
assert 'Grid.SetColumn(ring, 0)' in traffic and 'Grid.SetColumn(stats, 1)' in traffic
assert 'ring.Children.Add(percentage)' in traffic and 'percentage = Text("", 11, Brushes.White)' in traffic
assert 'Stroke = Color("#1FFFFFFF")' in traffic and 'Stroke = Color("#F2C53D")' in traffic
assert 'Background = Color("#202838")' in traffic and 'CornerRadius = new CornerRadius(12)' in traffic
assert 'connectionDot = new Ellipse { Width = 7, Height = 7' in traffic and 'new Thickness(0, 0, 6, 0)' in traffic
assert 'connectionDot.Visibility = connected ? Visibility.Visible : Visibility.Collapsed' in traffic
assert 'Color(connected ? "#8C3FCF8E" : "#2D3850")' in traffic
assert 'percent of traffic left' in traffic and 'AutomationProperties.SetName(ring, description)' in traffic
assert '" left, " + traffic.UsedText + " used of " + traffic.TotalText' in traffic
assert not any(dead in traffic for dead in ['badge','usageTrack','usageFill','usedFraction','arcGlow','YellowGradient','CardBackground','DropShadowEffect','LinearGradientBrush','new Button','In use','infoText'])
assert not any('\u0600'<=c<='\u06ff' for c in traffic)
assert '650' not in traffic and 'TimeSpan.FromMilliseconds(250)' in traffic
assert 'Animation(-6, 0)' in traffic and 'SystemParameters.ClientAreaAnimation' in traffic and 'bool show = traffic != null && activeTab' in traffic
assert '250ms' in (root/'docs/windows-residential.md').read_text().replace('250 ms','250ms')
signal=next(e for e in xml.iter() if e.attrib.get('DataContext')=='{Binding SignalServer, Mode=OneWay}')
assert signal.attrib['VerticalAlignment']=='Center' and not list(signal.iter('{http://schemas.microsoft.com/winfx/2006/xaml/presentation}TranslateTransform'))
print('PASS single-row flat traffic card: left remaining ring, LEFT/USED/TOTAL, no badge/bar/refresh/glow, existing entrance lifecycle preserved')
assert 'ReferenceEquals(TransitionBox.Content, uCServerList) ? ""' in main
assert 'RetryRequested' not in controller and 'residentialChecksConsumed' not in controller
assert 'HasSuccessfulTest' not in (base/'Components/ServerListControl/ServerListViewModel.cs').read_text()
print('PASS no fastest row model/badge or row retry; shared success gate, refresh and disabled Connect')
print('PASS tabs, active connection identity, async picker ownership, shared row parts, keyboard/accessibility and existing 4-second loading wiring')

