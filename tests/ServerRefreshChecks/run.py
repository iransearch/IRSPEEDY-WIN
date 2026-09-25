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
 public static ConcurrentQueue<string> Calls=new ConcurrentQueue<string>(); public static volatile bool Hold,Fail;
 public void UrlTest(){} public void UrlTestFull(object a,bool b,object c,Func<bool> cancel){
 Calls.Enqueue(CountryCode+ID); while(Hold && !cancel()) Thread.Sleep(1);
 if(!cancel()) foreach(var u in urls){u.latency=Fail?-1:100+ID;u.latencychkTime=DateTime.Now;}
 }
}
class Info { public object CurrentService; }
class FakeDispatcher { readonly SynchronizationContext context=SynchronizationContext.Current; public bool CheckAccess()=>true; public void Invoke(Action a)=>a(); public void BeginInvoke(Action a)=>context.Post(_=>a(),null); }
class UiContext : SynchronizationContext {
 readonly BlockingCollection<Action> queue=new BlockingCollection<Action>();
 public override void Post(SendOrPostCallback d,object state)=>queue.Add(()=>d(state));
 public void Run(Func<Task> action){SetSynchronizationContext(this);var task=action();while(!task.IsCompleted){if(queue.TryTake(out var next,100))next();}task.GetAwaiter().GetResult();}
}
class Picker { public int Updates; public void RefreshGroup(IVPNService s){Updates++;} }
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
 static void Main(){new UiContext().Run(MainAsync);}
 static async Task MainAsync(){
 string dir=Path.Combine(Path.GetTempPath(),"irspeedy-check-"+Guid.NewGuid());Directory.CreateDirectory(dir);
 try{
 var p=new Program(); p._currentServices=new IVPNService[]{S("US",3),S("DE",2),S("DE",1)};
 p.probeCache=new ServerCheckCache("user","xfast",dir);p.probeCache.Bind(p._currentServices);
 p.RunBackgroundUrlTests(p._currentServices);await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE1","DE2"}),"startup checks one country, including its numbered rows");
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
