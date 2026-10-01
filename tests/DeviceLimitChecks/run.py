"""Run production device/removal handlers with UI and network doubles, without WPF or credentials.

This checks request lifecycle, not rendering. tests/ThemeChecks/run-wpf.ps1
checks the real Windows UI against an unobfuscated application build.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
window = root / 'IRSpeedyVPN/Windows/DeviceLimitWindow.xaml.cs'
main = (root / 'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
handlers = main[main.index('        private async void DeviceLimitWindow_OnRemoveRequested'):main.index('        SettingInfo GetSetting()')]
xaml = ET.parse(root / 'IRSpeedyVPN/Windows/DeviceLimitWindow.xaml')
name_key = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
parents = {child: parent for parent in xaml.iter() for child in parent}
fields = []
initial = []
for node in xaml.iter():
    name = node.get(name_key)
    ancestor = parents.get(node)
    while ancestor is not None:
        if ancestor.tag.split('}')[-1] in ('ControlTemplate', 'DataTemplate'):
            name = None  # Template names live in their own WPF namescope.
            break
        ancestor = parents.get(ancestor)
    if name:
        kind = node.tag.split('}')[-1]
        fields.append('public ' + kind + ' ' + name + ' = new ' + kind + '();')
        if node.get('Visibility') == 'Collapsed':
            initial.append(name + '.Visibility = Visibility.Collapsed;')

harness = r'''
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Windows;
using System.Windows;
using System.Windows.Controls;
namespace System.Windows {
 public enum Visibility { Visible, Collapsed }
 public class RoutedEventArgs : EventArgs { }
 public class Window { public bool IsVisible=true; public event EventHandler Closed;
  public void Close(){IsVisible=false;Closed?.Invoke(this,EventArgs.Empty);} }
}
namespace System.Windows.Controls {
 public class Control { public bool IsEnabled=true; public Visibility Visibility; }
 public class TextBlock : Control { public string Text; }
 public class Border : Control { }
 public class Button : Control { public object DataContext; }
 public class ItemsControl : Control { public IEnumerable ItemsSource; }
}
namespace System.Windows.Input {
 public enum Key { Escape, System, F4 }
 [Flags] public enum ModifierKeys { None=0, Alt=1 }
 public static class Keyboard { public static ModifierKeys Modifiers; }
 public class KeyEventArgs : EventArgs { public Key Key; public Key SystemKey; public bool Handled; }
}
namespace IRSpeedyVPN.Windows {
 public partial class DeviceLimitWindow {
 // XAML names/initial visibility are generated above, not duplicated by hand.
 FIELDS
 private void InitializeComponent(){INITIAL}
 }
}
class Body { public string message; }
class Response { public System.Net.HttpStatusCode StatusCode; public Body ResponseData; }
class Controller {
 public TaskCompletionSource<Response> Reply = new TaskCompletionSource<Response>();
 public volatile bool Started; public string Username,Password,Token; public int ThreadId;
 public Response RemoveToken(string u,string p,string n,string t){Username=u;Password=p;Token=t;ThreadId=Thread.CurrentThread.ManagedThreadId;Started=true;return Reply.Task.GetAwaiter().GetResult();}
}
static class LogHelper { public static void WriteLog(Exception error){} }
namespace IRSpeedyVPN {
 class MainWindow {
  public DeviceLimitWindow deviceLimitWindow;
  public string lastLoginUsername="original-user",lastLoginPassword="original-password";
  public bool IsRememberChecked=true; public Button uCLogin=new Button();
  public Controller serviceController=new Controller();
  public int LoginRetries; public string RetryUser;
  void ShowMessage(string message){}
  void RunAsync(Action action){action();}
  bool Login(string u,string p,bool remember){RetryUser=u;LoginRetries++;return true;}
  public void Bind(DeviceLimitWindow w){deviceLimitWindow=w;uCLogin.IsEnabled=false;w.OnRemoveRequested+=DeviceLimitWindow_OnRemoveRequested;w.Closed+=DeviceLimitWindow_Closed;}
 HANDLERS
 }
}
class Program {
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Click(DeviceLimitWindow w,string method,object source){typeof(DeviceLimitWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{source,new RoutedEventArgs()});}
 static DeviceInfo Device(string name,string token){return new DeviceInfo{device_name=name,device_token=token};}
 static void Select(DeviceLimitWindow w,DeviceInfo d){Click(w,"RemoveDevice_Click",new Button{DataContext=d});}
 static void Confirm(DeviceLimitWindow w){Click(w,"ConfirmRemoval_Click",w);}
 static System.Windows.Input.KeyEventArgs Key(DeviceLimitWindow w,System.Windows.Input.Key key,System.Windows.Input.Key system=System.Windows.Input.Key.Escape){
  var e=new System.Windows.Input.KeyEventArgs{Key=key,SystemKey=system};
  typeof(DeviceLimitWindow).GetMethod("Window_PreviewKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{w,e});return e;
 }
 static async Task Wait(Func<bool> predicate){for(int i=0;i<300&&!predicate();i++)await Task.Delay(10);Check(predicate(),"asynchronous request completed");}
 static void Main(){Run().GetAwaiter().GetResult();}
 static async Task Run(){
  var first=Device("Windows-PC","private-token-1");var second=Device("Samsung A54","private-token-2");
  var w=new DeviceLimitWindow();w.SetDevices(new[]{first,null,second},null);
  Check(w.txtDeviceCount.Text=="۲ دستگاه"&&!w.txtMessage.Text.Contains("private-token"),"actual device count uses Persian digits and no tokens are displayed");
  int requests=0;DeviceInfo requested=null;w.OnRemoveRequested+=(s,d)=>{requests++;requested=d;};
  Select(w,first);Check(requests==0&&w.ConfirmationBox.Visibility==Visibility.Visible,"selection requires confirmation before sending a request");
  Click(w,"CancelRemoval_Click",w);Confirm(w);Check(requests==0,"cancelled selection cannot be submitted with Enter");
  Select(w,second);Confirm(w);Confirm(w);Select(w,first);Click(w,"Close_Click",w);
  Check(requests==1&&ReferenceEquals(requested,second)&&w.IsVisible&&w.IsRemovalInProgress,"pending removal blocks duplicate requests, changed selection and close");
  System.Windows.Input.Keyboard.Modifiers=System.Windows.Input.ModifierKeys.Alt;
  Check(Key(w,System.Windows.Input.Key.Escape).Handled&&Key(w,System.Windows.Input.Key.System,System.Windows.Input.Key.F4).Handled&&w.IsVisible,"Escape and Alt-F4 cannot dismiss an in-flight removal");
  System.Windows.Input.Keyboard.Modifiers=System.Windows.Input.ModifierKeys.None;
  w.SetDevices(new[]{first},"late response");Check(w.txtDeviceCount.Text=="۲ دستگاه","late login response cannot replace the in-flight list");
  w.ShowError("network error");Check(w.IsVisible&&!w.IsRemovalInProgress&&w.txtError.Visibility==Visibility.Visible&&w.btnConfirm.IsEnabled,"failed removal keeps the error visible and allows retry");
  Confirm(w);Check(requests==2&&ReferenceEquals(requested,second),"retry retains the exact selected device token");
  w.CompleteRemoval();Check(!w.IsVisible&&!w.IsRemovalInProgress,"confirmed successful removal closes the window");
  var empty=new DeviceLimitWindow();empty.SetDevices(null,"");Check(empty.txtEmpty.Visibility==Visibility.Visible&&empty.txtDeviceCount.Text=="۰ دستگاه","empty server list remains usable");
  var keyboard=new DeviceLimitWindow();keyboard.SetDevices(new[]{first},null);Select(keyboard,first);Key(keyboard,System.Windows.Input.Key.Escape);
  Check(keyboard.IsVisible&&keyboard.ConfirmationBox.Visibility==Visibility.Collapsed,"Escape cancels selection before dismissing the dialog");
  Key(keyboard,System.Windows.Input.Key.Escape);Check(!keyboard.IsVisible,"Escape dismisses an idle dialog");
  var unbound=new DeviceLimitWindow();unbound.SetDevices(new[]{first},null);Select(unbound,first);Confirm(unbound);
  Check(unbound.IsVisible&&!unbound.IsRemovalInProgress&&unbound.txtError.Visibility==Visibility.Visible,"missing request handler reports an error without trapping the dialog");

  var host=new IRSpeedyVPN.MainWindow();var win=new DeviceLimitWindow();host.Bind(win);win.SetDevices(new[]{first},null);
  int caller=Thread.CurrentThread.ManagedThreadId;Select(win,first);Confirm(win);await Wait(()=>host.serviceController.Started);
  Check(win.IsVisible&&host.LoginRetries==0&&host.serviceController.ThreadId!=caller,"network removal runs off the calling thread without early close or login");
  host.lastLoginUsername="later-user";host.lastLoginPassword="later-password";
  host.serviceController.Reply.SetResult(new Response{StatusCode=System.Net.HttpStatusCode.OK});await Wait(()=>host.LoginRetries==1);
  Check(host.RetryUser=="original-user"&&host.serviceController.Username=="original-user"&&host.uCLogin.IsEnabled,"success retries only the originating login and unlocks its controls");

  host=new IRSpeedyVPN.MainWindow();win=new DeviceLimitWindow();host.Bind(win);win.SetDevices(new[]{first},null);Select(win,first);Confirm(win);
  host.serviceController.Reply.SetResult(new Response{StatusCode=System.Net.HttpStatusCode.ServiceUnavailable,ResponseData=new Body{message="server refused"}});
  await Wait(()=>!win.IsRemovalInProgress);
  Check(win.IsVisible&&win.txtError.Text=="server refused"&&host.LoginRetries==0,"server failure is displayed in the originating window without retrying login");

  host=new IRSpeedyVPN.MainWindow();win=new DeviceLimitWindow();host.Bind(win);win.SetDevices(new[]{first},null);Select(win,first);Confirm(win);
  host.serviceController.Reply.SetException(new Exception("network unavailable"));await Wait(()=>!win.IsRemovalInProgress);
  Check(win.IsVisible&&win.txtError.Visibility==Visibility.Visible&&host.LoginRetries==0,"network exception leaves a retryable dialog without triggering login");

  host=new IRSpeedyVPN.MainWindow();win=new DeviceLimitWindow();host.Bind(win);win.SetDevices(new[]{first},null);Select(win,first);Confirm(win);await Wait(()=>host.serviceController.Started);
  win.Close();var replacement=new DeviceLimitWindow();replacement.SetDevices(new[]{second},null);host.Bind(replacement);
  host.serviceController.Reply.SetResult(new Response{StatusCode=System.Net.HttpStatusCode.OK});await Task.Delay(100);
  Check(replacement.IsVisible&&host.LoginRetries==0,"stale successful response cannot close a newer window or log in after dismissal");
 }
}
'''
harness = harness.replace('FIELDS', '\n'.join(fields)).replace('INITIAL', ''.join(initial)).replace('HANDLERS', handlers)
with tempfile.TemporaryDirectory(prefix='device-limit-checks-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(harness)
    (path / 'DeviceLimitWindow.cs').write_text(window.read_text())
    (path / 'DeviceInfo.cs').write_text((root / 'IRSpeedyVPN/Models/NewService/DeviceInfo.cs').read_text())
    (path / 'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'run', '--project', directory, '-v:q'], check=True)
