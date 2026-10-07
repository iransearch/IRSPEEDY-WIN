"""Exercise the real batch runner/UrlTestFull against a fake Core transport.

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
stubs = r'''
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Diagnostics;using System.IO;
using IRSpeedyVPN.Models;using IRSpeedyVPN.Models.NewService;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Services.Libcore;using IRSpeedyVPN.Common;using IRSpeedyVPN.Services.Hysteria;
namespace IRSpeedyVPN.Models { public enum VPNType{NORMAL,VOD,CHAIN} class GlobalInfo{public Settings settings=new Settings();}class Settings{public Setting setting=new Setting();}class Setting{public string url_test="https://example.invalid/probe";} }
namespace IRSpeedyVPN.Common {
 static class LogHelper{public static void WriteExLog(string s){}public static void WriteLog(Exception e){throw e;}}
 static class ConnectionDiagnostics{public static long EventSequence;public static string Fingerprint(string s)=>"hash";}
 static class CoreDiagnosticMetadata{public static string Hash(string s)=>"hash";}
 static class FreePortManager{static int next=10000;public static int Returned;public static int Dequeue()=>++next;public static void Enqueue(int p){Returned++;}}
 static class Order{public static IEnumerable<T> Randomize<T>(this IEnumerable<T> items)=>items.Reverse();}
}
namespace IRSpeedyVPN.Services.Hysteria {static class ConfigGenerator{public static void CleanupConfigFile(string s){}}}
namespace IRSpeedyVPN.Services.SingBox {
 static class ConfigGenerator {
  public class EndpointOverride{public string Server;public int ServerPort;}
  public static Dictionary<string,string[]> Chains;public static Dictionary<string,EndpointOverride> Endpoints;
  public static string GetUrlTestConfig(Dictionary<string,string[]> urls,int port,out Dictionary<string,string> tags,Dictionary<string,EndpointOverride> endpoints,Dictionary<string,Tuple<int,string,string>> socks,string prefix){
   Chains=new Dictionary<string,string[]>(urls);Endpoints=new Dictionary<string,EndpointOverride>(endpoints);
   tags=urls.Select((p,i)=>new{Tag=prefix+i,Link=p.Key}).ToDictionary(p=>p.Tag,p=>p.Link);return "fixture";
  }
 }
}
namespace IRSpeedyVPN.Services.Xray {
 static class ConfigGenerator {
  public class XraySocksInfo{public string Link,Tag,User,Pass;public int Port;}
  public static string UrlTestRefusalReason(string s)=>s.Contains("unsupported")?"unsupported":null;
  public static bool LinkNeedsXrayForUrlTest(string s)=>s.Contains("xray");
  public static string GetUrlTestXrayConfig(List<XraySocksInfo> infos)=>"fixture-xray";
 }
}
namespace IRSpeedyVPN.Services {
 static class UrlTestCoordinator{public static bool AbortRequested;}
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
  TestResp ExecuteCoreCall(Func<Client,TestResp> call)=>call(new Client());
  void EnsureCoreRunning(int port,ref Process p,ref bool owned){}
  string GetDefaultChainLink()=>null;
  string[] BuildChainLinks(string link,string[] others)=>new[]{link}.Concat(others).Where(s=>s!=null).ToArray();
  class SniRuntime{public string ListenHost="127.0.0.1";public int ListenPort=40443;}
  SniRuntime EnsureSniRuntime(string s,Dictionary<string,SniRuntime> runtimes,bool persistent){var r=new SniRuntime();runtimes[s]=r;return r;}
  void StopSniServers(Dictionary<string,SniRuntime> runtimes){}void TryKillProcess(Process p){}
'''
tests = r'''
 }
 class Program {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
  static void Main(){
   var urls=new[]{new Url{url="vless://first",chainproxy=1,extra_field_1="vless://chain"},new Url{url="hy2://second",extra_field_1="sni://example.invalid"},new Url{url="vless://third-xray"}};
   int calls=0;var complete=new List<string>();var progress=new List<string>();
   Client.Send=(req,report,cancel)=>{
    calls++;Check(report!=null,"manual refresh enables Core streaming snapshots");
    Check(req.OutboundTags.Select(t=>int.Parse(t.Substring(t.LastIndexOf('-')+1))).SequenceEqual(new[]{0,1,2}),"RPC preserves global batch ordering");
    Check(SingBox.ConfigGenerator.Chains.Keys.SequenceEqual(urls.Select(u=>u.url)),"config preserves supplied member order");
    Check(SingBox.ConfigGenerator.Chains[urls[0].url].SequenceEqual(new[]{"vless://chain"})&&SingBox.ConfigGenerator.Endpoints.ContainsKey(urls[1].url),"mixed-country runner resolves each config's own chain and SNI");
    Check(req.NeedXray&&req.MaxConcurrency==15&&req.TestTimeoutMs==5000,"refresh retains Core Xray/concurrency/timeout policy");
    var result=new TestResp{Results=req.OutboundTags.Select((t,i)=>new URLTestResp{OutboundTag=t,LatencyMs=100+i}).ToList()};
    report(result);return result;
   };
   TunnelPlusService.TestRefreshBatch(urls,new GlobalInfo(),CancellationToken.None,(u,l)=>progress.Add(u.url+":"+l),(u,l)=>complete.Add(u.url+":"+l));
   Check(calls==1&&progress.SequenceEqual(urls.Select((u,i)=>u.url+":"+(100+i)))&&complete.SequenceEqual(progress),"callbacks map unique Core tags back to the exact copied members once");
   Check(urls.Select(u=>u.latency).SequenceEqual(new long[]{100,101,102})&&FreePortManager.Returned==2,"real probe completes results and releases test ports");
   complete.Clear();Client.Send=(req,report,cancel)=>new TestResp{Results=req.OutboundTags.Select(t=>new URLTestResp{OutboundTag=t,LatencyMs=120}).ToList()};
   TunnelPlusService.TestRefreshBatch(new[]{new Url{url="vless://unsupported"},new Url{url="hy2://valid"}},new GlobalInfo(),CancellationToken.None,null,(u,l)=>complete.Add(u.url+":"+l));
   Check(complete.SequenceEqual(new[]{"vless://unsupported:-1","hy2://valid:120"}),"rejected config completes as failed without blocking the valid member");
   var canceled=new CancellationTokenSource();complete.Clear();progress.Clear();
   Client.Send=(req,report,cancel)=>{canceled.Cancel();var r=new TestResp{Results=req.OutboundTags.Select(t=>new URLTestResp{OutboundTag=t,LatencyMs=1}).ToList()};report(r);return r;};
   TunnelPlusService.TestRefreshBatch(urls,new GlobalInfo(),canceled.Token,(u,l)=>progress.Add(u.url),(u,l)=>complete.Add(u.url));
   Check(progress.Count==0&&complete.Count==0,"canceled Core snapshots cannot publish member results");
  }
 }
}
'''
with tempfile.TemporaryDirectory(prefix='refresh-core-dispatch-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(stubs + probe + lookup + tests)
    linked = ['Services/TunnelPlusService.RefreshTests.cs', 'Services/UrlTestRetryPolicy.cs',
              'Services/Libcore/LibcoreMessages.cs', 'Interfaces/IServer.cs', 'Models/NewService/Url.cs']
    includes = ''.join(f'<Compile Include="{root / "IRSpeedyVPN" / item}" />' for item in linked)
    net48 = '--net48' in sys.argv
    framework = 'net48' if net48 else 'net8.0'
    references = '<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" />' if net48 else ''
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>' + framework + '</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0649;CS0168</NoWarn></PropertyGroup><ItemGroup>' + includes + references + '<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
    executable = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] != '--net48' else 'dotnet'
    action = ['build', str(path / 'Checks.csproj')] if net48 else ['run', '--project', str(path / 'Checks.csproj')]
    subprocess.run([executable] + action + ['-v:q'], check=True)
