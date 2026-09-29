"""Exercise production scheduler + persistent cache; fake network/UI, real file I/O."""
from pathlib import Path
import tempfile, subprocess, sys
root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
region = source.split('        #region Background URL tests',1)[1].split('        #endregion',1)[0]
assert 'probeTimer' not in source and 'ServerProbeSchedule' not in source
stubs = r'''
using System; using System.Linq; using System.Collections.Generic; using System.Collections.Concurrent; using System.Threading; using System.Threading.Tasks; using System.IO;
using IRSpeedyVPN.Services; using IRSpeedyVPN.Interfaces; using IRSpeedyVPN.Models.NewService;
namespace IRSpeedyVPN.Models { public enum VPNType { NORMAL,VOD,CHAIN } }
namespace IRSpeedyVPN.Interfaces { interface IVPNService { int ID {get;} string Name {get;} bool IsUrlTestSupported {get;} string CountryCode {get;} string Country {get;} List<Url> GetServerUrls(); void UrlTest(); } }
class TunnelPlusService : IVPNService {
 public int ID {get;set;} public string Name=>"xfast"; public string CountryCode {get;set;} public string Country=>CountryCode; public bool IsUrlTestSupported=>true;
 public List<Url> urls=new List<Url>(); public List<Url> GetServerUrls()=>urls;
 public static ConcurrentQueue<string> Calls=new ConcurrentQueue<string>(); public static volatile bool Hold,Fail; public static string HoldCountry;
 public static Action<TunnelPlusService,Action<long>,Func<bool>> Probe;
 public void UrlTest(){} public void UrlTestFull(object a,bool b,Action<long> progress,Func<bool> cancel,CancellationToken cancellation){
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
 public int Updates,Progresses; public long Displayed; public bool Checking;
 public void SetGroupChecking(IVPNService s,bool checking){CheckThread();Checking=checking;}
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
 TunnelPlusService.Probe=(service,progress,cancel)=>{if(!live.countryPicker.Checking)throw new Exception("indicator must start before first result");if(progress==null)throw new Exception("missing live progress callback");progress(750);};
 live.RunBackgroundUrlTests(live._currentServices);
 await Until(()=>live.countryPicker.Progresses==1);
 Check(live.countryPicker.Checking,"indicator remains active while RPC is in flight");
 Check(live.probeRunning && live.countryPicker.Updates==0 && live.countryPicker.Displayed==750,"first success reaches UI while remaining server checks are blocked");
 Check(liveService.urls[0].latencychkTime==default(DateTime),"partial progress does not commit incomplete results to cache");
 TunnelPlusService.Hold=false;await live.Settle();
 Check(!live.countryPicker.Checking,"indicator stops when probe completes");
 Check(live.countryPicker.Displayed==121 && live.countryPicker.Updates==1,"final result replaces progress on UI thread before worker completion");
 // Each explicit disconnect starts a fresh full round, including failure/recovery.
 TunnelPlusService.Probe=null;
 for(int cycle=0;cycle<3;cycle++){
 TunnelPlusService.Fail=cycle==1;
 live.ResumeServerChecksAfterCleanup(true);await live.Settle();
 Check(live.countryPicker.Updates==cycle+2 && live.countryPicker.Displayed==(cycle==1?-1:121),"every disconnect round refreshes UI, including failure and recovery");
 }
 TunnelPlusService.Fail=false;TunnelPlusService.Hold=true;
 TunnelPlusService.Probe=(service,progress,cancel)=>{Check(progress==null,"refresh does not publish intermediate results");service.urls[0].latency=999;};
 int refreshCalls=TunnelPlusService.Calls.Count;
 live.ResumeServerChecksAfterCleanup(true);await Until(()=>TunnelPlusService.Calls.Count>refreshCalls);
 Check(live.countryPicker.Displayed==121,"previous result stays visible until the whole country completes");
 live.PauseServerChecksForConnection();
 await live.DrainServerChecksAsync();
 Check(!live.countryPicker.Checking,"cancel and drain stop the indicator");
 Check(live.countryPicker.Displayed==121 && liveService.urls[0].latency==121,"cancel/drain restores cached UI before allowing connection startup");
 await live.Settle();TunnelPlusService.Hold=false;
 // Refresh rounds never post provisional results, even with delayed dispatcher work.
 live.Dispatcher.HoldPosted=true;int shown=live.countryPicker.Progresses;
 live.ResumeServerChecksAfterCleanup(true);await live.Settle();live.Dispatcher.ReleasePosted();await Task.Delay(10);
 Check(live.countryPicker.Displayed==121 && live.countryPicker.Progresses==shown,"queued progress cannot overwrite a completed result");
 // Rebinding API rows while an old RPC is in flight must reject its callbacks.
 live.Dispatcher.HoldPosted=true;TunnelPlusService.Hold=true;
 int callsBefore=TunnelPlusService.Calls.Count;live.ResumeServerChecksAfterCleanup(true);
 await Until(()=>TunnelPlusService.Calls.Count>callsBefore);
 live._urlTestCts.Cancel();var replacement=S("DE",21);live._currentServices=new IVPNService[]{replacement};
 live.probeCache.Bind(live._currentServices);live.countryPicker.OnRefresh=s=>Check(ReferenceEquals(s,replacement),"obsolete row cannot receive a final UI update");
 live.RunBackgroundUrlTests(live._currentServices);TunnelPlusService.Hold=false;await live.Settle();
 live.Dispatcher.ReleasePosted();await Task.Delay(10);
 Check(live.countryPicker.Progresses==shown && live.countryPicker.Displayed==121,"old progress is rejected after API list replacement");
 TunnelPlusService.Probe=null;TunnelPlusService.Calls=new ConcurrentQueue<string>();

 // Partial bootstrap: save one completed row, hold the next and restart with a
 // different RNG. The unfinished row and all remaining turns must survive.
 var bootstrap=new Program();bootstrap._currentServices=new IVPNService[]{S("DE",1),S("US",3),S("DE",2)};
 bootstrap.probeCache=new ServerCheckCache("bootstrap","xfast",dir,new Random(31));bootstrap.probeCache.Bind(bootstrap._currentServices);
 int firstId=bootstrap.probeCache.NextService.ID;
 TunnelPlusService.Probe=(service,progress,cancel)=>{if(service.ID!=firstId)TunnelPlusService.Hold=true;};
 bootstrap.RunBackgroundUrlTests(bootstrap._currentServices);
 await Until(()=>TunnelPlusService.Calls.Count==2);
 int pendingId=bootstrap.probeCache.NextService.ID;
 Check(TunnelPlusService.Calls.First().EndsWith(firstId.ToString()) && firstId!=pendingId,"bootstrap advances immediately to another row");
 await bootstrap.DrainServerChecksAsync();await bootstrap.Settle();
 Check(!bootstrap.probeCache.InitialScanCompleted && bootstrap.probeCache.NextService.ID==pendingId,"cancellation preserves the unfinished row");
 string legacyDir=Path.Combine(dir,"legacy");Directory.CreateDirectory(legacyDir);
 foreach(var file in Directory.GetFiles(dir,"*.json")){
 var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));old["Version"]=1;old.Remove("PendingRows");old.Remove("CompletedRows");old["NextCountry"]="DE";
 File.WriteAllText(Path.Combine(legacyDir,Path.GetFileName(file)),old.ToString());}
 var legacy=new ServerCheckCache("bootstrap","xfast",legacyDir,new Random(7));legacy.Bind(new IVPNService[]{S("DE",1),S("US",3),S("DE",2)});
 var missing=new List<int>();for(int i=0;i<2;i++){var next=legacy.NextService;missing.Add(next.ID);legacy.CompleteService(next);}
 Check(missing.Distinct().Count()==2 && !missing.Contains(firstId) && legacy.InitialScanCompleted,"country-cache migration preserves results and checks only missing bootstrap rows");
 var resumed=new Program();resumed._currentServices=new IVPNService[]{S("DE",2),S("DE",1),S("US",3)};
 resumed.probeCache=new ServerCheckCache("bootstrap","xfast",dir,new Random(999));resumed.probeCache.Bind(resumed._currentServices);
 Check(!resumed.probeCache.InitialScanCompleted && resumed.probeCache.NextService.ID==pendingId,"restart and API reorder preserve unfinished queue head");
 TunnelPlusService.Hold=false;TunnelPlusService.Probe=null;TunnelPlusService.Calls=new ConcurrentQueue<string>();
 resumed.RunBackgroundUrlTests(resumed._currentServices);await resumed.Settle();
 Check(TunnelPlusService.Calls.Count==2 && TunnelPlusService.Calls.Distinct().Count()==2
  && !TunnelPlusService.Calls.Any(c=>c.EndsWith(firstId.ToString())) && resumed.probeCache.InitialScanCompleted,"restart finishes bootstrap without repeating completed rows");
 var completeCache=new ServerCheckCache("bootstrap","xfast",dir);completeCache.Bind(resumed._currentServices);
 Check(completeCache.InitialScanCompleted,"bootstrap completion survives restart");


 // Bootstrap remains shuffled; each disconnect resets a finite, ordered queue.
 var input=new IVPNService[]{S("US",3),S("DE",2),S("DE",1),S("CA",4),S("AT",5)};
 var queue=new ServerCheckCache("queue","xfast",dir,new Random(17));queue.Bind(input);queue.PersistQueue();
 var bootstrapOrder=new List<int>();
 for(int i=0;i<input.Length;i++){var next=queue.NextService;bootstrapOrder.Add(next.ID);queue.CompleteService(next);}
 Check(bootstrapOrder.Distinct().Count()==input.Length && queue.NextService==null,"bootstrap finishes once without a pre-created periodic round");
 for(int round=0;round<3;round++){
 queue.RestartFromFirstCountry();var seen=new List<int>();
 for(int i=0;i<input.Length;i++){
  var next=queue.NextService;seen.Add(next.ID);
  queue.Bind(input.Reverse());Check(queue.NextService.ID==next.ID,"display reorder cannot change pending turn");
  queue.CompleteService(next);
 }
 Check(seen.SequenceEqual(new[]{5,4,1,2,3}) && queue.NextService==null,"disconnect round covers countries and numbered rows in order exactly once");
 queue.Bind(input);Check(queue.NextService==null,"rebind after completion cannot start another round");
 }
 queue.PersistQueue();
 var queueReload=new ServerCheckCache("queue","xfast",dir,new Random(123));queueReload.Bind(input.Reverse());
 Check(queueReload.NextService==null,"completed round remains idle after app restart");
 queueReload.RestartFromFirstCountry();var oldNext=queueReload.NextService;int nextId=oldNext.ID;
 var replacements=input.Select(s=>(IVPNService)S(s.CountryCode,s.ID)).ToArray();queueReload.Bind(replacements);
 queueReload.CompleteService(oldNext);Check(queueReload.NextService.ID==nextId,"late old-row completion cannot skip replacement");
 var surviving=replacements.Where(s=>s.ID!=nextId).Concat(new[]{S("NL",6)}).ToArray();queueReload.Bind(surviving);
 var reconciled=new List<int>();while(queueReload.NextService!=null){var next=queueReload.NextService;reconciled.Add(next.ID);queueReload.CompleteService(next);}
 Check(!reconciled.Contains(nextId) && reconciled.Distinct().Count()==surviving.Length && reconciled.Contains(6),"active round prunes removed rows and includes new ones without repeating survivors");
 queueReload.Bind(surviving.Concat(new[]{S("FR",7)}));Check(queueReload.NextService==null,"API refresh after completion waits for the next disconnect");

 // Drive actual UI scheduling: no timers, immediate next row, full restart after cancel.
 TunnelPlusService.Calls=new ConcurrentQueue<string>();
 var p=new Program();p._currentServices=new IVPNService[]{S("US",3),S("DE",2),S("DE",1)};
 p.probeCache=new ServerCheckCache("user","xfast",dir,new Random(11));p.probeCache.Bind(p._currentServices);
 p.RunBackgroundUrlTests(p._currentServices);await p.Settle();
 Check(TunnelPlusService.Calls.Count==3 && TunnelPlusService.Calls.Distinct().Count()==3 && p.probeCache.InitialScanCompleted,"initial scan covers all numbered rows immediately");
 TunnelPlusService.Calls=new ConcurrentQueue<string>();p.countryPicker.Updates=0;
 p.ResumeServerChecksAfterCleanup(true);await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE2","US3"}) && p.countryPicker.Updates==3,"disconnect checks every country consecutively without a delay");
 int count=TunnelPlusService.Calls.Count;
 p.RunBackgroundUrlTests(p._currentServices);p.ResumeServerChecksAfterCleanup();await Task.Delay(20);
 Check(TunnelPlusService.Calls.Count==count,"navigation and ordinary cleanup do not start a new completed round");
 var fresh=new IVPNService[]{S("DE",1),S("DE",2),S("US",3)};
 var restarted=new ServerCheckCache("user","xfast",dir);restarted.Bind(fresh);
 Check(fresh.All(s=>s.GetServerUrls()[0].latency==100+s.ID) && restarted.NextService==null,"results survive restart and completed queue stays empty");
 Check(Directory.GetFiles(dir,"*.json").All(f=>!File.ReadAllText(f).Contains("secret-")),"persisted queue/results contain no raw subscription credentials");
 p.PauseServerChecks();p.RunBackgroundUrlTests(p._currentServices);Check(TunnelPlusService.Calls.Count==count,"paused connection blocks tests");
 p.globalInfo.CurrentService=new object();p.ResumeServerChecksAfterCleanup(true);Check(TunnelPlusService.Calls.Count==count,"active connection blocks resume");
 p.globalInfo.CurrentService=null;TunnelPlusService.HoldCountry="US";p.ResumeServerChecksAfterCleanup(true);
 await Until(()=>TunnelPlusService.Calls.Count==count+3);
 Check(p.probeCache.NextService.ID==3,"earlier countries finish while the final country remains in flight");
 p.PauseServerChecksForConnection();await p.DrainServerChecksAsync();await p.Settle();
 var canceledReload=new ServerCheckCache("user","xfast",dir);canceledReload.Bind(fresh);
 Check(canceledReload.NextService==null,"login or app reload cannot revive a connection-canceled round");
 count=TunnelPlusService.Calls.Count;p.ResumeServerChecksAfterCleanup();p.RunBackgroundUrlTests(p._currentServices);await Task.Delay(20);
 Check(TunnelPlusService.Calls.Count==count,"failed connect cleanup cannot resume the canceled round");
 TunnelPlusService.HoldCountry=null;TunnelPlusService.Calls=new ConcurrentQueue<string>();
 p.ResumeServerChecksAfterCleanup(true);await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE2","US3"}),"disconnect after interruption restarts at the first country, not the unfinished country");
 // A quick reconnect/disconnect can arrive before the old worker has drained.
 TunnelPlusService.Calls=new ConcurrentQueue<string>();TunnelPlusService.Hold=true;
 p.ResumeServerChecksAfterCleanup(true);await Until(()=>TunnelPlusService.Calls.Count==1);
 p.PauseServerChecksForConnection();p.ResumeServerChecksAfterCleanup(true);TunnelPlusService.Hold=false;await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE1","DE2","US3"}),"rapid resume drains the old worker before starting exactly one fresh round");
 p.IsVisible=false;TunnelPlusService.Fail=true;p.ResumeServerChecksAfterCleanup(true);await p.Settle();
 var failed=p._currentServices.Single(s=>s.ID==1).GetServerUrls()[0];
 Check(failed.latency==-1 && failed.LastSuccessfulLatency==101,"hidden window finishes the round and failures retain separate success history");
 TunnelPlusService.Fail=false;
 var failedReload=new ServerCheckCache("user","xfast",dir);var same=new[]{S("DE",1),S("DE",2),S("US",3)};failedReload.Bind(same);
 Check(same[0].urls[0].latency==-1 && same[0].urls[0].LastSuccessfulLatency==101,"failure/history survive restart");
 same[0].urls[0].extra_field_1="changed";failedReload.Bind(same);
 Check(same[0].urls[0].latency==0 && same[0].urls[0].LastSuccessfulLatency==0 && failedReload.NextService==null,"changed configuration invalidates results but does not start an idle scan");
 var other=new ServerCheckCache("other-user","xfast",dir);other.Bind(same);Check(same.All(s=>s.urls[0].latency==0),"accounts have isolated results");
 // Upgrade a completed v2 cache with its automatically created pending rotation.
 var legacyCompleted=new ServerCheckCache("completed-v2","xfast",dir);legacyCompleted.Bind(fresh);legacyCompleted.PersistQueue();
 foreach(var file in Directory.GetFiles(dir,"*.json")){
 var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));old["Version"]=2;old["InitialScanCompleted"]=true;old.Remove("RoundInProgress");old.Remove("SequentialRound");File.WriteAllText(file,old.ToString());}
 var upgraded=new ServerCheckCache("completed-v2","xfast",dir);upgraded.Bind(fresh);
 Check(upgraded.NextService==null,"upgrade discards old periodic queue after completed bootstrap");
 upgraded.RestartFromFirstCountry();Check(upgraded.NextService.ID==1,"upgraded cache starts from first country on explicit disconnect");
 foreach(var file in Directory.GetFiles(dir,"*.json"))File.WriteAllText(file,"broken");
 var corrupt=new ServerCheckCache("user","xfast",dir);corrupt.Bind(same);
 Check(corrupt.NextService!=null && !corrupt.InitialScanCompleted,"corrupt cache starts a fresh scan safely");
 }finally{Directory.Delete(dir,true);}
 }
}
'''
with tempfile.TemporaryDirectory() as d:
 p=Path(d);(p/'Program.cs').write_text(stubs+region+tests)
 includes=''.join(f'<Compile Include="{root/path}" />' for path in ['IRSpeedyVPN/Services/ServerCheckCache.cs','IRSpeedyVPN/Models/NewService/Url.cs'])
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>'+includes+'<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Check.csproj'),'-v:q'],check=True)
