from pathlib import Path
import subprocess, tempfile, sys
root=Path(__file__).resolve().parents[2]
s=(root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
methods=s[s.index('        internal static bool ExitCleanupStarted'):s.index('        public void mainTimerCallback')]
harness=r'''
using System;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;
namespace System.Windows { class Application { public static Application Current=new Application();public TaskCompletionSource<bool> Done=new TaskCompletionSource<bool>();public void Shutdown(){Done.TrySetResult(true);} } }
interface IVPNService { void Disconnect(); }
class TunnelPlusService : IVPNService { public static bool Exiting,Killed;public bool Cancelled;public int Stops;public bool Stall;
 public static void BeginApplicationExit(){Exiting=true;} public void CancelForApplicationExit(){Cancelled=true;}
 public static void StopOwnedProcessesForExit(){Killed=true;}
 public void Disconnect(){Interlocked.Increment(ref Stops);if(Stall)new ManualResetEventSlim().Wait();}
}
namespace Services.Hotspot { static class DirectHotspot { public static Counter Controller=new Counter();public static void StopPollingForExit(){} } }
class Counter { public int Calls;public void Stop(){Interlocked.Increment(ref Calls);}public void Detach(){Interlocked.Increment(ref Calls);} }
static class SystemProxy { public static bool Disabled;public static void Disable(){Disabled=true;} }
static class LogHelper { public static void WriteExLog(string text){Console.WriteLine(text);} }
class ServiceFactory { public List<IVPNService> Services=new List<IVPNService>(); }
class Info { public IVPNService CurrentService; }
class View { public void PauseServerChecks(){} }
class Notify { public bool Visible;public void Dispose(){} }
class Harness {
 bool IsEnabled=true,IsUserLogin=true;long connectionRequestVersion;
 Timer mainTimer,sessionMaintenanceTimer;View uCServerList=new View();
 ServiceFactory serviceFactory=new ServiceFactory();Info gInfo=new Info();Counter proxifier=new Counter();Notify notify=new Notify();
 void UnRegiserVpnService(){}
'''
tests=r'''
 static async Task Main(){
 var h=new Harness();var stale=new TunnelPlusService{Stall=true};var active=new TunnelPlusService();h.serviceFactory.Services.Add(stale);h.gInfo.CurrentService=active;
 var clock=System.Diagnostics.Stopwatch.StartNew();h.Notify_Exit(null,EventArgs.Empty);h.Notify_Exit(null,EventArgs.Empty);
 if(clock.ElapsedMilliseconds>500)throw new Exception("Exit blocked its caller");
 await Task.Delay(100);if(!SystemProxy.Disabled||h.proxifier.Calls!=1||!active.Cancelled||!stale.Cancelled)throw new Exception("independent cleanup/cancellation missing");
 if(await Task.WhenAny(System.Windows.Application.Current.Done.Task,Task.Delay(11000))!=System.Windows.Application.Current.Done.Task)throw new Exception("Exit never completed");
 if(!TunnelPlusService.Killed||!h.exitCleanupFinished||active.Stops!=1||stale.Stops!=1)throw new Exception("ownership cleanup/current service/reentry error");
 Console.WriteLine("PASS blocked service cannot prevent bounded Exit; caller free, proxy cleanup independent, exact current service included, duplicate Exit ignored elapsedMs="+clock.ElapsedMilliseconds);
 }
}
'''
with tempfile.TemporaryDirectory(prefix='exit-lifecycle-') as d:
 p=Path(d);(p/'Program.cs').write_text(harness+methods+tests)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>0649</NoWarn></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
