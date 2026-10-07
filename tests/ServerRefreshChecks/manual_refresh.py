"""Production global queue/cache/controller checks with fake Core and UI."""
from pathlib import Path
import ast
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
fixture = ast.parse(Path(__file__).with_name('run.py').read_text())
stubs = next(ast.literal_eval(n.value) for n in fixture.body if isinstance(n, ast.Assign)
             and any(isinstance(t, ast.Name) and t.id == 'stubs' for t in n.targets))
stubs = stubs.replace('bool manualRefreshBusy,manualRefreshExternalPause;Task manualRefreshTask=Task.CompletedTask;void CancelManualRefresh(){}',
                      'bool IsLoaded=true,_isUrlTestSupported=true;int HeaderUpdates;void UpdateHeaderIcons(){HeaderUpdates++;}')
source = (root / 'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
region = source.split('        #region Background URL tests', 1)[1].split('        #endregion', 1)[0]
manual = (root / 'IRSpeedyVPN/UserControls/UCServerList.ServerResultsRefresh.cs').read_text()
manual = manual[manual.index('        private bool manualRefreshBusy;'):manual.rfind('\n    }')]
tests = r'''
 static int passed;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
 static TunnelPlusService S(string country,int id,int members=1)=>new TunnelPlusService{ID=id,CountryCode=country,
   urls=Enumerable.Range(0,members).Select(i=>new Url{url="vless://fixture-"+id+"-"+i,extra_field_1="chain-"+id+"-"+i,chainproxy=1}).ToList()};
 static async Task Until(Func<bool> condition){for(int i=0;i<1000&&!condition();i++)await Task.Delay(2);Check(condition(),"async condition settled");}
 static Program View(string dir,params TunnelPlusService[] rows){var p=new Program{_currentServices=rows};p.probeCache=new ServerCheckCache(Guid.NewGuid().ToString(),"xfast",dir);p.probeCache.Bind(rows);return p;}
 static void Main()=>new UiContext().Run(Run);
 static async Task Run(){
  string dir=Path.Combine(Path.GetTempPath(),"manual-refresh-"+Guid.NewGuid());Directory.CreateDirectory(dir);
  try{
   // Permutation, bounded batches, duplicate metadata and numbered rows.
   var rows=new[]{S("DE",1,12),S("US",2,12),S("DE",3,12)};
   var plan=new ServerRefreshPlan(rows,new Random(19));var all=new List<ServerRefreshPlan.Candidate>();
   var first=plan.TakeBatch();Check(first.Count==15&&first.SelectMany(c=>c.Owners).Select(o=>o.Service.CountryCode).Distinct().Count()==2,"batch mixes countries with a 15-config bound");
   all.AddRange(first);while(plan.HasPending)all.AddRange(plan.TakeBatch());
   Check(all.Count==36&&all.Select(c=>c.Probe.url).Distinct().Count()==36,"every config appears once in the global permutation");
   Check(!all.Select(c=>c.Probe.url).SequenceEqual(rows.SelectMany(s=>s.urls).Select(u=>u.url)),"country enumeration does not determine the queue");
   Check(all.All(c=>c.Owners.All(o=>!ReferenceEquals(o.Url,c.Probe)&&o.Url.extra_field_1==c.Probe.extra_field_1)),"probe copies preserve chain metadata without sharing live results");
   var done=new List<IRSpeedyVPN.Interfaces.IVPNService>();foreach(var item in all){done.AddRange(plan.Complete(item));Check(plan.Complete(item).Length==0,"duplicate completion is idempotent");}
   Check(done.Count==3&&done.Select(s=>s.ID).Distinct().Count()==3,"numbered rows in one country complete independently");
   var a=S("DE",11);var b=S("US",12);b.urls[0].url=a.urls[0].url;b.urls[0].extra_field_1=a.urls[0].extra_field_1;
   plan=new ServerRefreshPlan(new[]{a,b});var shared=plan.TakeBatch();Check(plan.Total==1&&plan.Complete(shared[0]).Length==2,"identical configurations test once and update both countries");
   b.urls[0].extra_field_1="different-chain";plan=new ServerRefreshPlan(new[]{a,b},new Random(4));
   Check(plan.Total==2&&plan.TakeBatch().Count==1&&plan.TakeBatch().Count==1,"same link with different chain metadata stays in separate batches");
   b.urls[0].url=a.urls[0].url.ToUpperInvariant();plan=new ServerRefreshPlan(new[]{a,b});
   Check(plan.Total==2&&plan.TakeBatch().Count==1&&plan.TakeBatch().Count==1,"case variants cannot collide in case-insensitive SNI/chain lookup");

   // All history and priority values are erased persistently; API replacement is guarded.
   var cache=new ServerCheckCache("persistent","xfast",dir);cache.Bind(rows);
   foreach(var row in rows){foreach(var u in row.urls){u.latency=90;u.latencychkTime=DateTime.Now;}cache.Record(row,DateTime.Now.AddSeconds(-1));}
   Check(cache.ClearForManualRefresh(rows)&&rows.SelectMany(s=>s.urls).All(u=>u.latency==0&&u.latencychkTime==default(DateTime)&&u.LastSuccessfulLatency==0),"refresh clears all displayed/current/historical results");
   var replacement=new[]{S("DE",1,12),S("US",2,12),S("DE",3,12)};var reloaded=new ServerCheckCache("persistent","xfast",dir);reloaded.Bind(replacement);
   Check(replacement.SelectMany(s=>s.urls).All(u=>u.latency==0&&u.LastSuccessfulLatency==0)&&reloaded.NextService==null,"erased measurements cannot reappear from disk or a normal queue");
   cache.Bind(replacement);cache.RecordRefreshMember(rows[0],rows[0].urls[0],999,DateTime.UtcNow);
   Check(!cache.ClearForManualRefresh(rows)&&replacement[0].urls[0].latency==0,"obsolete API objects cannot clear/commit replacement rows");
   cache.FinishManualRefresh(rows);Check(!cache.InitialScanCompleted,"obsolete manual round cannot mark the replacement initial scan complete");

   // Early per-country finish and provisional minimum; final updates run on UI.
   var live=View(dir,S("DE",21,2),S("US",22,1));var originals=live._currentServices.SelectMany(s=>s.GetServerUrls()).ToArray();
   live.countryPicker.OnRefresh=row=>Check(row.GetServerUrls().All(u=>u.latencychkTime!=default(DateTime)),"row reorders only after all its own members finish");
   TunnelPlusService.ManualProbe=(urls,token,progress,complete)=>{
    Check(urls.All(u=>!originals.Contains(u)),"Core receives private URL objects");
    var de=urls.Where(u=>u.url.Contains("21-")).ToArray();var us=urls.Single(u=>u.url.Contains("22-"));
    progress(de[0],80);progress(de[1],400);Thread.Sleep(20);
    Check(live.countryPicker.Displayed==80,"slower partial results do not replace a faster row result");
    complete(us,70);Thread.Sleep(20);Check(live.countryPicker.Updates==1,"one finished country moves while another country is still testing");
    complete(de[0],80);complete(de[1],-1);progress(us,999);
   };
   live.RefreshServerResults();await live.manualRefreshTask;await Task.Delay(20);
   Check(!live.manualRefreshBusy&&live.probeCache.InitialScanCompleted&&live.countryPicker.Updates==2,"manual round finishes once with per-row updates");
   Check(live._currentServices[0].GetServerUrls().Select(u=>u.latency).OrderBy(x=>x).SequenceEqual(new long[]{-1,80}),"final member success/failure is preserved independently of shuffle order");
   Check(live.countryPicker.Displayed!=999,"late progress cannot overwrite a completed country");
   int before=TunnelPlusService.Calls.Count;live.RunBackgroundUrlTests(live._currentServices);await Task.Delay(20);
   Check(TunnelPlusService.Calls.Count==before,"manual completion cannot start an automatic extra scan");

   // Old worker drains before clearing; connection cancels private work and joins it.
   var cancel=View(dir,S("DE",31,2));foreach(var u in cancel._currentServices[0].GetServerUrls()){u.latency=99;u.latencychkTime=DateTime.Now;}
   cancel.probeCache.Record(cancel._currentServices[0],DateTime.Now.AddSeconds(-1));
   TunnelPlusService.Hold=true;TunnelPlusService.Probe=(s,p,c)=>{s.urls[0].latency=777;};
   before=TunnelPlusService.Calls.Count;cancel.RunBackgroundUrlTests(cancel._currentServices);await Until(()=>TunnelPlusService.Calls.Count>before);
   bool entered=false;Action<Url,long> late=null;Url lateUrl=null;
   TunnelPlusService.ManualProbe=(urls,token,p,c)=>{Check(cancel._currentServices[0].GetServerUrls().All(u=>u.latency==0&&u.LastSuccessfulLatency==0),"old worker restored and drained before clearing");late=p;lateUrl=urls[0];entered=true;while(!token.IsCancellationRequested)Thread.Sleep(1);p(urls[0],888);c(urls[0],888);token.ThrowIfCancellationRequested();};
   cancel.RefreshServerResults();cancel.RefreshServerResults();await Until(()=>entered);
   Check(cancel.manualRefreshBusy,"duplicate refresh click shares one in-flight round");
   cancel.PauseServerChecksForConnection();await cancel.DrainServerChecksAsync();TunnelPlusService.Hold=false;TunnelPlusService.Probe=null;
   late(lateUrl,999);await Task.Delay(20);
   Check(!cancel.manualRefreshBusy&&cancel.probesPaused&&cancel.probesRequireDisconnect&&!cancel.probeRunning,"connection waits for cancellation drain and retains pause ownership");
   Check(cancel._currentServices[0].GetServerUrls().All(u=>u.latency==0)&&cancel.countryPicker.Displayed!=999,"late canceled results cannot resurrect old or provisional values");
   cancel.globalInfo.CurrentService=new object();before=cancel.HeaderUpdates;cancel.RefreshServerResults();Check(!cancel.manualRefreshBusy&&cancel.HeaderUpdates==before,"connected VPN cannot start refresh");

   // A finalized member of an unfinished row survives cancellation/reload;
   // provisional measurements and prior history never do.
   var partial=View(dir,S("DE",35,2));entered=false;
   TunnelPlusService.ManualProbe=(u,t,p,c)=>{c(u.Single(x=>x.url.EndsWith("-0")),125);p(u.Single(x=>x.url.EndsWith("-1")),60);entered=true;while(!t.IsCancellationRequested)Thread.Sleep(1);t.ThrowIfCancellationRequested();};
   partial.RefreshServerResults();await Until(()=>entered);partial.PauseServerChecks();await partial.DrainServerChecksAsync();
   var rebind=new[]{S("DE",35,2)};var saved=Directory.GetFiles(dir,"*.json").Select(f=>Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(f))).Single(o=>o["Results"].Children().Any(r=>(long)r.First["Latency"]==125));
   Check(saved["Results"].Children().Count()==1,"unfinished row's finalized member is persisted before cancellation drain completes");
   partial.probeCache.Bind(rebind);
   Check(rebind[0].urls[0].latency==125&&rebind[0].urls[1].latency==0&&rebind[0].urls[1].LastSuccessfulLatency==0,"cancel/reload retains finalized fresh members but discards provisional measurements");

   // Missing member callbacks (RPC failure) and API replacement terminate safely.
   var failed=View(dir,S("DE",41),S("US",42));failed.probesRequireDisconnect=true;TunnelPlusService.ManualProbe=(u,t,p,c)=>{};
   failed.RefreshServerResults();await failed.manualRefreshTask;await Task.Delay(10);
   Check(failed._currentServices.All(s=>s.GetServerUrls().All(u=>u.latency==-1))&&!failed.manualRefreshBusy,"missing RPC results cannot leave a spinning refresh");
   Check(!failed.probesRequireDisconnect,"explicit disconnected refresh starts a new round after a failed connect");
   var stale=View(dir,S("DE",51));entered=false;
   TunnelPlusService.ManualProbe=(u,t,p,c)=>{entered=true;while(!t.IsCancellationRequested)Thread.Sleep(1);p(u[0],444);c(u[0],444);};
   stale.RefreshServerResults();await Until(()=>entered);stale.CancelManualRefresh();stale._currentServices=new[]{S("US",52)};stale.probeCache.Bind(stale._currentServices);
   await stale.manualRefreshTask;await Task.Delay(10);
   Check(stale._currentServices[0].GetServerUrls()[0].latency==0&&!stale.manualRefreshBusy,"retired API snapshot cannot update the replacement list");
   Console.WriteLine("Manual refresh checks passed: "+passed);
  }finally{Directory.Delete(dir,true);}
 }
}
'''
with tempfile.TemporaryDirectory(prefix='manual-refresh-checks-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(stubs + region + manual + tests)
    includes = ''.join(f'<Compile Include="{root / source}" />' for source in [
        'IRSpeedyVPN/Services/ServerCheckCache.cs', 'IRSpeedyVPN/Services/ServerRefreshPlan.cs', 'IRSpeedyVPN/Models/NewService/Url.cs'])
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'run', '--project', str(path / 'Checks.csproj'), '-v:q'], check=True)
