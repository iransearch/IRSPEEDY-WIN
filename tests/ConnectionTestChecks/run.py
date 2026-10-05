"""Exercise the production connection-test lifecycle with UI/network doubles.

Run with Python and a .NET 8 SDK. WPF rendering still requires Windows.
"""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
harness = r'''
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Windows;
namespace System.Windows {
 public enum Visibility { Visible, Collapsed }
 public enum WindowState { Normal, Minimized }
 public class Window {
  public static Window Shared = new Window();
  public event EventHandler StateChanged;
  public WindowState WindowState;
  public static Window GetWindow(object control) { return Shared; }
  public void Change(WindowState state) { WindowState = state; StateChanged?.Invoke(this, EventArgs.Empty); }
 }
}
namespace System.Windows.Input { public class MouseButtonEventArgs : EventArgs {} }
namespace System.Windows.Media.Animation {
 public class Storyboard {
  public static int Starts, Stops;
  public bool Running = true;
  public Storyboard() { Interlocked.Increment(ref Starts); }
  public void Remove(object owner) { if(Running) Interlocked.Increment(ref Stops); Running=false; }
 }
}
namespace IRSpeedyVPN.Services {
 public class TunnelPlusService : UserControls.Service {
  public static int SnapshotRequests;
  public void RequestConnectionState(string reason) { if(reason=="connection-test-failed") Interlocked.Increment(ref SnapshotRequests); }
 }
}
namespace IRSpeedyVPN.Common {
 public static class LogHelper { public static void WriteLog(Exception error) {} }
 public static class ConnectionTlsTest {
  public static ManualResetEventSlim Gate = new ManualResetEventSlim(false);
  public static int Calls, Workers, WrongPorts, ExpectedPort;
  public static int CallerThread, UiThreadCalls;
  public static bool AllFail;
  public static Task<bool> CheckAsync(string host, int timeout, int? port, CancellationToken token) {
   Interlocked.Increment(ref Workers);
   try {
    if(Thread.CurrentThread.ManagedThreadId == CallerThread) Interlocked.Increment(ref UiThreadCalls);
    if(port != ExpectedPort || timeout != 8000) Interlocked.Increment(ref WrongPorts);
    Interlocked.Increment(ref Calls);
    Gate.Wait();
    return Task.FromResult(!AllFail&&!host.Contains("youtube"));
   } finally { Interlocked.Decrement(ref Workers); }
  }
  public static void Reset(int port) {
   Gate = new ManualResetEventSlim(false); Calls=Workers=WrongPorts=UiThreadCalls=0; ExpectedPort=port; AllFail=false;
   CallerThread=Thread.CurrentThread.ManagedThreadId;
  }
 }
}
namespace IRSpeedyVPN.Windows {
 public class PingResult {
  public static int Shown; public static PingResult Last;
  public bool GoogleConfirmed, YoutubeConfirmed, InstagramConfirmed, TelegramConfirmed; public Window Owner;
  public void ShowDialog() { Last=this; Interlocked.Increment(ref Shown); }
 }
}
namespace IRSpeedyVPN.UserControls {
 public class Control {
  public bool IsEnabled=true; public Visibility Visibility=Visibility.Visible;
  public bool IsVisible => Visibility==Visibility.Visible;
 }
 public class Service { public int? HttpPort; }
 public class Info { public Service CurrentService; public DateTime ConnectionTime=DateTime.Now; }
 public partial class UCUserInfo {
  private Info globalInfo=new Info();
  public bool IsLoaded=true, IsVisible=true;
  public Control TestConnectionMenu=new Control(), ConnectionTestVisual=new Control(), ConnectionTestStatus=new Control(), ConnectedCheckBadge=new Control();
  private Storyboard StartMotion(string key) { return new Storyboard(); }
  public UCUserInfo(int port) { globalInfo.CurrentService=new Services.TunnelPlusService{HttpPort=port}; SetConnectionTestPending(false); }
  public void Start() { ConnectionTest_PreviewMouseDown(this,null); }
  public void Cancel() { CancelConnectionTest(); }
  public void ReplaceService() { globalInfo.CurrentService=new Service{HttpPort=9876}; }
  public void ReplaceSession() { globalInfo.ConnectionTime=globalInfo.ConnectionTime.AddSeconds(1); }
  public bool Pending => connectionTestRequest!=null;
 }
}
class Program {
 static void Check(bool ok,string label) { if(!ok) throw new Exception(label); Console.WriteLine("PASS "+label); }
 static async Task Wait(Func<bool> ready) { for(int i=0;i<300&&!ready();i++) await Task.Delay(10); if(!ready()) throw new Exception("request timed out in harness"); }
 static void Main() { Run().GetAwaiter().GetResult(); }
 static async Task Run() {
  ConnectionTlsTest.Reset(1177); var view=new UCUserInfo(1177); view.Start(); view.Start();
  await Wait(()=>ConnectionTlsTest.Calls>0);
  Check(view.Pending&&!view.TestConnectionMenu.IsEnabled&&view.ConnectionTestVisual.IsVisible&&view.ConnectionTestStatus.IsVisible,"pending test shows local effect and blocks repeat clicks");
  Check(view.ConnectedCheckBadge.Visibility==Visibility.Collapsed&&ConnectionTlsTest.UiThreadCalls==0,"network runs off the initiating thread and leaves the page responsive");
  int starts=Storyboard.Starts; Window.Shared.Change(WindowState.Minimized);
  Check(Storyboard.Stops>0,"minimizing stops the testing animation");
  Window.Shared.Change(WindowState.Normal); Check(Storyboard.Starts==starts+1,"restoring resumes the active testing animation");
  view.ReplaceService(); ConnectionTlsTest.Gate.Set(); await Wait(()=>!view.Pending);
  Check(PingResult.Shown==0&&view.TestConnectionMenu.IsEnabled&&!view.ConnectionTestVisual.IsVisible,"changed service suppresses stale result and clears busy state");
  Check(ConnectionTlsTest.WrongPorts==0,"all probes use the captured connection port and TCP/TLS timeout");

  ConnectionTlsTest.Reset(2233); view=new UCUserInfo(2233); view.Start(); await Wait(()=>ConnectionTlsTest.Calls>0);
  view.Cancel(); Check(!view.Pending&&!view.ConnectionTestStatus.IsVisible&&view.TestConnectionMenu.IsEnabled,"disconnect or navigation cancels the presentation immediately");
  ConnectionTlsTest.Gate.Set(); await Wait(()=>ConnectionTlsTest.Workers==0); await Task.Delay(100);
  Check(PingResult.Shown==0,"cancelled worker cannot display a late result");

  ConnectionTlsTest.Reset(3344); view=new UCUserInfo(3344); view.Start(); await Wait(()=>ConnectionTlsTest.Calls>0);
  view.ReplaceSession(); ConnectionTlsTest.Gate.Set(); await Wait(()=>!view.Pending);
  Check(PingResult.Shown==0,"new connection to the same service invalidates the old test");

  ConnectionTlsTest.Reset(4455); view=new UCUserInfo(4455); view.Start(); await Wait(()=>ConnectionTlsTest.Calls>0);
  ConnectionTlsTest.Gate.Set(); await Wait(()=>!view.Pending);
  Check(PingResult.Shown==1&&PingResult.Last.GoogleConfirmed&&!PingResult.Last.YoutubeConfirmed&&PingResult.Last.InstagramConfirmed&&PingResult.Last.Owner==Window.Shared,"completed test displays the recorded success and failure results once");
  starts=Storyboard.Starts; Window.Shared.Change(WindowState.Minimized); Window.Shared.Change(WindowState.Normal);
  Check(Storyboard.Starts==starts&&view.ConnectedCheckBadge.IsVisible,"completed test removes animation and restores the connected badge");
  Check(IRSpeedyVPN.Services.TunnelPlusService.SnapshotRequests==0,"partial success does not request failure diagnostics");
  ConnectionTlsTest.Reset(5566); ConnectionTlsTest.AllFail=true; view=new UCUserInfo(5566); view.Start();
  await Wait(()=>ConnectionTlsTest.Calls>0); ConnectionTlsTest.Gate.Set(); await Wait(()=>!view.Pending);
  Check(IRSpeedyVPN.Services.TunnelPlusService.SnapshotRequests==1&&PingResult.Shown==2,"all-failed manual test requests one snapshot and still displays results");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='connection-test-checks-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(harness)
    (path / 'ConnectionTest.cs').write_text((root / 'IRSpeedyVPN/UserControls/UCUserInfo.ConnectionTest.cs').read_text())
    (path / 'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet', 'run', '--project', directory, '-v:q'], check=True)
