"""Run production authenticated refresh and factory replacement with fake API/UI.

No credentials, live API calls, Core process or Windows runtime are used.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
from run import stubs

root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/MainWindow.FastStartup.cs').read_text()
methods = source[source.index('        private void ValidateSessionAndRefreshServerList('):source.index('        private void FastStartup_Closing')]
factory = (root / 'IRSpeedyVPN/Services/ServiceFactory.cs').read_text()
renew = factory[factory.index('        public void RenewServiceList(List<Group> groups)'):factory.index('        public void RenewServiceList(List<Server> servers)')]
ui = stubs[stubs.index('class FakeDispatcher {'):stubs.index('class Picker {')]
prefix = r'''
using System;using System.Linq;using System.Collections.Generic;using System.Collections.Concurrent;using System.Threading;using System.Threading.Tasks;using System.Net;
using IRSpeedyVPN.Common;
class Url {public string url;public int is_subscription;public long latency;public DateTime latencychkTime;}
class ServerEx {public int ID;public string Country,Protocol;public List<Url> urls=new List<Url>();public bool Invalid;}
class Group {public string title="xfast";public List<ServerEx> servers=new List<ServerEx>();}
interface IVPNService {string Name{get;set;}List<Url> GetServerUrls();}
class TunnelPlusService:IVPNService {
 internal readonly ServerEx Server;public string Name{get;set;}public List<Url> GetServerUrls()=>Server.urls;
 public TunnelPlusService(ServerEx s,Info i){if(s.Invalid)throw new Exception("bad config");Server=s;}
}
class CiscoService:TunnelPlusService {public CiscoService(ServerEx s,Info i):base(s,i){} }
static class ConnectionDiagnostics {
 internal static readonly ConcurrentQueue<string> Lines=new ConcurrentQueue<string>();
 internal static void Write(string stage,string fields)=>Lines.Enqueue(stage+" "+fields);
}
static class WireGuardEndpoint {
 internal static string DiagnosticReason(Exception error)=>error.GetType().Name;
 internal static bool IsLink(string link)=>link!=null&&link.StartsWith("vpn://",StringComparison.OrdinalIgnoreCase);
 internal static object Build(string profile,string tag) {
  if(profile!="vpn://supported-fixture")throw new FormatException("invalid fixture key");
  return new object();
 }
}
static class ShareHandler {
 internal static string ImportFromConfigLink(string link,out string message,out string failure) {
  failure=link=="vpn://unreadable-fixture"?"invalid-wireguard-profile-section":null;
  message="";return link=="vpn://unreadable-fixture"?null:link;
 }
}
class Factory {
 public List<IVPNService> services=new List<IVPNService>();public List<IVPNService> Services=>services;Info globalInfo=new Info();
 RENEW
}
class Info {public string Username="user",Password="pw",ServerResponse;public object CurrentService,settings=new object();
 public void Import(Account a,string p,string t){Username=a.UserAccount.Username;Password=p;settings=a.Settings;} }
class Account {public User UserAccount=new User();public List<Group> groups=new List<Group>();public object Settings;}
class User {public string Username,Status="OK";public DateTime? ExpiryDate;}
class LoginData {public Account Decrypted=new Account();public string DecryptedString="cached",message="denied";}
class Response {public HttpStatusCode StatusCode=HttpStatusCode.OK;public LoginData ResponseData=new LoginData();public bool VerifiedServerList=true;}
class Controller {
 public Response Next=new Response();public int Calls;public ManualResetEventSlim Hold;
 // Deliver the blocked response even after cancellation to exercise the late-result guard.
 public Response Login2(string u,string p,CancellationToken cancellation){Interlocked.Increment(ref Calls);Hold?.Wait();return Next;}
}
class ListView {public int Binds;public bool Preserve;public void ConfigureResidential(Account account){}public void RefreshServicesFromFactory(bool preserveManualRefresh=false){Binds++;Preserve=preserveManualRefresh;} }
class Cache {public string TempPath="temp";public int Saves;public void SaveConfig(Account a,string p){Saves++;} }
static class VpnTimeZone {public static DateTime AccountNow=>DateTime.Now;}
static class LogHelper {public static void WriteLog(Exception e){}public static void WriteExLog(string message){} }
'''.replace('RENEW', renew)
program = r'''
class Program {
 readonly SemaphoreSlim serverRefreshGate=new SemaphoreSlim(1,1);
 readonly AccountSessionMaintenance accountSessionMaintenance=new AccountSessionMaintenance();
 readonly CancellationTokenSource loginApplicationExit=new CancellationTokenSource();
 bool ExitCleanupStarted;
 void ScheduleSessionMaintenance(){}
 bool IsUserLogin=true,LoadLocal,IsRememberChecked=true;long connectionRequestVersion;
 Info gInfo=new Info();Controller serviceController=new Controller();Factory serviceFactory=new Factory();
 FakeDispatcher Dispatcher=new FakeDispatcher();ListView uCServerList=new ListView();Cache localResource=new Cache();string Message;
 void ShowMessage(string text){Message=text;}void LogoutInvalidSession(string m,bool expired=false){IsUserLogin=false;accountSessionMaintenance.End();}
 bool IsExplicitSessionFailure(HttpStatusCode status)=>status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden||status==HttpStatusCode.NotAcceptable;
 static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
 static List<Group> Groups(params ServerEx[] servers)=>new List<Group>{new Group{servers=servers.ToList()}};
 static ServerEx S(params string[] urls)=>new ServerEx{urls=urls.Select(url=>new Url{url=url}).ToList()};
 static Program View(){var p=new Program();p.accountSessionMaintenance.Begin(true);p.serviceFactory.RenewServiceList(Groups(S("vless://old")));p.serviceController.Next.ResponseData.Decrypted.groups=Groups(S("vless://new"));return p;}
 static async Task Until(Func<bool> condition){for(int i=0;i<1000&&!condition();i++)await Task.Delay(2);Check(condition(),"async API phase settled");}
 static void Main()=>new UiContext().Run(Run);
 static async Task Run(){
  const string awg="vpn://supported-fixture",cisco="https://cisco.example.invalid/",proxy="vless://fixture";
  var catalog=new Factory();
  var mixed=S(cisco,awg,proxy);mixed.ID=41;mixed.Country="AZ";mixed.Protocol="native";
  catalog.RenewServiceList(Groups(mixed,S(cisco),S(cisco,"vpn://unreadable-fixture"),S(cisco,"vpn://invalid-key")));
  var awgRow=(TunnelPlusService)catalog.Services[0];
  Check(awgRow.GetType()==typeof(TunnelPlusService)&&awgRow.Name=="xfast"
   &&awgRow.Server.ID==41&&awgRow.Server.Country=="AZ"&&awgRow.Server.Protocol=="native",
   "a valid AWG member keeps the mixed Azerbaijan row eligible for a country Smart Pool");
  Check(awgRow.GetServerUrls().Select(u=>u.url).SequenceEqual(new[]{awg,proxy})
   &&mixed.urls.Select(u=>u.url).SequenceEqual(new[]{cisco,awg,proxy}),
   "mixed rows probe and pool their tunnel links while the original API catalog is preserved");
  Check(catalog.Services.Skip(1).All(s=>s is CiscoService),
   "existing Cisco rows remain Cisco when no valid native WireGuard endpoint is available");

  Check(ConnectionDiagnostics.Lines.Contains("country-catalog row=41 entries=3 gatewayLinks=1 subscriptions=0 wireguardLinks=1 adapter=TunnelPlusService")
   &&ConnectionDiagnostics.Lines.Contains("country-catalog row=0 entries=1 gatewayLinks=1 subscriptions=0 wireguardLinks=0 adapter=CiscoService")
   &&ConnectionDiagnostics.Lines.Contains("country-catalog row=0 step=import reason=invalid-wireguard-profile-section")
   &&ConnectionDiagnostics.Lines.Contains("country-catalog row=0 step=endpoint reason=FormatException")
   &&!ConnectionDiagnostics.Lines.Any(line=>line.Contains("://")),"catalog evidence distinguishes missing, unreadable and unusable WireGuard members without URLs");
  var live=View();object settings=live.gInfo.settings,connected=new object();live.gInfo.CurrentService=connected;
  Check(await live.RefreshServerListForManualCheckAsync(()=>true,CancellationToken.None),"manual action awaits successful authenticated list fetch");
  Check(live.serviceController.Calls==1&&live.serviceFactory.Services[0].GetServerUrls()[0].url=="vless://new"&&live.uCServerList.Binds==1&&live.uCServerList.Preserve,"fresh API list binds while retaining manual pause/cancellation ownership");
  Check(ReferenceEquals(live.gInfo.settings,settings)&&ReferenceEquals(live.gInfo.CurrentService,connected)&&live.localResource.Saves==1,"refresh preserves settings and exact live service while updating account cache");
  foreach(var kind in new[]{"timeout","500","missing","auth-server","malformed"}){
   var failed=View();var original=failed.serviceFactory.Services;original[0].GetServerUrls()[0].latency=101;
   if(kind=="timeout")failed.serviceController.Next=null;
   if(kind=="500")failed.serviceController.Next.StatusCode=HttpStatusCode.InternalServerError;
   if(kind=="missing")failed.serviceController.Next.ResponseData.Decrypted.groups=null;
   if(kind=="auth-server")failed.serviceController.Next.ResponseData.Decrypted.UserAccount.Status="AuthServerError";
   if(kind=="malformed")failed.serviceController.Next.ResponseData.Decrypted.groups=Groups(S("vless://valid"),new ServerEx{Invalid=true,urls=new List<Url>{new Url{url="vless://bad"}}});
   Check(!await failed.RefreshServerListForManualCheckAsync(()=>true,CancellationToken.None),kind+" returns fallback signal");
   Check(ReferenceEquals(failed.serviceFactory.Services,original)&&original[0].GetServerUrls()[0].latency==101&&failed.uCServerList.Binds==0&&failed.localResource.Saves==0&&failed.IsUserLogin,kind+" preserves complete previous list, results and session");
  }
  var invalid=View();invalid.serviceController.Next.StatusCode=HttpStatusCode.Forbidden;
  Check(!await invalid.RefreshServerListForManualCheckAsync(()=>true,CancellationToken.None)&&!invalid.IsUserLogin,"explicit authentication failure retains existing logout policy");
  var stale=View();var old=stale.serviceFactory.Services;stale.Dispatcher.Before=()=>Interlocked.Increment(ref stale.connectionRequestVersion);
  Check(!await stale.RefreshServerListForManualCheckAsync(()=>true,CancellationToken.None)&&ReferenceEquals(stale.serviceFactory.Services,old)&&stale.uCServerList.Binds==0,"connect/logout generation change rejects an old API response");

  var canceled=View();old=canceled.serviceFactory.Services;canceled.serviceController.Hold=new ManualResetEventSlim();
  using(var token=new CancellationTokenSource()){
   Task<bool> pending=canceled.RefreshServerListForManualCheckAsync(()=>!token.IsCancellationRequested,token.Token);
   await Until(()=>Volatile.Read(ref canceled.serviceController.Calls)==1);token.Cancel();
   Check(await Task.WhenAny(pending,Task.Delay(1000))==pending,"connection/logout cancellation does not wait for synchronous API timeout");
   try{await pending;throw new Exception("expected cancellation");}catch(OperationCanceledException){}
   Check(canceled.serverRefreshGate.CurrentCount==0,"canceled in-flight API worker retains shared gate until it finishes");
   canceled.serviceController.Hold.Set();await Until(()=>canceled.serverRefreshGate.CurrentCount==1);
   Check(ReferenceEquals(canceled.serviceFactory.Services,old)&&canceled.uCServerList.Binds==0&&canceled.localResource.Saves==0,"late canceled API response cannot replace old list or credentials");
  }

  var serial=View();serial.serviceController.Hold=new ManualResetEventSlim();Task periodic=Task.Run(()=>serial.ValidateSessionAndRefreshServerList());
  await Until(()=>Volatile.Read(ref serial.serviceController.Calls)==1);
  Task<bool> manual=serial.RefreshServerListForManualCheckAsync(()=>true,CancellationToken.None);await Task.Delay(20);
  Check(!manual.IsCompleted&&serial.serviceController.Calls==1,"manual fetch queues behind periodic maintenance without skipping or overlapping API calls");
  serial.serviceController.Hold.Set();await periodic;Check(await manual&&serial.serviceController.Calls==2,"queued manual action performs its own fresh fetch before testing");
  var queued=View();await queued.serverRefreshGate.WaitAsync();
  using(var token=new CancellationTokenSource()){
   Task<bool> waiting=queued.RefreshServerListForManualCheckAsync(()=>true,token.Token);token.Cancel();
   try{await waiting;throw new Exception("expected canceled queue");}catch(OperationCanceledException){}
   Check(queued.serviceController.Calls==0,"cancellation while queued never starts an API request");
  }queued.serverRefreshGate.Release();
 }
'''
with tempfile.TemporaryDirectory(prefix='api-refresh-checks-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(prefix + ui + program + methods + '\n}\n')
    maintenance = root / 'IRSpeedyVPN/Common/AccountSessionMaintenance.cs'
    framework = 'net48' if '--net48' in sys.argv else 'net8.0'
    (path / 'Checks.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>' + framework +
        '</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0649;CS0414</NoWarn>' +
        '</PropertyGroup><ItemGroup><Compile Include="' + str(maintenance) + '" /></ItemGroup></Project>'
    )
    args = ['build', str(path / 'Checks.csproj')] if framework == 'net48' else ['run', '--project', str(path / 'Checks.csproj')]
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet'] + args + ['-v:q'], check=True)
