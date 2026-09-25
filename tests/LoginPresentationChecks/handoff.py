"""Execute the production login handoff with lightweight UI stand-ins."""
from pathlib import Path
import subprocess, sys, tempfile
root=Path(__file__).resolve().parents[2]
s=(root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
methods=s[s.index('        private bool loginPresentationActive;'):s.index('        private async void RunLoginWithPresentation')]
harness=r'''
using System;using System.Threading.Tasks;using System.Collections.Generic;
namespace System.Windows.Threading { enum DispatcherPriority { ContextIdle } }
namespace Services.Hotspot { static class DirectSharingProbe { public static int Count;public static void BeginLoginCheck(){Count++;} } }
class UiDispatcher {
 public Queue<Action> Queue=new Queue<Action>();
 public Task InvokeAsync(Action action,System.Windows.Threading.DispatcherPriority p){action();return Task.CompletedTask;}
 public void BeginInvoke(System.Windows.Threading.DispatcherPriority p,Action action){Queue.Enqueue(action);}
 public void Drain(){while(Queue.Count>0)Queue.Dequeue()();}
}
class Loader { public bool Held;public void HoldCompletedFrame(){Held=true;} }
class ListView { public bool Paused;public int Starts,Loads;
 public void PrepareServerChecksForLogin(){Paused=false;}
 public void PauseServerChecks(){Paused=true;}
 public void ResumeServerChecksAfterCleanup(){Paused=false;Starts++;}
}
class ContentHost { public object Content; }
class Program {
 bool IsUserLogin=true;
 Loader uCLoginLoading=new Loader(); ListView uCServerList=new ListView();
 ContentHost TransitionBox=new ContentHost(); UiDispatcher Dispatcher=new UiDispatcher();
 void ShowControl(object ctrl){loginServerListPending=false;TransitionBox.Content=ctrl;if(ctrl==uCServerList){uCServerList.Loads++;if(!uCServerList.Paused)uCServerList.Starts++;}}
'''
tests=r'''
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static async Task Main(){
 var p=new Program();p.loginPresentationActive=true;p.ShowLoginServerList();
 Check(p.loginServerListPending&&p.uCServerList.Loads==0,"successful login queues list without loading it during ticks");
 Check(Services.Hotspot.DirectSharingProbe.Count==0,"hardware check stays out of login animation");
 await p.PrepareLoginResultAsync();
 Check(p.uCLoginLoading.Held&&p.uCServerList.Loads==1&&p.uCServerList.Starts==0,"completed frame holds while list loads with probes paused");
 p.loginPresentationActive=false;p.StartPostLoginChecks();
 Check(p.uCServerList.Starts==0,"checks wait until reveal dispatcher work finishes");p.Dispatcher.Drain();
 Check(p.uCServerList.Starts==1&&Services.Hotspot.DirectSharingProbe.Count==1,"checks start once after reveal");
 p.StartPostLoginChecks();p.Dispatcher.Drain();Check(p.uCServerList.Starts==1,"duplicate completion does not restart checks");
 var failed=new Program();failed.loginPresentationActive=true;failed.ShowLoginServerList();failed.ShowControl(new object());await failed.PrepareLoginResultAsync();
 Check(failed.uCServerList.Loads==0,"error or update navigation cancels pending server page");
 var logout=new Program();logout.loginPresentationActive=true;logout.ShowLoginServerList();await logout.PrepareLoginResultAsync();logout.loginPresentationActive=false;logout.StartPostLoginChecks();logout.IsUserLogin=false;logout.Dispatcher.Drain();
 Check(logout.uCServerList.Starts==0,"logout invalidates deferred checks");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='login-handoff-') as d:
 p=Path(d);(p/'Program.cs').write_text(harness+methods+tests)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
