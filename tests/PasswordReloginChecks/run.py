"""Production re-login/session lifecycle with inert UI/network/clock boundaries.
Usage: run.py DOTNET CSC NET8_REFS [NET48_REFS]
No live credentials, API endpoints or VPN processes are used.
"""
from pathlib import Path
import subprocess, sys, tempfile
root = Path(__file__).resolve().parents[2]
s = (root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
flow = s[s.index('        private void BeginPasswordChangeLogin('):s.index('        private void Logout_PreviewMouseDown',s.index('        private void BeginPasswordChangeLogin('))]
login = s[s.index('        bool Login('):s.index('        private List<DeviceInfo> TryParseDeviceList')]
refresh_source = (root/'IRSpeedyVPN/MainWindow.FastStartup.cs').read_text()
refresh = refresh_source[refresh_source.index('        private void BeginAccountSession('):refresh_source.index('        private void FastStartup_Closing')]
logout = s[s.index('        void Logout(string Message'):s.index('        private void btnWeb_PreviewMouseDown')]
recharge = s[s.index('        private void RechareLogout('):s.index('        private bool CheckUpdateExist()')]
maintenance = root/'IRSpeedyVPN/Common/AccountSessionMaintenance.cs'
list_source = (root/'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text(encoding='utf-8-sig')
prepare = list_source[list_source.index('        internal void PrepareServerChecksForLogin()'):list_source.index('        internal void ResumeServerChecksAfterCleanup(')]
assert 'uCServerList.PrepareServerChecksForLogin();\n            ShowControl(uCServerList);' in s
prefix = r'''
using Timer=TimerStub;
using System; using System.Linq; using System.Threading; using System.Threading.Tasks; using System.Diagnostics; using System.Net;
using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;
namespace IRSpeedyVPN.Interfaces {interface IVPNService {void Disconnect();}}
class ProbeService:IVPNService {public int Disconnects;public Action OnDisconnect;public void Disconnect(){Interlocked.Increment(ref Disconnects);OnDisconnect?.Invoke();}}
namespace IRSpeedyVPN.Services {class TunnelPlusService:ProbeService {public bool Canceled;public void CancelPendingConnection(){Canceled=true;}}}
class Shop {public string shop_url; }class Settings {public Shop setting;}
class Info { public string Username="user",Password="old",ServerResponse; public IVPNService CurrentService=new ProbeService();public Settings settings; public void Import(Account a,string p,string t){Password=p;} }
class Result { public bool IsSuccess; public int Code; public string ErrorMessage; }
class Response<T> { public HttpStatusCode StatusCode; public T ResponseData;public bool VerifiedServerList=true; }
class User { public string Username,Status="OK"; public DateTime? ExpiryDate; }
class Account { public User UserAccount=new User(); public Settings Settings;public object groups=new object(); }
class LoginData { public string data,message="denied",DecryptedString="account"; public Account Decrypted=new Account(); }
class Controller {
 public Response<Result> Next=new Response<Result>{StatusCode=HttpStatusCode.OK,ResponseData=new Result{IsSuccess=true,Code=0}};
 public Response<LoginData> Auth=new Response<LoginData>{StatusCode=HttpStatusCode.OK,ResponseData=new LoginData()};
 public string AuthPassword; public int Changes,Logins; public Exception Failure;
 public Response<Result> ChangePassword(string u,string o,string n){Changes++;return Next;}
 public Response<LoginData> Login2(string u,string p,CancellationToken cancellation=default(CancellationToken)){cancellation.ThrowIfCancellationRequested();if(Failure!=null)throw Failure;Logins++;AuthPassword=p;return Auth;}
}
class TimerStub {public static long Now;public long DueAt;public int Period;readonly TimerCallback callback;public TimerStub(){}public TimerStub(TimerCallback cb,object state,int due,int period){callback=cb;Change(due,period);}public void Change(int due,int period){DueAt=due<0?-1:Now+due;Period=period;}public void Fire(){callback?.Invoke(null);} }
class LoginForm { public string User,Password; public bool Remember; public void SetUserPassword(string u,string p,bool r){User=u;Password=p;Remember=r;} public void HideRenewMessage(){}public void ResetInput(){}public void ShowRenewMessage(string url){}public void SetUserName(string value){User=value;} }
class ServerList {
 readonly System.Collections.Generic.HashSet<string> initializedProbeScopes=new System.Collections.Generic.HashSet<string>();
 public void ConfigureResidential(object account){}
 public bool probesPaused=true;
 public bool probesRequireDisconnect=true,probeRestartRequested=true,probeLoginRoundRequested;
 PREPARE_METHOD
 public bool Drained,Paused;public Task Drain=Task.CompletedTask; public Task DrainServerChecksAsync(){Drained=true;return Drain;} public void PauseServerChecks(){Paused=true;} public void RefreshServicesFromFactory(bool preserveManualRefresh=false){} }
class ProbeTimer {public bool Stopped; public void Stop(){Stopped=true;} }
class Factory { public void RenewServiceList(object groups){} }
class FakeDispatcher { public Action Before;public void Invoke(Action action){Before?.Invoke();action();} }
class Label {public string Text;}
class Proxy {public int Detaches;public bool Fail;public void Detach(){Detaches++;if(Fail)throw new InvalidOperationException("detach");} }
class Cache {public string TempPath="temp";public string Password="old";public int Saves,Removes;public void SaveConfig(Account a,string p){Saves++;Password=p;}public void RemoveConfig(){Removes++;Password=null;} }
class LogHelper {public static void WriteExLog(string s){} public static void WriteLog(Exception e){} }
static class VpnTimeZone { public static DateTime AccountNow=>DateTime.Now;public static void EndConnection(){} }
enum Visibility {Collapsed,Visible}class Button {public Visibility Visibility;}
class Program {
 readonly CancellationTokenSource loginApplicationExit=new CancellationTokenSource();
 readonly SemaphoreSlim serverRefreshGate=new SemaphoreSlim(1,1);bool LoadLocal;FakeDispatcher Dispatcher=new FakeDispatcher();Factory serviceFactory=new Factory();
 readonly SemaphoreSlim connectionRequestGate=new SemaphoreSlim(1,1);
 readonly object sessionMaintenanceTimerGate=new object();
 readonly AccountSessionMaintenance accountSessionMaintenance=new AccountSessionMaintenance(()=>TimerStub.Now);
 Task pendingSessionLogout=Task.CompletedTask;static bool ExitCleanupStarted;int Renewals;bool RenewalFailure;
 Program(){accountSessionMaintenance.Begin(true);}
 bool IsUserLogin=true,loginPresentationActive,IsRememberChecked=true;long connectionRequestVersion;
 string lastLoginUsername,lastLoginPassword;Stopwatch loginUiStopwatch;
 Info gInfo=new Info();Controller serviceController=new Controller();Cache localResource=new Cache();
 TimerStub mainTimer=new TimerStub(),sessionMaintenanceTimer;LoginForm uCLogin=new LoginForm();ServerList uCServerList=new ServerList();Button btnSettings=new Button();
 Label txtUsername=new Label();Proxy proxifier=new Proxy();object Screen;Action Worker;string Error;
 void UnRegiserVpnService(){} void DisconnectAll(){Renewals++;if(RenewalFailure)throw new InvalidOperationException("renewal cleanup");}void HideLoading(){} void ShowMessage(string s){Error=s;} void ShowControl(object c){Screen=c;}
 void RunLoginWithPresentation(Action a){Worker=a;loginPresentationActive=true;}
 bool ProcessInfo(Account a,string password,bool renew,bool sessionValidated=false){gInfo.Password=password;if(!renew)BeginAccountSession(sessionValidated);IsUserLogin=true;return true;}
 object TryParseDeviceList(string s)=>null;void ShowDeviceLimitPopup(object d,string m){}
 static void Check(bool v,string m){if(!v)throw new Exception(m);Console.WriteLine("PASS "+m);}
'''
tests = r'''
 static async Task FinishLogout(Program p){if(await Task.WhenAny(p.pendingSessionLogout,Task.Delay(3000))!=p.pendingSessionLogout)throw new Exception("Session logout stalled");await p.pendingSessionLogout;}
 static async Task Main(){
 var list=new ServerList();list.PrepareServerChecksForLogin();
 Check(!list.probesPaused && !list.probesRequireDisconnect && !list.probeRestartRequested,"successful login resumes pending server before new services load");
 var p=new Program();
 Check(await p.ChangeAccountPasswordAsync("old","00123")==null,"200/st=true/code=0 accepted");
 Check(p.serviceController.Logins==0 && p.localResource.Password=="old","acceptance alone does not persist password");
 p.BeginPasswordChangeLogin("user","00123",false);
 Check(!p.IsUserLogin && ReferenceEquals(p.Screen,p.uCLogin) && p.Worker!=null,"immediate account verification screen");
 Check(p.uCLogin.Password=="00123" && !p.uCLogin.Remember,"new password and remember preference retained for retry");
 p.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;p.Worker();
 Check(p.uCServerList.Drained,"old scan drained before password login");
 Check(p.serviceController.Logins==1 && p.serviceController.AuthPassword=="00123","first login uses new password immediately");
 Check(p.localResource.Saves==0 && p.localResource.Password=="old" && p.uCLogin.Password=="00123","failed login preserves cache and retry credential");
 p.serviceController.Auth.StatusCode=HttpStatusCode.OK;
 Check(p.Login("user","00123",true) && p.localResource.Saves==1 && p.localResource.Password=="00123","successful login stores new password");
 var rejected=new Program();rejected.serviceController.Next.ResponseData.Code=32;
 Check(await rejected.ChangeAccountPasswordAsync("old","00123")!=null && rejected.Worker==null,"nonzero code does not initiate login");
 rejected.serviceController.Next.ResponseData.Code=0;rejected.serviceController.Next.StatusCode=HttpStatusCode.Forbidden;
 Check(await rejected.ChangeAccountPasswordAsync("old","00123")!=null,"non-200 response is rejected");
 rejected.serviceController.Next.ResponseData.Code=36;rejected.serviceController.Next.StatusCode=HttpStatusCode.Conflict;
 Check(await rejected.ChangeAccountPasswordAsync("old","00123")!=null && rejected.Worker==null && rejected.localResource.Saves==0,"409/code36 keeps session credentials and does not initiate login");
 var stale=new Program();stale.gInfo.Username="other";stale.BeginPasswordChangeLogin("user","00123",true);
 Check(stale.Worker==null,"do not log into stale account");
 var refreshRace=new Program();refreshRace.Dispatcher.Before=()=>{refreshRace.IsUserLogin=false;};
 refreshRace.ValidateSessionAndRefreshServerList();
 Check(refreshRace.localResource.Saves==0,"in-flight refresh ignored during re-login");
 var staleRefresh=new Program();staleRefresh.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;
 staleRefresh.Dispatcher.Before=()=>{staleRefresh.gInfo.Password="00123";};staleRefresh.ValidateSessionAndRefreshServerList();
 Check(staleRefresh.IsUserLogin && staleRefresh.localResource.Saves==0,"old password rejection cannot log out new session");
 var busy=new Program();busy.passwordChangeInProgress=true;
 Check(await busy.ChangeAccountPasswordAsync("old","00123")!=null && busy.serviceController.Changes==0,"duplicate mutation blocked");
 var exiting=new Program();exiting.loginApplicationExit.Cancel();
 Check(!exiting.Login("user","test",false) && exiting.Error==null,"shutdown cancellation is caught by production Login without touching a closing dispatcher");
 foreach(var failure in new Exception[]{new TimeoutException("Authentication flow timeout budget exhausted: flow=Login"),new InvalidOperationException("Initial: exit=56; recovery failed")}){
  var failed=new Program();failed.serviceController.Failure=failure;
  Check(!failed.Login("user","test",false) && failed.Error=="ارتباط با سرور برقرار نیست","deadline/technical failure never escapes Login or appears as technical UI text");
  failed.ValidateSessionAndRefreshServerList();
  Check(failed.IsUserLogin,"background Login2 caller catches network failure without logging out the session");
 }
 var canceledRefresh=new Program();canceledRefresh.loginApplicationExit.Cancel();canceledRefresh.ValidateSessionAndRefreshServerList();
 Check(canceledRefresh.IsUserLogin,"background Login2 caller catches exit cancellation");

 foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.NotAcceptable}){
  var invalid=new Program();var service=(ProbeService)invalid.gInfo.CurrentService;
  service.OnDisconnect=()=>Check(invalid.localResource.Removes==0,"stop account-bound service before clearing cached credentials");
  invalid.serviceController.Auth.StatusCode=code;invalid.ValidateSessionAndRefreshServerList();await FinishLogout(invalid);
  Check(!invalid.IsUserLogin && service.Disconnects==1 && invalid.localResource.Removes==1 && ReferenceEquals(invalid.Screen,invalid.uCLogin),"recognized HTTP rejection disconnects and returns to login exactly once");
 }
 var switched=new Program();var switchedService=(ProbeService)switched.gInfo.CurrentService;
 switched.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;switched.Dispatcher.Before=()=>switched.connectionRequestVersion++;
 switched.ValidateSessionAndRefreshServerList();await FinishLogout(switched);
 Check(!switched.IsUserLogin && switchedService.Disconnects==1,"same-account rejection survives a VPN connection-generation change");
 var hidden=new Program();bool apply=true;hidden.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;
 hidden.Dispatcher.Before=()=>apply=false;hidden.RefreshAuthenticatedServerList(()=>apply);await FinishLogout(hidden);
 Check(!hidden.IsUserLogin && hidden.localResource.Removes==1,"same-account rejection survives a stale requesting view");
 foreach(bool sameCredentials in new[]{false,true}){
  var replacement=new Program();replacement.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;
  replacement.Dispatcher.Before=()=>{replacement.EndAccountSession();if(!sameCredentials)replacement.gInfo.Username="other";replacement.BeginAccountSession(true);};
  replacement.ValidateSessionAndRefreshServerList();await FinishLogout(replacement);
  Check(replacement.IsUserLogin && replacement.localResource.Removes==0,"old rejection cannot end a new login, even with identical credentials");
 }
 var validSwitch=new Program();var live=validSwitch.gInfo.CurrentService;validSwitch.Dispatcher.Before=()=>validSwitch.connectionRequestVersion++;
 validSwitch.ValidateSessionAndRefreshServerList();
 Check(validSwitch.IsUserLogin && validSwitch.localResource.Saves==0 && ReferenceEquals(live,validSwitch.gInfo.CurrentService),"stale valid refresh cannot replace data or the active connection");
 foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.NotAcceptable,HttpStatusCode.NotFound,HttpStatusCode.InternalServerError}){
  var unknown=new Program();unknown.serviceController.Auth.StatusCode=code;unknown.serviceController.Auth.ResponseData.message=null;
  unknown.ValidateSessionAndRefreshServerList();
  Check(unknown.IsUserLogin && unknown.localResource.Removes==0,"HTML/empty/unrecognized HTTP errors and outages preserve the session");
 }
 foreach(string status in new[]{"Expired","Deleted"}){
  var removed=new Program();removed.serviceController.Auth.ResponseData.Decrypted.UserAccount.Status=status;
  removed.ValidateSessionAndRefreshServerList();await FinishLogout(removed);
  Check(!removed.IsUserLogin && removed.localResource.Removes>0,"recognized expired/deleted account ends the session");
 }
 var expiry=new Program();expiry.serviceController.Auth.ResponseData.Decrypted.UserAccount.ExpiryDate=DateTime.Now.AddDays(-1);
 expiry.ValidateSessionAndRefreshServerList();await FinishLogout(expiry);
 Check(!expiry.IsUserLogin && expiry.Renewals==1,"expired account retains the renewal logout path");
 var noShop=new Program();noShop.gInfo.settings=new Settings();noShop.serviceController.Auth.ResponseData.Decrypted.UserAccount.Status="Expired";
 noShop.ValidateSessionAndRefreshServerList();await FinishLogout(noShop);
 Check(!noShop.IsUserLogin && noShop.localResource.Removes>0,"actual renewal logout tolerates missing optional nested shop settings");
 var renewalFailure=new Program();renewalFailure.RenewalFailure=true;renewalFailure.serviceController.Auth.ResponseData.Decrypted.UserAccount.Status="Expired";
 renewalFailure.ValidateSessionAndRefreshServerList();await FinishLogout(renewalFailure);
 Check(renewalFailure.localResource.Removes==1 && ReferenceEquals(renewalFailure.Screen,renewalFailure.uCLogin),"optional renewal cleanup failure cannot prevent invalid-account logout");
 var outage=new Program();outage.serviceController.Auth.ResponseData.Decrypted.UserAccount.Status="AuthServerError";
 outage.serviceController.Auth.ResponseData.Decrypted.UserAccount.ExpiryDate=DateTime.Now.AddDays(-1);outage.ValidateSessionAndRefreshServerList();
 Check(outage.IsUserLogin && outage.localResource.Removes==0,"auth service outage is transient even with an old expiry field");
 var malformed=new Program();malformed.serviceController.Auth.ResponseData.Decrypted.groups=null;malformed.ValidateSessionAndRefreshServerList();
 Check(malformed.IsUserLogin && malformed.localResource.Removes==0,"unrecognized account data never terminates login");
 var unverified=new Program();unverified.serviceController.Auth.VerifiedServerList=false;unverified.serviceController.Auth.ResponseData.Decrypted.UserAccount.Status="Deleted";unverified.ValidateSessionAndRefreshServerList();
 Check(unverified.IsUserLogin && unverified.localResource.Removes==0,"mismatched encrypted request ID cannot revoke the current account");
 var missing=new Program();missing.gInfo.Password="";missing.ValidateSessionAndRefreshServerList();
 Check(missing.IsUserLogin && missing.serviceController.Logins==0 && missing.localResource.Removes==0,"missing local credentials are not server proof of invalidity");

 var drain=new Program();var drained=new TaskCompletionSource<bool>();drain.uCServerList.Drain=drained.Task;
 drain.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;drain.ValidateSessionAndRefreshServerList();
 Check(!drain.IsUserLogin && !drain.pendingSessionLogout.IsCompleted && drain.localResource.Removes==0 && drain.uCServerList.Paused,"rejection invalidates account immediately while canceled probes drain asynchronously");
 drained.SetResult(true);await FinishLogout(drain);
 Check(drain.localResource.Removes==1,"cache clears after canceled account work drains");
 var gateRace=new Program();var oldService=(ProbeService)gateRace.gInfo.CurrentService;gateRace.connectionRequestGate.Wait();
 gateRace.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;gateRace.ValidateSessionAndRefreshServerList();
 gateRace.gInfo.CurrentService=new ProbeService();gateRace.BeginAccountSession(true);gateRace.IsUserLogin=true;gateRace.connectionRequestGate.Release();await FinishLogout(gateRace);
 Check(gateRace.IsUserLogin && oldService.Disconnects==0 && gateRace.localResource.Removes==0,"queued old-account teardown cannot touch a replacement login");
 var startup=new Program();var pending=new IRSpeedyVPN.Services.TunnelPlusService();startup.gInfo.CurrentService=pending;
 var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();
 pending.OnDisconnect=()=>{entered.Set();if(!release.Wait(3000))throw new Exception("test startup release timeout");};
 startup.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;startup.ValidateSessionAndRefreshServerList();
 Check(entered.Wait(3000) && pending.Canceled && !startup.pendingSessionLogout.IsCompleted && startup.localResource.Removes==0,"startup is canceled before asynchronous disconnect; caller remains responsive");
 var nextConnection=startup.connectionRequestGate.WaitAsync();Check(!nextConnection.IsCompleted,"a second Core start cannot pass the connection gate during teardown");
 release.Set();await FinishLogout(startup);await nextConnection;startup.connectionRequestGate.Release();
 Check(startup.localResource.Removes==1,"startup cleanup finishes before credential removal");
 entered.Dispose();release.Dispose();
 var cleanupFailure=new Program();((ProbeService)cleanupFailure.gInfo.CurrentService).OnDisconnect=()=>{throw new InvalidOperationException("stop");};cleanupFailure.proxifier.Fail=true;
 cleanupFailure.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;cleanupFailure.ValidateSessionAndRefreshServerList();await FinishLogout(cleanupFailure);
 Check(cleanupFailure.localResource.Removes==1 && ReferenceEquals(cleanupFailure.Screen,cleanupFailure.uCLogin) && !cleanupFailure.pendingSessionLogout.IsFaulted,"cleanup errors are logged without retaining login or faulting an async logout");
 var drainFailure=new Program();var drainFailureService=(ProbeService)drainFailure.gInfo.CurrentService;drainFailure.uCServerList.Drain=Task.FromException(new InvalidOperationException("probe cleanup"));drainFailure.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;
 drainFailure.ValidateSessionAndRefreshServerList();await FinishLogout(drainFailure);
 Check(drainFailure.localResource.Removes==1 && drainFailureService.Disconnects==1,"failed probe drain cannot suppress service cleanup and logout");

 TimerStub.Now=0;var schedule=new Program();schedule.StartSessionMaintenance();var timer=schedule.sessionMaintenanceTimer;
 TimerStub.Now=29*60*1000;schedule.StartSessionMaintenance();
 Check(ReferenceEquals(timer,schedule.sessionMaintenanceTimer) && timer.DueAt==30*60*1000 && timer.Period==Timeout.Infinite,"reopening chooser at minute 29 retains minute-30 deadline and one timer");
 TimerStub.Now=30*60*1000;timer.Fire();Check(schedule.serviceController.Logins==1 && timer.DueAt==60*60*1000,"due request advances the deadline only when accepted");
 TimerStub.Now=0;var cache=new Program();cache.BeginAccountSession(false);cache.StartSessionMaintenance();
 Check(cache.sessionMaintenanceTimer.DueAt==0,"cached startup schedules immediate background validation");
 cache.sessionMaintenanceTimer.Fire();Check(cache.serviceController.Logins==1,"cached startup sends one authenticated validation request");
 TimerStub.Now=0;var contention=new Program();contention.BeginAccountSession(false);contention.StartSessionMaintenance();contention.serverRefreshGate.Wait();contention.sessionMaintenanceTimer.Fire();
 Check(contention.serviceController.Logins==0 && contention.sessionMaintenanceTimer.DueAt==60000 && contention.accountSessionMaintenance.DelayMilliseconds()==0,"busy gate retries after one minute without acquiring a fresh 30-minute deadline");
 contention.serverRefreshGate.Release();TimerStub.Now=60000;contention.sessionMaintenanceTimer.Fire();Check(contention.serviceController.Logins==1,"overdue validation resumes after gate contention");
 TimerStub.Now=0;var manual=new Program();manual.StartSessionMaintenance();TimerStub.Now=20*60*1000;manual.ValidateSessionAndRefreshServerList();
 Check(manual.serviceController.Logins==1 && manual.sessionMaintenanceTimer.DueAt==50*60*1000,"actual authenticated manual refresh shares and advances the same deadline");
 var stopped=new Program();stopped.StartSessionMaintenance();stopped.EndAccountSession();stopped.sessionMaintenanceTimer.Fire();
 Check(stopped.serviceController.Logins==0 && stopped.sessionMaintenanceTimer.DueAt==-1,"stopped account cannot issue requests from a queued timer callback");
 var queued=new Program();long oldGeneration=queued.accountSessionMaintenance.Generation;queued.EndAccountSession();queued.BeginAccountSession(true);
 queued.ValidateSessionAndRefreshServerList(oldGeneration);
 Check(queued.serviceController.Logins==0,"previous account timer cannot borrow the new login generation to issue an extra POST");
 var closing=new Program();closing.serviceController.Auth.StatusCode=HttpStatusCode.Forbidden;closing.Dispatcher.Before=()=>ExitCleanupStarted=true;
 closing.ValidateSessionAndRefreshServerList();ExitCleanupStarted=false;
 Check(closing.localResource.Removes==0 && closing.pendingSessionLogout.IsCompleted,"shutdown ignores late results without navigating the closing window");
 }
}
'''
with tempfile.TemporaryDirectory() as d:
 p=Path(d);(p/'Program.cs').write_text(prefix.replace('PREPARE_METHOD',prepare)+flow+login+refresh+logout+recharge+tests)
 (p/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>CS0649;CS0414</NoWarn></PropertyGroup></Project>')
 if len(sys.argv) in (4,5):
  dotnet,compiler,refs=sys.argv[1:4]
  subprocess.run([dotnet,compiler,'-nologo','-langversion:7.3','-target:exe','-out:'+str(p/'checks.dll')]+['-r:'+str(dll) for dll in Path(refs).glob('*.dll')]+[str(maintenance)]+[str(source) for source in p.glob('*.cs')],check=True)
  (p/'checks.runtimeconfig.json').write_text('{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
  subprocess.run([dotnet,str(p/'checks.dll')],check=True)
  if len(sys.argv)==5:
   subprocess.run([dotnet,compiler,'-nologo','-langversion:7.3','-target:library','-out:'+str(p/'checks-net48.dll')]+['-r:'+str(dll) for dll in Path(sys.argv[4]).glob('*.dll') if dll.name not in ['System.EnterpriseServices.Thunk.dll','System.EnterpriseServices.Wrapper.dll']]+[str(maintenance),str(p/'Program.cs')],check=True)
   print('PASS production session schedule, refresh, async teardown and password flows compile against .NET Framework 4.8 references')
 else:
  shutil_source=p/'AccountSessionMaintenance.cs';shutil_source.write_text(maintenance.read_text())
  subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Checks.csproj'),'-v:q'],check=True)
