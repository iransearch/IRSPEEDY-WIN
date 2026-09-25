"""Exercise production scheduler + persistent cache; fake network/UI, real file I/O."""
from pathlib import Path
import tempfile, subprocess, sys
root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
region = 'private readonly System.Windows.Threading.DispatcherTimer probeTimer =' + source.split('        private readonly System.Windows.Threading.DispatcherTimer probeTimer =',1)[1].split('        #endregion',1)[0]
stubs = r'''
using System; using System.Linq; using System.Collections.Generic; using System.Collections.Concurrent; using System.Threading; using System.Threading.Tasks; using System.IO;
using IRSpeedyVPN.Services; using IRSpeedyVPN.Interfaces; using IRSpeedyVPN.Models.NewService;
namespace System.Windows.Threading { class DispatcherTimer { public TimeSpan Interval {get;set;} public bool Running; public void Stop(){Running=false;} public void Start(){Running=true;} } }
namespace IRSpeedyVPN.Models { public enum VPNType { NORMAL,VOD,CHAIN } }
namespace IRSpeedyVPN.Interfaces { interface IVPNService { int ID {get;} string Name {get;} bool IsUrlTestSupported {get;} string CountryCode {get;} string Country {get;} List<Url> GetServerUrls(); void UrlTest(); } }
class TunnelPlusService : IVPNService {
 public int ID {get;set;} public string Name=>"xfast"; public string CountryCode {get;set;} public string Country=>CountryCode; public bool IsUrlTestSupported=>true;
 public List<Url> urls=new List<Url>(); public List<Url> GetServerUrls()=>urls;
 public static ConcurrentQueue<string> Calls=new ConcurrentQueue<string>(); public static volatile bool Hold,Fail; public static string HoldCountry;
 public static Action<TunnelPlusService,Action<long>,Func<bool>> Probe;
 public void UrlTest(){} public void UrlTestFull(object a,bool b,Action<long> progress,Func<bool> cancel){
 Calls.Enqueue(CountryCode+ID); Probe?.Invoke(this,progress,cancel);
 while((Hold || HoldCountry==CountryCode) && !cancel()) Thread.Sleep(1);
 if(!cancel()) foreach(var u in urls){u.latency=Fail?-1:100+ID;u.latencychkTime=DateTime.Now;}
 }
}
class Info { public object CurrentService; }
class FakeDispatcher {
 readonly SynchronizationContext context=SynchronizationContext.Current;
 readonly int thread=Thread.CurrentThread.ManagedThreadId;
 public bool HoldPosted; readonly ConcurrentQueue<Action> held=new ConcurrentQueue<Action>();
 public bool CheckAccess()=>Thread.CurrentThread.ManagedThreadId==thread;
 public void Invoke(Action a){if(!CheckAccess())throw new Exception("unexpected synchronous dispatcher call");a();}
 public void BeginInvoke(Action a){if(HoldPosted)held.Enqueue(a);else context.Post(_=>a(),null);}
 public void ReleasePosted(){HoldPosted=false;while(held.TryDequeue(out var a))context.Post(_=>a(),null);}
}
class UiContext : SynchronizationContext {
 readonly BlockingCollection<Action> queue=new BlockingCollection<Action>();
 public override void Post(SendOrPostCallback d,object state)=>queue.Add(()=>d(state));
 public void Run(Func<Task> action){SetSynchronizationContext(this);var task=action();while(!task.IsCompleted){if(queue.TryTake(out var next,100))next();}task.GetAwaiter().GetResult();}
}
class Picker {
 readonly int thread=Thread.CurrentThread.ManagedThreadId;
 public int Updates,Progresses; public long Displayed;
 public Action<IVPNService> OnRefresh;
 void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new Exception("picker updated off UI thread");}
 public void RefreshGroup(IVPNService s){CheckThread();Updates++;Displayed=s.GetServerUrls()[0].latency;OnRefresh?.Invoke(s);}
 public void ShowGroupProgress(IVPNService s,long latency){CheckThread();Progresses++;Displayed=latency;}
}
class LogHelper { public static void WriteLog(Exception e)=>throw e; }
class UrlTestCoordinator { public static volatile bool AbortRequested; public static void CancelAll()=>AbortRequested=true; public static void BeginBatch()=>AbortRequested=false; }
class Program {
 FakeDispatcher Dispatcher=new FakeDispatcher(); Picker countryPicker=new Picker(); Info globalInfo=new Info();
 bool IsVisible=true; CancellationTokenSource _urlTestCts; IVPNService[] _currentServices;
'''
tests = r'''
 static void Check(bool value,string name){if(!value)throw new Exception(name); Console.WriteLine("PASS "+name);}
 static TunnelPlusService S(string country,int id,string config=null){return new TunnelPlusService{ID=id,CountryCode=country,urls=new List<Url>{new Url{url=config??("vless://secret-"+id+"@host"),extra_field_1="config"}}};}
 async Task Settle(){await probeTask;for(int i=0;i<500 && probeRunning;i++)await Task.Delay(2);await Task.Delay(10);Check(!probeRunning,"worker settled");}
 static async Task Until(Func<bool> condition){for(int i=0;i<1000 && !condition();i++)await Task.Delay(2);Check(condition(),"asynchronous condition completed");}
 static void Main(){new UiContext().Run(MainAsync);}
 static async Task MainAsync(){
 string dir=Path.Combine(Path.GetTempPath(),"irspeedy-check-"+Guid.NewGuid());Directory.CreateDirectory(dir);
 try{
 // Drive the production scheduler while the fake RPC remains in flight. The
 // final URL value differs from progress so a late callback is observable.
 var live=new Program();var liveService=S("DE",21);live._currentServices=new IVPNService[]{liveService};
 live.probeCache=new ServerCheckCache("live","xfast",dir);live.probeCache.Bind(live._currentServices);
 TunnelPlusService.Hold=true;
 TunnelPlusService.Probe=(service,progress,cancel)=>{if(progress==null)throw new Exception("missing live progress callback");progress(750);};
 live.RunBackgroundUrlTests(live._currentServices);
 await Until(()=>live.countryPicker.Progresses==1);
 Check(live.probeRunning && live.countryPicker.Updates==0 && live.countryPicker.Displayed==750,"first success reaches UI while remaining server checks are blocked");
 Check(liveService.urls[0].latencychkTime==default(DateTime),"partial progress does not commit incomplete results to cache");
 TunnelPlusService.Hold=false;await live.Settle();
 Check(live.countryPicker.Displayed==121 && live.countryPicker.Updates==1,"final result replaces progress on UI thread before worker completion");
 // Exercise repeated timer cycles, failure and recovery on the SAME row objects.
 TunnelPlusService.Probe=null;
 for(int cycle=0;cycle<3;cycle++){
 TunnelPlusService.Fail=cycle==1;live.probeSchedule.Completed(DateTime.UtcNow.AddMinutes(-3));
 live.RunBackgroundUrlTests(live._currentServices);await live.Settle();
 Check(live.countryPicker.Updates==cycle+2 && live.countryPicker.Displayed==(cycle==1?-1:121),"every periodic result refreshes UI, including failure and recovery");
 }
 TunnelPlusService.Fail=false;TunnelPlusService.Hold=true;
 TunnelPlusService.Probe=(service,progress,cancel)=>progress(999);
 live.ResumeServerChecksAfterCleanup();await Until(()=>live.countryPicker.Displayed==999);
 await live.DrainServerChecksAsync();
 Check(live.countryPicker.Displayed==121 && liveService.urls[0].latency==121,"cancel/drain restores cached UI before allowing connection startup");
 await live.Settle();TunnelPlusService.Hold=false;
 // Delay dispatcher progress until AFTER the final result has been applied.
 live.Dispatcher.HoldPosted=true;int shown=live.countryPicker.Progresses;
 live.ResumeServerChecksAfterCleanup();await live.Settle();live.Dispatcher.ReleasePosted();await Task.Delay(10);
 Check(live.countryPicker.Displayed==121 && live.countryPicker.Progresses==shown,"queued progress cannot overwrite a completed result");
 // Rebinding API rows while an old RPC is in flight must reject its callbacks.
 live.Dispatcher.HoldPosted=true;TunnelPlusService.Hold=true;
 int callsBefore=TunnelPlusService.Calls.Count;live.ResumeServerChecksAfterCleanup();
 await Until(()=>TunnelPlusService.Calls.Count>callsBefore);
 live._urlTestCts.Cancel();var replacement=S("DE",21);live._currentServices=new IVPNService[]{replacement};
 live.probeCache.Bind(live._currentServices);live.countryPicker.OnRefresh=s=>Check(ReferenceEquals(s,replacement),"obsolete row cannot receive a final UI update");
 live.RunBackgroundUrlTests(live._currentServices);TunnelPlusService.Hold=false;await live.Settle();
 live.Dispatcher.ReleasePosted();await Task.Delay(10);
 Check(live.countryPicker.Progresses==shown && live.countryPicker.Displayed==121,"old progress is rejected after API list replacement");
 TunnelPlusService.Probe=null;TunnelPlusService.Calls=new ConcurrentQueue<string>();
 var bootstrap=new Program();bootstrap._currentServices=new IVPNService[]{S("DE",1),S("US",3)};
 bootstrap.probeCache=new ServerCheckCache("bootstrap","xfast",dir);bootstrap.probeCache.Bind(bootstrap._currentServices);
 TunnelPlusService.HoldCountry="US";bootstrap.RunBackgroundUrlTests(bootstrap._currentServices);
 for(int i=0;i<500 && !TunnelPlusService.Calls.Contains("US3");i++)await Task.Delay(2);
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","US3"}),"bootstrap immediately moves to next country without a three minute wait");
 await bootstrap.DrainServerChecksAsync();await bootstrap.Settle();
 string legacyDir=Path.Combine(dir,"legacy");Directory.CreateDirectory(legacyDir);
 foreach(var file in Directory.GetFiles(dir,"*.json")){
 var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));old.Remove("InitialScanCompleted");old.Remove("InitialCountries");
 File.WriteAllText(Path.Combine(legacyDir,Path.GetFileName(file)),old.ToString());}
 var legacy=new ServerCheckCache("bootstrap","xfast",legacyDir);legacy.Bind(new IVPNService[]{S("DE",1),S("US",3)});
 Check(!legacy.InitialScanCompleted && legacy.NextCountry=="US","legacy partial cache retains completed countries and resumes missing ones");
 var resumed=new Program();resumed._currentServices=new IVPNService[]{S("DE",1),S("US",3)};
 resumed.probeCache=new ServerCheckCache("bootstrap","xfast",dir);resumed.probeCache.Bind(resumed._currentServices);
 Check(!resumed.probeCache.InitialScanCompleted && resumed.probeCache.NextCountry=="US","incomplete bootstrap and unfinished country persist across restart");
 TunnelPlusService.HoldCountry=null;TunnelPlusService.Calls=new ConcurrentQueue<string>();
 resumed.RunBackgroundUrlTests(resumed._currentServices);await resumed.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"US3"}) && resumed.probeCache.InitialScanCompleted,"restart completes only remaining bootstrap countries");
 var completeCache=new ServerCheckCache("bootstrap","xfast",dir);completeCache.Bind(resumed._currentServices);
 Check(completeCache.InitialScanCompleted,"bootstrap completion persists across restart");
 TunnelPlusService.Calls=new ConcurrentQueue<string>();
 var p=new Program(); p._currentServices=new IVPNService[]{S("US",3),S("DE",2),S("DE",1)};
 p.probeCache=new ServerCheckCache("user","xfast",dir);p.probeCache.Bind(p._currentServices);
 p.RunBackgroundUrlTests(p._currentServices);await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE2","US3"}) && p.probeCache.InitialScanCompleted,"first startup checks every country sequentially including numbered rows");
 Check(p.probeTimer.Interval>TimeSpan.FromSeconds(175),"three minute cycle begins only after full initial scan");
 TunnelPlusService.Calls=new ConcurrentQueue<string>();p.countryPicker.Updates=0;
 p.ResumeServerChecksAfterCleanup();await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE2"}),"after bootstrap disconnect checks only the pending country");
 Check(p.countryPicker.Updates==2 && p.probeCache.NextCountry=="US","results refresh and cursor advances only after entire country");
 int count=TunnelPlusService.Calls.Count;
 p.RunBackgroundUrlTests(p._currentServices);await Task.Delay(20);
 Check(TunnelPlusService.Calls.Count==count && p.probeTimer.Running,"no second country before three minutes after completion");
 var restarted=new ServerCheckCache("user","xfast",dir);var fresh=new IVPNService[]{S("DE",1),S("DE",2),S("US",3)};restarted.Bind(fresh);
 Check(fresh[0].GetServerUrls()[0].latency==101 && restarted.NextCountry=="US","restart restores results and pending country");
 Check(!File.ReadAllText(Directory.GetFiles(dir,"*.json")[0]).Contains("secret-"),"cache does not store subscription credentials");
 p.PauseServerChecks();p.RunBackgroundUrlTests(p._currentServices);Check(TunnelPlusService.Calls.Count==count,"paused connection blocks tests");
 p.globalInfo.CurrentService=new object();p.ResumeServerChecksAfterCleanup();Check(TunnelPlusService.Calls.Count==count,"active connection blocks resume");
 p.globalInfo.CurrentService=null;TunnelPlusService.Hold=true;p.ResumeServerChecksAfterCleanup();
 for(int i=0;i<500 && TunnelPlusService.Calls.Count==count;i++)await Task.Delay(2);
 Check(TunnelPlusService.Calls.Last()=="US3","disconnect immediately starts pending country");
 await p.DrainServerChecksAsync();await p.Settle();Check(p.probeCache.NextCountry=="US","cancellation retains unfinished turn");
 TunnelPlusService.Hold=false;p.ResumeServerChecksAfterCleanup();await p.Settle();Check(p.probeCache.NextCountry=="DE","resumed turn finishes before moving on");
 p.IsVisible=false;p.probeSchedule.Completed(DateTime.UtcNow.AddMinutes(-3));TunnelPlusService.Fail=true;p.RunBackgroundUrlTests(p._currentServices);await p.Settle();
 var failed=p._currentServices[2].GetServerUrls()[0];Check(failed.latency==-1 && failed.LastSuccessfulLatency==101,"hidden app continues due tests; failure retains separate history");
 TunnelPlusService.Fail=false;TunnelPlusService.Hold=true;
 p.ResumeServerChecksAfterCleanup();int oldUpdates=p.countryPicker.Updates;
 for(int i=0;i<500 && !p.probeRunning;i++)await Task.Delay(2);
 p._urlTestCts.Cancel();p._currentServices=new IVPNService[]{S("DE",1),S("DE",2),S("US",3)};
 p.probeCache.Bind(p._currentServices);p.RunBackgroundUrlTests(p._currentServices);TunnelPlusService.Hold=false;
 for(int i=0;i<500 && (p.probeRunning || p.countryPicker.Updates==oldUpdates);i++)await Task.Delay(2);
 Check(p.countryPicker.Updates==oldUpdates+1 && p.probeCache.NextCountry=="DE","list replacement drains old probe and resumes pending country once");
 var failedReload=new ServerCheckCache("user","xfast",dir);var same=new[]{S("DE",1),S("DE",2),S("US",3)};failedReload.Bind(same);
 Check(same[0].urls[0].latency==-1 && same[0].urls[0].LastSuccessfulLatency==101,"failure/history survive restart");
 same[0].urls[0].extra_field_1="changed";failedReload.Bind(same);Check(same[0].urls[0].latency==0 && same[0].urls[0].LastSuccessfulLatency==0,"configuration changes invalidate results");
 var other=new ServerCheckCache("other-user","xfast",dir);other.Bind(same);Check(same.All(s=>s.urls[0].latency==0),"accounts have isolated results");
 var removed=new ServerCheckCache("user","xfast",dir);removed.Bind(new[]{S("DE",1)});Check(removed.NextCountry=="DE","removed pending country reconciles to live list");
 foreach(var file in Directory.GetFiles(dir,"*.json"))File.WriteAllText(file,"broken");var corrupt=new ServerCheckCache("user","xfast",dir);corrupt.Bind(same);Check(corrupt.NextCountry=="DE","corrupt cache safely starts fresh");
 var clock=new CountryProbeSchedule();var now=DateTime.UtcNow;Check(clock.Remaining(now)==TimeSpan.Zero,"new app starts immediately");clock.Completed(now);Check(clock.Remaining(now.AddSeconds(179))==TimeSpan.FromSeconds(1),"three minute delay measured from test completion");Check(clock.Remaining(now.AddMinutes(3))==TimeSpan.Zero,"next country due at three minutes");clock.RestartNow();Check(clock.Remaining(now)==TimeSpan.Zero,"disconnect overrides remaining wait");
 }finally{Directory.Delete(dir,true);}
 }
}
'''
with tempfile.TemporaryDirectory() as d:
 p=Path(d);(p/'Program.cs').write_text(stubs+region+tests)
 includes=''.join(f'<Compile Include="{root/path}" />' for path in ['IRSpeedyVPN/Services/ServerCheckCache.cs','IRSpeedyVPN/Services/CountryProbeSchedule.cs','IRSpeedyVPN/Models/NewService/Url.cs'])
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>'+includes+'<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Check.csproj'),'-v:q'],check=True)
