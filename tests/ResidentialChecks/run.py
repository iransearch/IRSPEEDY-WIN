"""Offline model execution + actual WPF/.NET 4.8 API compile + XAML contracts.

Usage: run.py DOTNET CSC NET8_REFS NET48_REFS
No live subscription, credentials, network or installed WPF desktop is used.
"""
from pathlib import Path
import json, subprocess, sys, tempfile, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[2]
dotnet, csc, refs8, refs48 = sys.argv[1:]
base = root / 'IRSpeedyVPN'
models = [base/'Residential/ResidentialModels.cs', base/'Residential/ResidentialSubscription.cs', base/'Common/Json/JsonConvertibleAttribute.cs', base/'Common/Json/JsonPropertyAttribute.cs']
def compile_to(output, refs, sources):
    subprocess.run([dotnet,csc,'-nologo','-langversion:7.3','-nowarn:0649,0169,0067','-target:'+('exe' if output.name in ['checks.dll','state.dll'] else 'library'),'-out:'+str(output)]
        + ['-r:'+str(p) for p in Path(refs).glob('*.dll') if p.name not in ['System.EnterpriseServices.Wrapper.dll','System.EnterpriseServices.Thunk.dll']] + [str(p) for p in sources], check=True)
with tempfile.TemporaryDirectory(prefix='residential-checks-') as tmp:
    tmp=Path(tmp)
    compile_to(tmp/'checks.dll', refs8, models+[Path(__file__).with_name('ModelChecks.cs')])
    (tmp/'checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([dotnet,str(tmp/'checks.dll')],check=True)
    picker_source=(base/'Components/ServerListControl/ServerCountryPicker.xaml.cs').read_text()
    group=picker_source[picker_source.index('    internal class GroupItem'):picker_source.index('    #endregion',picker_source.index('    internal class GroupItem'))]
    sig=picker_source[picker_source.index('    internal static class Sig'):picker_source.index('    #endregion',picker_source.index('    internal static class Sig'))]
    (tmp/'GroupItem.cs').write_text('using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Windows.Input;using System.Windows.Media;using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Models.NewService;namespace IRSpeedyVPN.Components.ServerListControl{'+sig+group+'}')
    compile_to(tmp/'state.dll', refs8, models+[base/'Residential/ResidentialViewModel.cs',base/'Residential/ResidentialService.cs',Path(__file__).with_name('CompileBoundary.cs'),Path(__file__).with_name('StateBoundary.cs'),Path(__file__).with_name('StateChecks.cs'),Path(__file__).with_name('HttpChecks.cs'),tmp/'GroupItem.cs'])
    (tmp/'state.runtimeconfig.json').write_text((tmp/'checks.runtimeconfig.json').read_text())
    subprocess.run([dotnet,str(tmp/'state.dll')],check=True)
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
assert 'IsFastest' in picker and 'Row_KeyDown' in picker
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
assert 'ShowControl(service is IRSpeedyVPN.Residential.ResidentialService ? (object)uCServerList : uCUserInfo);' in main
print('PASS tabs, active connection identity, async picker ownership, shared row parts, keyboard/accessibility and existing 4-second loading wiring')

