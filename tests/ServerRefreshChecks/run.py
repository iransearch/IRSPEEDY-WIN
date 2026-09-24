"""Compile and exercise the actual server-list scheduler with a fake UI dispatcher."""
from pathlib import Path
import tempfile, subprocess, sys
root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
region = source.split('        private readonly System.Windows.Threading.DispatcherTimer probeTimer =',1)[1].split('        #endregion',1)[0]
region = 'private readonly System.Windows.Threading.DispatcherTimer probeTimer =' + region
stubs = r'''
using System; using System.Linq; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
namespace System.Windows.Threading { class DispatcherTimer { public TimeSpan Interval {get;set;} public void Stop(){} public void Start(){} } }
class Url { public DateTime latencychkTime; }
interface IVPNService { bool IsUrlTestSupported {get;} string CountryCode {get;} string Country {get;} List<Url> GetServerUrls(); void UrlTest(); }
class TunnelPlusService : IVPNService {
 public string CountryCode {get;set;} public string Country=>CountryCode; public bool IsUrlTestSupported=>true;
 public List<Url> urls=new List<Url>{new Url()}; public List<Url> GetServerUrls()=>urls;
 public static List<string> Calls=new List<string>(); public static bool Hold;
 public void UrlTest(){} public void UrlTestFull(object a,bool b,object c,Func<bool> cancel){
 lock(Calls) Calls.Add(CountryCode); while(Hold && !cancel()) Thread.Sleep(1);
 if(!cancel()) urls[0].latencychkTime=DateTime.Now;
 }
}
class Info { public object CurrentService; }
class FakeDispatcher { public bool CheckAccess()=>true; public void Invoke(Action a)=>a(); public void BeginInvoke(Action a)=>a(); }
class Picker { public void RefreshGroup(IVPNService s){} }
class LogHelper { public static void WriteLog(Exception e)=>throw e; }
class UrlTestCoordinator { public static volatile bool AbortRequested; public static void CancelAll()=>AbortRequested=true; public static void BeginBatch()=>AbortRequested=false; }
class Program {
 FakeDispatcher Dispatcher=new FakeDispatcher(); Picker countryPicker=new Picker(); Info globalInfo=new Info();
 bool IsVisible=true; CancellationTokenSource _urlTestCts; IVPNService[] _currentServices;
'''
tests = r'''
 static void Check(bool value,string name){if(!value)throw new Exception(name); Console.WriteLine("PASS "+name);}
 async Task Settle(){await probeTask; for(int i=0;i<100 && !initialScanFinished;i++)await Task.Delay(2);}
 static async Task Main(){
 var p=new Program(); p._currentServices=new IVPNService[]{new TunnelPlusService{CountryCode="DE"},new TunnelPlusService{CountryCode="US"}};
 p.RunBackgroundUrlTests(p._currentServices); await p.Settle();
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"DE","US"}),"initial pass checks all countries");
 TunnelPlusService.Calls.Clear(); p.countryChecked["DE"]=DateTime.UtcNow; p.countryChecked["US"]=DateTime.MinValue;
 p.RunBackgroundUrlTests(p._currentServices); await p.probeTask; await Task.Delay(20);
 Check(TunnelPlusService.Calls.SequenceEqual(new[]{"US"}),"next pass checks only oldest country");
 p.PauseServerChecks(); p.RunBackgroundUrlTests(p._currentServices); Check(TunnelPlusService.Calls.Count==1,"paused connection blocks checks");
 p.globalInfo.CurrentService=new object(); p.ResumeServerChecksAfterCleanup(); Check(TunnelPlusService.Calls.Count==1,"live service blocks checks even after resume");
 p.globalInfo.CurrentService=null; TunnelPlusService.Hold=true; p.ResumeServerChecksAfterCleanup();
 for(int i=0;i<100 && TunnelPlusService.Calls.Count<2;i++)await Task.Delay(2);
 await p.DrainServerChecksAsync(); Check(p.probeTask.IsCompleted,"connection barrier drains cancelled probe");
 Check(p.probeTimer.Interval==TimeSpan.FromMinutes(3),"three minute interval");
 }
}
'''
with tempfile.TemporaryDirectory() as d:
 p=Path(d); (p/'Program.cs').write_text(stubs+region+tests)
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Check.csproj'),'-v:q'],check=True)
