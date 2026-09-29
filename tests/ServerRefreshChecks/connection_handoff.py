"""Compile real connection handlers to check cleanup/scan handoff and latest-click ordering."""
from pathlib import Path
import subprocess, sys, tempfile
root = Path(__file__).resolve().parents[2]
s = (root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
methods = s[s.index('        private async void UCUserInfo_OnDisconnectRequest('):s.index('        void RegiserVpnService()')]
prefix = r'''
using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
interface IVPNService { void Disconnect();void Connect(string protocol);bool IsRequirementAvailable(); }
class TunnelPlusService : IVPNService {
 public ManualResetEventSlim DisconnectRelease=new ManualResetEventSlim(true);
 public volatile bool DisconnectEntered;public int Connects;
 public void CancelPendingConnection(){}public bool IsRequirementAvailable()=>true;
 public void Disconnect(){DisconnectEntered=true;DisconnectRelease.Wait();}
 public void Connect(string p){Connects++;}
}
class UCServerList {
 public int PausesForConnection,Drains;public List<bool> Resumes=new List<bool>();
 public void PauseServerChecks(){}public void PauseServerChecksForConnection(){PausesForConnection++;}
 public Task DrainServerChecksAsync(){Drains++;return Task.CompletedTask;}
 public void ResumeServerChecksAfterCleanup(bool restartRound=false){Resumes.Add(restartRound);}
}
class Info { public IVPNService CurrentService; }
class Proxy { public void Detach(){} }
class Resource { public void ExtractResource(bool b){} }
class LogHelper { public static void WriteLog(Exception e){throw e;} }
class Program {
 bool isUpdateAvailable,IsUserLogin=true;long connectionRequestVersion;int pendingConnectionRequests;
 SemaphoreSlim connectionRequestGate=new SemaphoreSlim(1,1);
 Info gInfo=new Info();UCServerList uCServerList=new UCServerList();Proxy proxifier=new Proxy();Resource localResource=new Resource();
 void UnRegiserVpnService(){}void RegiserVpnService(){}void HideLoading(){}void ShowLoading(string s,bool b){}void ShowControl(object c){}void ShowMessage(string m){}
'''
tests = r'''
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static async Task Until(Func<bool> ready){for(int i=0;i<1000&&!ready();i++)await Task.Delay(2);Check(ready(),"async handoff finished");}
 static async Task Main(){
 var p=new Program();var old=new TunnelPlusService();p.gInfo.CurrentService=old;old.DisconnectRelease.Reset();
 p.UCUserInfo_OnDisconnectRequest(null,EventArgs.Empty);await Until(()=>old.DisconnectEntered);
 Check(p.uCServerList.Resumes.Count==0,"disconnect waits for old VPN cleanup before restarting tests");
 old.DisconnectRelease.Set();await Until(()=>p.uCServerList.Resumes.Count==1);
 Check(p.uCServerList.Resumes[0] && p.gInfo.CurrentService==null,"explicit disconnect requests a fresh round after cleanup");
 var failure=new Program();await failure.ApplyConnectionRequestAsync(null,null,0);
 Check(failure.uCServerList.Resumes.Count==1 && !failure.uCServerList.Resumes[0],"automatic failure cleanup does not request a fresh round");
 var race=new Program();var prior=new TunnelPlusService();var next=new TunnelPlusService();race.gInfo.CurrentService=prior;prior.DisconnectRelease.Reset();
 race.UCUserInfo_OnDisconnectRequest(null,EventArgs.Empty);await Until(()=>prior.DisconnectEntered);
 race.UCServerList_OnConnectRequest(race.uCServerList,next,null,null);
 Check(race.uCServerList.PausesForConnection==1 && next.Connects==0,"connect stops tests immediately even while old cleanup is pending");
 prior.DisconnectRelease.Set();await Until(()=>next.Connects==1 && race.pendingConnectionRequests==0);
 Check(race.uCServerList.Resumes.Count==0 && ReferenceEquals(race.gInfo.CurrentService,next),"a newer connect suppresses obsolete disconnect scan restart");
 race.UCUserInfo_OnDisconnectRequest(null,EventArgs.Empty);await Until(()=>race.uCServerList.Resumes.Count==1);
 Check(race.uCServerList.Resumes[0],"the next disconnect starts exactly one new round");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='scan-handoff-') as d:
 p=Path(d);(p/'Program.cs').write_text(prefix+methods+tests)
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>CS0649</NoWarn></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
