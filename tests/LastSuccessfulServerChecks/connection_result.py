"""Check the production success callback; failed/stale results never pin a row."""
from pathlib import Path
import subprocess,sys,tempfile
root=Path(__file__).resolve().parents[2]
s=(root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
method=s[s.index('        private System.Diagnostics.Stopwatch connectingPresentationTime;'):s.index('        void ShowControl(object ctrl)')]
harness=r'''
using System;using System.Threading;using System.Threading.Tasks;using System.Diagnostics;
class Info {public object CurrentService=new object();public DateTime ConnectionTime;}
class ListView {public int Marks;public object Recorded;public void MarkSuccessfulConnection(object service){Marks++;Recorded=service;}}
class Dispatch {public bool HasShutdownStarted;}
static class VpnTimeZone { public static object Owner;public static void BeginConnection(object service){Owner=service;}public static void EndConnection(){Owner=null;} }
class Window {
 public Info gInfo=new Info();public ListView uCServerList=new ListView();object uCUserInfo=new object();
 public Dispatch Dispatcher=new Dispatch();public long connectionRequestVersion;public bool IsUserLogin=true;
 void HideLoading(){}void ShowMessage(string message){}void ShowControl(object view){}void UnRegiserVpnService(){}
 Task ApplyConnectionRequestAsync(object service,object protocol,long version)=>Task.CompletedTask;
 public void Result(bool success)=>ProcessConnectionResult(success,"fixture");
 public void StartDelay(){connectingPresentationTime=Stopwatch.StartNew();}
'''
checks=r'''
}
class Program {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static async Task Main(){
  var success=new Window();success.Result(true);
  Check(success.uCServerList.Marks==1&&ReferenceEquals(success.uCServerList.Recorded,success.gInfo.CurrentService),"successful presentation records the actual connected service");
  Check(ReferenceEquals(VpnTimeZone.Owner,success.gInfo.CurrentService),"clock activation belongs to the presented VPN connection");
  success.Result(false);
  Check(VpnTimeZone.Owner==null,"disconnect removes clock activation before cleanup");
  Check(success.uCServerList.Marks==1,"failed or disconnected result retains the previous successful marker");
  var cancelled=new Window();cancelled.StartDelay();cancelled.Result(true);cancelled.connectionRequestVersion++;
  var replaced=new Window();replaced.StartDelay();replaced.Result(true);replaced.gInfo.CurrentService=new object();
  var superseded=new Window();superseded.StartDelay();superseded.Result(true);superseded.Result(false);
  await Task.Delay(4200);
  Check(cancelled.uCServerList.Marks==0,"cancelled connection cannot pin a delayed success");
  Check(replaced.uCServerList.Marks==0,"replaced service cannot pin a stale success");
  Check(superseded.uCServerList.Marks==0,"a newer failure wins over delayed success");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='last-successful-callback-') as directory:
 p=Path(directory);(p/'Program.cs').write_text(harness+method+checks)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>CS0649</NoWarn></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',directory,'-v:q'],check=True)
