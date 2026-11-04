"""Offline execution of production settings handlers with isolated UI/store boundaries.

Usage: run.py <dotnet> <csc.dll> <net8-ref-dir> [net48-ref-dir]
The optional compile uses real WPF references; neither run renders Windows UI.
"""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
dotnet, csc, refs = sys.argv[1:4]
windows = root / 'IRSpeedyVPN/Windows'
xname = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
hub = ET.parse(windows / 'SettingsHub.xaml').getroot()
methods = ET.parse(windows / 'VGAURDServiceSetting.xaml').getroot()
assert not any(n.get(xname) in ('GameMode', 'GameRow') for n in hub.iter())
card = next(n for n in methods.iter() if n.get(xname) == 'rowSmartDns')
assert any(n.get('Text') == 'DNS هوشمند' for n in card.iter())
toggle = next(n for n in card.iter() if n.get(xname) == 'GameMode')
assert toggle.get('AutomationProperties.Name') == 'DNS هوشمند'
parents = {child: parent for parent in methods.iter() for child in parent}
node = toggle
while node in parents:
    assert node.get('Visibility') not in ('Hidden', 'Collapsed')
    node = parents[node]
print('PASS Smart DNS is visible and accessible only in connection methods')

boundary = r'''
using System;
using System.Collections.Generic;
#if !REAL_WPF
namespace System.Windows {
 public enum Visibility { Visible, Collapsed }
 public class RoutedEventArgs : EventArgs { }
 public class UIElement { public bool IsEnabled=true; public double Opacity=1; public Visibility Visibility; }
 public class Window : UIElement {
  public Window Owner; public bool ClosedCalled;
  public event EventHandler Loaded, Closed;
  public static Action<Window> DialogAction;
  public void ShowDialog() { DialogAction?.Invoke(this); }
  public void Close() { ClosedCalled=true; Closed?.Invoke(this,EventArgs.Empty); }
 }
 public static class MessageBox { public static void Show(Window owner,string message,string title) { } }
}
namespace System.Windows.Input { public class MouseButtonEventArgs : EventArgs { } }
namespace System.Windows.Threading {
 public class DispatcherTimer { public TimeSpan Interval; public event EventHandler Tick;
  public void Start() {} public void Stop() {} }
}
namespace System.Windows.Controls {
 public class TextBlock : System.Windows.UIElement { public string Text; public static object ForegroundProperty;
  public void SetResourceReference(object property,string key) {} }
}
#endif
namespace IRSpeedyVPN.Controls {
#if REAL_WPF
 public class SettingsToggleSwitch : System.Windows.Controls.Primitives.ToggleButton {
  public bool IsOn { get { return IsChecked==true; } set { IsChecked=value; } }
 }
#else
 public class SettingsToggleSwitch : System.Windows.UIElement {
  bool? checkedValue; public Action Changed;
  public bool? IsChecked { get { return checkedValue; } set { if(checkedValue==value)return; checkedValue=value; Changed?.Invoke(); } }
  public bool IsOn { get { return IsChecked==true; } set { IsChecked=value; } }
 }
#endif
}
namespace IRSpeedyVPN.Interfaces { public interface IVPNService { Type SettingType {get;} } }
namespace IRSpeedyVPN.Resource {
 public static class RegHelper {
  public static Dictionary<string,string> Values=new Dictionary<string,string>();
  public static int Writes;
  public static string GetSettingValue(string key) { string v; return Values.TryGetValue(key,out v)?v:null; }
  public static void SetSettingValue(string key,string value) { Values[key]=value; Writes++; }
 }
}
namespace IRSpeedyVPN.Services.Xray {
 public static class SmartIpRouting { public static bool Enabled;
  public static bool IsEnabled() { return Enabled; } public static void SetEnabled(bool on) { Enabled=on; } }
}
namespace IRSpeedyVPN.Services.SplitTunneling {
 public class SplitTunnelSettings { public bool Enabled; }
 public static class SplitTunnelStore {
  public static bool Enabled, Fail; public static int Saves;
  public static SplitTunnelSettings Load() { if(Fail)throw new Exception("synthetic store error"); return new SplitTunnelSettings {Enabled=Enabled}; }
  public static void Save(SplitTunnelSettings s) { Enabled=s.Enabled; Saves++; }
 }
}
namespace IRSpeedyVPN.Common { public static class WindowDrag {
 public static void Begin(object w,System.Windows.Input.MouseButtonEventArgs e) {} } }
namespace IRSpeedyVPN {
 public static class AppServices { public static Info GlobalInfo;
  public class Info { public IRSpeedyVPN.Interfaces.IVPNService CurrentService; } }
}
namespace IRSpeedyVPN.Windows {
 using System.Windows;
 using IRSpeedyVPN.Controls;
 public class SSRServiceSetting : Window { }
 public class SpeedyShieldSetting : Window { }
 public class ShareVPNSetting : Window { public object Service; }
 public class SettingsSplitTunnelApps : Window { }
 public class SettingsPassword : Window {
  public Func<string,string,System.Threading.Tasks.Task<string>> ChangePasswordAsync;
  public Action<string> PasswordChangeAccepted;
 }
 public partial class SettingsHub {
  public UIElement ShareButton=new UIElement(), MethodsButton=new UIElement(), ShieldButton=new UIElement(),
   PasswordButton=new UIElement(), SaveButton=new UIElement(), AiRow=new UIElement();
  public SettingsToggleSwitch AiService=new SettingsToggleSwitch();
  private void InitializeComponent() { }
 }
 public partial class VGAURDServiceSetting {
  public UIElement rowVpn=new UIElement(), rowSmartDns=new UIElement();
  public SettingsToggleSwitch VPNMode=new SettingsToggleSwitch(), GlobalProxy=new SettingsToggleSwitch(),
   SystemProxy=new SettingsToggleSwitch(), TelegramRouteProxy=new SettingsToggleSwitch(),
   AiService=new SettingsToggleSwitch(), GameMode=new SettingsToggleSwitch();
  public System.Windows.Controls.TextBlock SplitTunnelStatus=new System.Windows.Controls.TextBlock();
  private void InitializeComponent() {
#if !REAL_WPF
   GameMode.Changed=()=> { if(GameMode.IsOn)GameMode_Checked(null,null); else GameMode_Unchecked(null,null); };
   VPNMode.Changed=()=> { if(VPNMode.IsOn)VPNMode_Checked(null,null); };
   GlobalProxy.Changed=()=> { if(GlobalProxy.IsOn)GlobalProxy_Checked(null,null); };
   SystemProxy.Changed=()=> { if(SystemProxy.IsOn)SystemProxy_Checked(null,null); };
   TelegramRouteProxy.Changed=()=> { if(TelegramRouteProxy.IsOn)TelegramRouteProxy_Checked(null,null); };
#endif
  }
 }
}
'''
checks = r'''
using System;
using System.Reflection;
using IRSpeedyVPN.Windows;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services.SplitTunneling;
class Checks {
 static int count;
 static void Check(bool ok,string name) { if(!ok)throw new Exception(name); count++; }
 static object Call(object page,string method) { return page.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(page,method=="SavePreferences"?null:new object[]{null,null}); }
 static void Reset() { RegHelper.Values.Clear(); RegHelper.Writes=0; SplitTunnelStore.Enabled=false;
  SplitTunnelStore.Fail=false; SplitTunnelStore.Saves=0; IRSpeedyVPN.Services.Xray.SmartIpRouting.Enabled=false; }
 static void Main() {
  Reset(); RegHelper.Values["VGAURDGameMode"]="1"; RegHelper.Values["VGAURDVPNMode"]="0";
  RegHelper.Values["VGAURDGlobalProxy"]="1";
  var page=new VGAURDServiceSetting();
  Check(page.GameMode.IsOn && page.VPNMode.IsOn,"old enabled preference restored and forces TUN");
  Check(!page.GlobalProxy.IsOn && !page.VPNMode.IsEnabled && !page.SystemProxy.IsEnabled,"other methods locked");
  page.GameMode.IsOn=false;
  Check(page.VPNMode.IsEnabled && page.GlobalProxy.IsEnabled && page.SystemProxy.IsEnabled,"disabling unlocks methods");
  page.SystemProxy.IsOn=true; Call(page,"btnOK_Click");
  Check(RegHelper.GetSettingValue("VGAURDGameMode")=="0" && RegHelper.GetSettingValue("VGAURDSystemProxy")=="1","disable and alternate mode saved");
  Reset(); SplitTunnelStore.Enabled=true;
  page=new VGAURDServiceSetting(); page.GameMode.IsOn=true;
  Check(SplitTunnelStore.Enabled && RegHelper.Writes==0,"editing does not persist");
  Call(page,"Close_Click");
  Check(SplitTunnelStore.Enabled && RegHelper.Writes==0,"cancel preserves settings");
  page=new VGAURDServiceSetting(); page.GameMode.IsOn=true; Call(page,"btnOK_Click");
  Check(!SplitTunnelStore.Enabled && SplitTunnelStore.Saves==1,"save disables conflicting split tunnel");
  Check(RegHelper.GetSettingValue("VGAURDGameMode")=="1" && RegHelper.GetSettingValue("VGAURDVPNMode")=="1","existing keys saved");
  foreach(var key in new[]{"VGAURDGlobalProxy","VGAURDSystemProxy","ProxifierTelegramRoute"})
   Check(RegHelper.GetSettingValue(key)=="0","proxy off: "+key);
  Check(page.ClosedCalled,"successful save closes");
  var hub=new SettingsHub(); hub.AiService.IsOn=true; Call(hub,"SavePreferences");
  Check(RegHelper.GetSettingValue("VGAURDGameMode")=="1","parent cannot overwrite child DNS preference");
  Check(IRSpeedyVPN.Services.Xray.SmartIpRouting.Enabled,"AI remains independent");
  Reset(); page=new VGAURDServiceSetting(); page.GameMode.IsOn=true; SplitTunnelStore.Fail=true;
  Call(page,"btnOK_Click");
  Check(RegHelper.Writes==0 && !page.ClosedCalled,"store failure aborts before any mode write");
  Reset(); page=new VGAURDServiceSetting(); page.GameMode.IsOn=true; page.CanSavePreferences=()=>false;
  Call(page,"btnOK_Click");
  Check(RegHelper.Writes==0 && !page.ClosedCalled,"busy guard blocks save");
  Reset(); SplitTunnelStore.Enabled=true; page=new VGAURDServiceSetting(); Call(page,"btnOK_Click");
  Check(SplitTunnelStore.Enabled && SplitTunnelStore.Saves==0,"DNS off preserves split tunnel");
  Reset(); hub=new SettingsHub { IsConnected=()=>true }; hub.AiService.IsOn=true;
  Check(!(bool)Call(hub,"SavePreferences") && !IRSpeedyVPN.Services.Xray.SmartIpRouting.Enabled,"connected parent guard retained");
  Console.WriteLine("PASS "+count+" production settings handler checks (isolated UI/store boundaries)");
 }
}
'''
with tempfile.TemporaryDirectory() as directory:
    tmp = Path(directory)
    (tmp / 'Boundary.cs').write_text(boundary)
    (tmp / 'Checks.cs').write_text(checks)
    production = [windows / 'SettingsHub.xaml.cs', windows / 'VGAURDServiceSetting.xaml.cs']
    def compile(output, references, sources, *options):
        subprocess.run([dotnet, csc, '-nologo', '-langversion:7.3', '-nowarn:0067,0649',
                        '-out:' + str(output), *options]
                       + ['-r:' + str(p) for p in Path(references).glob('*.dll')
                          if p.name not in ('System.EnterpriseServices.Thunk.dll', 'System.EnterpriseServices.Wrapper.dll')]
                       + [str(p) for p in sources], check=True)
    compile(tmp / 'checks.dll', refs, production + [tmp / 'Boundary.cs', tmp / 'Checks.cs'], '-target:exe')
    (tmp / 'checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    subprocess.run([dotnet, str(tmp / 'checks.dll')], check=True)
    if len(sys.argv) > 4:
        compile(tmp / 'settings-net48.dll', sys.argv[4], production + [tmp / 'Boundary.cs'],
                '-target:library', '-define:REAL_WPF')
        print('PASS production settings code-behind compiles against .NET 4.8/WPF references (stub service and generated-field boundaries)')
