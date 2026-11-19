"""Exercise the real display runner and UrlTestFull against a fake Core transport.

Verifies per-config callbacks, preserved ordering, chain/SNI lookup, rejection,
and cancellation without starting Windows processes or a VPN.
"""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/Services/TunnelPlusService.cs').read_text()
probe = source[source.index('        public void UrlTestFull('):source.index('        private string ProbeContext(')]
lookup = source[source.index('        private string GetChainLink('):source.index('        // chain.txt is a static deployed file')]
build_chain = source[source.index('        private string[] BuildChainLinks('):source.index('        public void Disconnect()')]
stubs = r'''
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Diagnostics;using System.IO;
using IRSpeedyVPN.Models;using IRSpeedyVPN.Models.NewService;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Services.Libcore;using IRSpeedyVPN.Common;using IRSpeedyVPN.Services.Hysteria;
namespace IRSpeedyVPN.Models { public enum VPNType{NORMAL,CHAIN} class GlobalInfo{public Settings settings=new Settings();}class Settings{public Setting setting=new Setting();}class Setting{public string url_test="https://example.invalid/probe";} }
namespace IRSpeedyVPN.Common {
 static class LogHelper{public static void WriteExLog(string s){}public static void WriteLog(Exception e){throw e;}}
 static class ConnectionDiagnostics{public static long EventSequence;public static string Fingerprint(string s)=>"hash";}
 static class CoreDiagnosticMetadata{
  public static string Hash(string s)=>"hash";
  public static int ListenerPid(int port)=>IRSpeedyVPN.Services.Process.DisplayWorkers[port].Id;
 }
 static class FreePortManager{
  static int next=10000;
  public static int Returned,Admissions;
  public static int? RejectAdmission;
  public static readonly HashSet<int> Leased=new HashSet<int>();
  public static int Dequeue(){
   if(++Admissions==RejectAdmission)throw new TimeoutException();
   Leased.Add(++next);return next;
  }
  public static void Enqueue(int p){
   if(!Leased.Remove(p))throw new Exception("port returned twice or without a lease");
   Returned++;
  }
 }
 static class Order{public static IEnumerable<T> Randomize<T>(this IEnumerable<T> items)=>items.Reverse();}
}
namespace IRSpeedyVPN.Services.Hysteria {static class ConfigGenerator{public static void CleanupConfigFile(string s){}}}
namespace IRSpeedyVPN.Services.SingBox {
 static class ConfigGenerator {
  public class EndpointOverride{public string Server;public int ServerPort;}
  public static Dictionary<string,string[]> Chains;public static Dictionary<string,EndpointOverride> Endpoints;
  public static Dictionary<string,Tuple<int,string,string>> Socks;
  public static string[] GetRejectedProbeLinks(string config,string error,Dictionary<string,string> tags)=>new string[0];
  public static string GetUrlTestConfig(Dictionary<string,string[]> urls,int port,out Dictionary<string,string> tags,Dictionary<string,EndpointOverride> endpoints=null,Dictionary<string,Tuple<int,string,string>> socks=null,string prefix="",Dictionary<string,string> rejected=null){
   Chains=new Dictionary<string,string[]>(urls);Endpoints=endpoints;Socks=socks;
   tags=urls.Select((p,i)=>new{Tag=prefix+i,Link=p.Key}).ToDictionary(p=>p.Tag,p=>p.Link);return "fixture";
  }
 }
}
namespace IRSpeedyVPN.Services.Xray {
 static class ConfigGenerator {
  public class XraySocksInfo{public string Link,Tag,User,Pass;public int Port;}
  public static string UrlTestRefusalReason(string s)=>s.Contains("unsupported")?"unsupported":null;
  public static bool LinkNeedsXrayForUrlTest(string s)=>s.Contains("xray");
  public const string UrlTestOutboundDnsStrategy="ForceIP";
  public static string GetUrlTestXrayConfig(List<XraySocksInfo> infos,bool useCoreDns=false){if(!useCoreDns)throw new Exception("probe resolver was not opted in");return "fixture-xray";}
 }
}
namespace IRSpeedyVPN.Services {
 // A process-shaped double keeps platform process creation out of the fixture.
 // The production runner still starts/stops workers, validates listener owners and releases ports.
 class Process : IDisposable {
  internal static readonly Dictionary<int,Process> DisplayWorkers=new Dictionary<int,Process>();
  internal static int Starts,Stops;
  internal int Port,Id;
  internal bool HasExited;
  public static Process Start(ProcessStartInfo info) {
   var worker=new Process{Id=++Starts};
   if(info.Arguments.StartsWith("-port ")){
    worker.Port=int.Parse(info.Arguments.Substring(6));
    DisplayWorkers.Add(worker.Port,worker);
   }
   return worker;
  }
  public void BeginOutputReadLine(){}
  public void BeginErrorReadLine(){}
  public void WaitForExit(){if(!HasExited)throw new Exception("worker has not exited");}
  public void Dispose(){
   if(!HasExited)throw new Exception("worker disposed before stop");
   if(Port!=0)DisplayWorkers.Remove(Port);
   Stops++;
  }
 }
 class Client {
  internal static Func<TestReq,Action<TestResp>,Func<bool>,TestResp> Send;
  public TestResp TestWithProgress(TestReq req,Action<TestResp> report,Func<bool> cancel,Action<string> log,CancellationToken token)=>Send(req,report,cancel);
 }
 partial class TunnelPlusService {
  IServer server;GlobalInfo gInfo;bool cancelUrlTest;long urlTestSpeed;string selectedUrl;DateTime lastUrlTest;
  static object grpcLock=new object();Process coreProcess;bool coreOwned;const int CorePort=19810;const string SniScheme="sni://";
  public static string selectedChain;public string Name{get;set;}public int ID=>server.ID;public int CountryIndex=>0;
  Action<TunnelPlusService,bool,int,string> onConnectDisconnect;
  public TunnelPlusService(IServer server,GlobalInfo info){this.server=server;gInfo=info;}
  void Diagnostic(string stage,string detail=""){}string ProbeContext(string id,string link)=>"";
  void LogProbeAttempt(string a,string b,string c,TestReq d,TestResp e,Dictionary<string,string> f,long g,Exception h){}
  TestResp ExecuteCoreCall(Func<Client,TestResp> call,string operation)=>call(new Client());
  void EnsureCoreRunning(int port,ref Process p,ref bool owned){}
  string GetDefaultChainLink()=>null;
  static readonly object ownedExitGate=new object();
  static readonly HashSet<Process> ownedExitProcesses=new HashSet<Process>();
  static bool applicationExiting;
  internal static string SniConfigPath;
  internal static int SniPortsReturned;
  string ResolveCorePath()=>Path.Combine(Path.GetTempPath(),"fixture-core");
  class SniRuntime{public string ListenHost="127.0.0.1";public int ListenPort=40443;public Process Process;public string ConfigPath;}
  SniRuntime EnsureSniRuntime(string s,Dictionary<string,SniRuntime> runtimes,bool persistent){var r=new SniRuntime();runtimes[s]=r;return r;}
  string ParseSniLink(string link)=>link;
  SniRuntime StartSniRuntime(string link,bool persistent,Func<int> remaining,CancellationToken token){
   if(link!="sni://example.invalid"||persistent||remaining()<=0||token.IsCancellationRequested)
    throw new Exception("invalid SNI worker admission");
   SniConfigPath=Path.GetTempFileName();
   var process=Process.Start(new ProcessStartInfo{FileName="fixture-sni"});
   ownedExitProcesses.Add(process);
   return new SniRuntime{Process=process,ConfigPath=SniConfigPath};
  }
  void ReleaseSniListenPort(int port){if(port!=40443)throw new Exception("wrong SNI port");SniPortsReturned++;}
  void StopSniServers(Dictionary<string,SniRuntime> runtimes){}
  bool TryKillProcess(Process p){p.HasExited=true;return true;}
'''
tests = r'''
 }
 namespace Libcore {
  class LibcoreServiceClient {
   internal static Func<TestReq,int,CancellationToken,TestResp> Send;
   public LibcoreServiceClient(string host,int port){
    if(host!="127.0.0.1"||!Process.DisplayWorkers.ContainsKey(port))throw new Exception("display probe sent outside its worker");
   }
   public TestResp TestWithDeadline(TestReq request,int timeout,CancellationToken token)=>Send(request,timeout,token);
  }
 }
 class Program {
  class FixtureServer:IServer {
   public int ID=>1;public string Country{get;set;}="DE";public string Protocol{get;set;}public List<Url> urls{get;set;}
  }
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
  static void UrlTestFull(Url[] urls,CancellationToken cancellation,Action<Url,long> progress,Action<Url,long> completed){
   var service=new TunnelPlusService(new FixtureServer{urls=urls.ToList()},new GlobalInfo());
   service.UrlTestFull(urls,cancellation:cancellation,memberProgress:progress,memberCompleted:completed,preserveOrder:true);
  }
  static void Main(){
   var urls=new[]{new Url{url="vless://first",chainproxy=1,extra_field_1="vless://chain"},new Url{url="hy2://second",extra_field_1="sni://example.invalid"},new Url{url="vless://third-xray"}};
   int calls=0;var complete=new List<string>();var progress=new List<string>();
   Client.Send=(req,report,cancel)=>{
    calls++;Check(report!=null,"UrlTestFull enables Core streaming snapshots for member callbacks");
    Check(req.OutboundTags.Select(t=>int.Parse(t.Substring(t.LastIndexOf('-')+1))).SequenceEqual(new[]{0,1,2}),"RPC preserves global batch ordering");
    Check(SingBox.ConfigGenerator.Chains.Keys.SequenceEqual(urls.Select(u=>u.url)),"config preserves supplied member order");
    Check(SingBox.ConfigGenerator.Chains[urls[0].url].SequenceEqual(new[]{"vless://chain"})&&SingBox.ConfigGenerator.Endpoints.ContainsKey(urls[1].url),"mixed-country runner resolves each config's own chain and SNI");
    Check(req.NeedXray&&req.MaxConcurrency==15&&req.TestTimeoutMs==5000,"refresh retains Core Xray/concurrency/timeout policy");
    Check(req.Url=="https://example.invalid/probe","refresh retains the configured health-test URL");
    Check(req.XrayOutboundDnsStrategy==Xray.ConfigGenerator.UrlTestOutboundDnsStrategy,"refresh binds Xray DNS to the isolated probe box resolver");
    var result=new TestResp{Results=req.OutboundTags.Select((t,i)=>new URLTestResp{OutboundTag=t,LatencyMs=100+i}).ToList()};
    report(result);return result;
   };
   UrlTestFull(urls,CancellationToken.None,(u,l)=>progress.Add(u.url+":"+l),(u,l)=>complete.Add(u.url+":"+l));
   Check(calls==1&&progress.SequenceEqual(urls.Select((u,i)=>u.url+":"+(100+i)))&&complete.SequenceEqual(progress),"callbacks map unique Core tags back to the exact copied members once");
   Check(urls.Select(u=>u.latency).SequenceEqual(new long[]{100,101,102})&&FreePortManager.Returned==2,"real probe completes results and releases test ports");
   complete.Clear();Client.Send=(req,report,cancel)=>new TestResp{Results=req.OutboundTags.Select(t=>new URLTestResp{OutboundTag=t,LatencyMs=120}).ToList()};
   UrlTestFull(new[]{new Url{url="vless://unsupported"},new Url{url="hy2://valid"}},CancellationToken.None,null,(u,l)=>complete.Add(u.url+":"+l));
   Check(complete.SequenceEqual(new[]{"vless://unsupported:-1","hy2://valid:120"}),"rejected config completes as failed without blocking the valid member");
   var canceled=new CancellationTokenSource();complete.Clear();progress.Clear();
   Client.Send=(req,report,cancel)=>{canceled.Cancel();var r=new TestResp{Results=req.OutboundTags.Select(t=>new URLTestResp{OutboundTag=t,LatencyMs=1}).ToList()};report(r);return r;};
   UrlTestFull(urls,canceled.Token,(u,l)=>progress.Add(u.url),(u,l)=>complete.Add(u.url));
   Check(progress.Count==0&&complete.Count==0,"canceled Core snapshots cannot publish member results");
   DisplayProbe();
  }
  static void DisplayProbe(){
   const string target="https://probe.example.invalid/generate_204";
   var config=new Url{url="vless://fixture-xray@example.invalid",extra_field_1="sni://example.invalid"};
   TunnelPlusService.selectedChain="vless://fixture-base@example.invalid";
   var info=new GlobalInfo();
   int calls=0;
   LibcoreServiceClient.Send=(request,timeout,token)=>{
    calls++;
    Check(request.MaxConcurrency==1&&request.OutboundTags.Count==1&&request.NeedXray&&
     request.XrayOutboundDnsStrategy==Xray.ConfigGenerator.UrlTestOutboundDnsStrategy,
     "display RPC admits one tag with Xray using the isolated worker's DNS");
    Check(request.Url==(calls==1?target:UrlTestRetryPolicy.RetryUrl)&&timeout>0&&timeout<=(calls==1?4000:8000),
     "display RPC uses the configured primary and bounded fallback deadlines");
    Check(SingBox.ConfigGenerator.Chains[config.url].SequenceEqual(new[]{TunnelPlusService.selectedChain})&&
     SingBox.ConfigGenerator.Endpoints[config.url].ServerPort==40443&&SingBox.ConfigGenerator.Socks.ContainsKey(config.url),
     "display config keeps the selected member's chain, SNI and bridge fields");
    var resp=new TestResp{Results=new List<URLTestResp>{
     new URLTestResp{OutboundTag="foreign-tag",LatencyMs=1},
     new URLTestResp{OutboundTag=request.OutboundTags[0],LatencyMs=calls==1?501:123}
    }};
    return resp;
   };
   var result=TunnelPlusService.TestRefreshBatch(config,info,target,CancellationToken.None);
   Check(result.Latency==123&&calls==2&&config.latency==0,"display RPC selects its tagged response and does not mutate the original config");
   Check(Process.Starts==2&&Process.Stops==2&&FreePortManager.Leased.Count==0&&
    TunnelPlusService.SniPortsReturned==1&&!File.Exists(TunnelPlusService.SniConfigPath),
    "display worker and SNI process, temporary file and leases are cleaned up");

   int starts=Process.Starts;
   result=TunnelPlusService.TestRefreshBatch(new Url{url="vless://unsupported"},info,target,CancellationToken.None);
   Check(result.Measured&&!result.Successful&&Process.Starts==starts,"a refused config fails before allocating a worker");
   FreePortManager.RejectAdmission=FreePortManager.Admissions+2;
   result=TunnelPlusService.TestRefreshBatch(config,info,target,CancellationToken.None);
   Check(!result.Measured&&Process.Starts==starts&&FreePortManager.Leased.Count==0,
    "port exhaustion is unmeasured and releases a partially acquired lease");
   FreePortManager.RejectAdmission=null;

   using(var stop=new CancellationTokenSource()){
    LibcoreServiceClient.Send=(request,timeout,token)=>{
     Check(token==stop.Token,"display RPC receives cancellation token");
     stop.Cancel();
     return new TestResp{Results=new List<URLTestResp>{new URLTestResp{OutboundTag=request.OutboundTags[0],LatencyMs=1}}};
    };
    try{TunnelPlusService.TestRefreshBatch(config,info,target,stop.Token);throw new Exception("expected cancellation");}
    catch(OperationCanceledException){ }
   }
   Check(Process.Starts==Process.Stops&&FreePortManager.Leased.Count==0&&
    TunnelPlusService.SniPortsReturned==2&&!File.Exists(TunnelPlusService.SniConfigPath),
    "a late RPC success after cancellation is discarded and all worker resources drain");
  }
 }
}
'''
with tempfile.TemporaryDirectory(prefix='refresh-core-dispatch-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(stubs + probe + lookup + build_chain + tests)
    linked = ['Services/TunnelPlusService.RefreshTests.cs', 'Services/UrlTestRetryPolicy.cs',
              'Services/Libcore/LibcoreMessages.cs', 'Services/UrlTestCoordinator.cs', 'Interfaces/IServer.cs', 'Models/NewService/Url.cs']
    includes = ''.join(f'<Compile Include="{root / "IRSpeedyVPN" / item}" />' for item in linked)
    net48 = '--net48' in sys.argv
    framework = 'net48' if net48 else 'net8.0'
    references = '<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" />' if net48 else ''
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>' + framework + '</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0649;CS0168</NoWarn></PropertyGroup><ItemGroup>' + includes + references + '<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
    executable = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] != '--net48' else 'dotnet'
    action = ['build', str(path / 'Checks.csproj')] if net48 else ['run', '--project', str(path / 'Checks.csproj')]
    subprocess.run([executable] + action + ['-v:q'], check=True)
