"""Exercise production async proxy snapshots with blocked network enumeration."""
from pathlib import Path
import subprocess, tempfile, sys
root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/Windows/ShareVPNSetting.xaml.cs').read_text()
methods = source[source.index('        private bool ProxyListening()'):source.index('        private async void btnStartStop_Click')]
harness = r'''
using System; using System.Linq; using System.Net; using System.Threading; using System.Threading.Tasks;
using System.Collections.Concurrent;
enum Visibility { Visible, Collapsed }
class Color { public static object FromRgb(int r,int g,int b)=>null; }
class SolidColorBrush { public SolidColorBrush(object c){} }
class Brushes { public static object Gray; }
class Control { public bool IsChecked,IsEnabled; public Visibility Visibility; public double Opacity; public string Text; public object Foreground; }
class ServiceInfo { public bool IsShareActive=true; public int? HttpPort=8080,SocksPort=1080; }
class IPGlobalProperties {
 public static IPGlobalProperties GetIPGlobalProperties()=>new IPGlobalProperties();
 public IPEndPoint[] GetActiveTcpListeners()=>new[]{new IPEndPoint(IPAddress.Any,8080)};
}
class NetworkInformationException:Exception{}
class UiContext:SynchronizationContext {
 readonly BlockingCollection<Action> queue=new BlockingCollection<Action>();
 public override void Post(SendOrPostCallback d,object state)=>queue.Add(()=>d(state));
 public void Run(Func<Task> action){SetSynchronizationContext(this);var task=action();while(!task.IsCompleted){if(queue.TryTake(out var next,100))next();}task.GetAwaiter().GetResult();}
}
class Program {
 bool IsLoaded=true,proxyBusy,proxyRefreshPending,proxyListenerActive,hotspotBusy;
 int proxyNetworkVersion,proxySnapshotPort; object proxySnapshotService; string proxyIp,proxyError;
 ServiceInfo Service=new ServiceInfo(); bool ProxyAvailable=true;
 Control btnStartStop=new Control(),pnlShowIP=new Control(),ProxyMotion=new Control(),ProxyOffHint=new Control(),ProxyStepTwo=new Control(),HTTPAddress=new Control(),SOCKS5Address=new Control(),ProxyStatus=new Control();
 static int uiThread,queries; static ManualResetEventSlim entered=new ManualResetEventSlim(),release=new ManualResetEventSlim();
 static string GetInternetInterfaceIp(){if(Thread.CurrentThread.ManagedThreadId==uiThread)throw new Exception("network ran on UI");Interlocked.Increment(ref queries);entered.Set();if(!release.Wait(5000))throw new Exception("test timed out");return "192.168.1.2";}
'''
tests = r'''
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static async Task Until(Func<bool> predicate){for(int i=0;i<1000&&!predicate();i++)await Task.Delay(2);if(!predicate())throw new Exception("timeout");}
 static void Main(){new UiContext().Run(Run);}
 static async Task Run(){
 uiThread=Thread.CurrentThread.ManagedThreadId;
 var p=new Program();p.RefreshProxyUi();await Until(()=>entered.IsSet);
 Check(p.proxyRefreshPending,"UI continues while network query is blocked");
 p.RefreshProxyUi();Check(queries==1,"timer ticks coalesce overlapping queries");
 p.proxyNetworkVersion++; // toggle happened while old snapshot was pending
 release.Set();await Until(()=>!p.proxyRefreshPending);
 Check(p.HTTPAddress.Text==null&&!p.proxyListenerActive,"discard stale snapshot after toggle");
 p.RefreshProxyUi();await Until(()=>!p.proxyRefreshPending);
 Check(p.ProxyListening()&&p.HTTPAddress.Text=="192.168.1.2 : 8080","fresh snapshot updates UI after worker completion");
 entered.Reset();release.Reset();p.RefreshProxyUi();await Until(()=>entered.IsSet);
 p.Service=new ServiceInfo();Check(!p.ProxyListening(),"cached copy and QR actions reject a different VPN session");p.HTTPAddress.Text="new session";release.Set();await Until(()=>!p.proxyRefreshPending);
 Check(p.HTTPAddress.Text=="new session","old VPN snapshot cannot update replacement service");
 p.ProxyAvailable=false;p.RefreshProxyUi();Check(!p.ProxyListening()&&p.HTTPAddress.Text=="","disconnect immediately clears cached addresses");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='sharing-ui-') as d:
 p=Path(d);(p/'Program.cs').write_text(harness+methods+tests)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>0649</NoWarn></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
