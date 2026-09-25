"""Exercise production re-login methods with fake UI/network; no live credential changes."""
from pathlib import Path
import subprocess, sys, tempfile
root = Path(__file__).resolve().parents[2]
s = (root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
flow = s[s.index('        private void BeginPasswordChangeLogin('):s.index('        private void Logout_PreviewMouseDown',s.index('        private void BeginPasswordChangeLogin('))]
login = s[s.index('        bool Login('):s.index('        private List<DeviceInfo> TryParseDeviceList')]
refresh_source = (root/'IRSpeedyVPN/MainWindow.FastStartup.cs').read_text()
refresh = refresh_source[refresh_source.index('        private void ValidateSessionAndRefreshServerList()'):refresh_source.index('        private void FastStartup_Closing')]
list_source = (root/'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text(encoding='utf-8-sig')
prepare = list_source[list_source.index('        internal void PrepareServerChecksForLogin()'):list_source.index('        internal void ResumeServerChecksAfterCleanup()')]
assert 'uCServerList.PrepareServerChecksForLogin();\n                            ShowControl(uCServerList);' in s
prefix = r'''
using System; using System.Linq; using System.Threading; using System.Threading.Tasks; using System.Diagnostics; using System.Net;
class Info { public string Username="user",Password="old",ServerResponse; public object CurrentService=new object(),settings; public void Import(Account a,string p,string t){Password=p;} }
class Result { public bool IsSuccess; public int Code; public string ErrorMessage; }
class Response<T> { public HttpStatusCode StatusCode; public T ResponseData; }
class User { public string Username,Status="OK"; public DateTime? ExpiryDate; }
class Account { public User UserAccount=new User(); public object Settings,groups=new object(); }
class LoginData { public string data,message="denied",DecryptedString="account"; public Account Decrypted=new Account(); }
class Controller {
 public Response<Result> Next=new Response<Result>{StatusCode=HttpStatusCode.OK,ResponseData=new Result{IsSuccess=true,Code=0}};
 public Response<LoginData> Auth=new Response<LoginData>{StatusCode=HttpStatusCode.OK,ResponseData=new LoginData()};
 public string AuthPassword; public int Changes,Logins;
 public Response<Result> ChangePassword(string u,string o,string n){Changes++;return Next;}
 public Response<LoginData> Login2(string u,string p){Logins++;AuthPassword=p;return Auth;}
}
class TimerStub {public void Change(int a,int b){} }
class LoginForm { public string User,Password; public bool Remember; public void SetUserPassword(string u,string p,bool r){User=u;Password=p;Remember=r;} public void HideRenewMessage(){} }
class ServerList {
 public bool probesPaused=true;
 public IRSpeedyVPN.Services.CountryProbeSchedule probeSchedule=new IRSpeedyVPN.Services.CountryProbeSchedule();
 public ProbeTimer probeTimer=new ProbeTimer();
 PREPARE_METHOD
 public bool Drained; public Task DrainServerChecksAsync(){Drained=true;return Task.CompletedTask;} public void PauseServerChecks(){} public void RefreshServicesFromFactory(){} }
class ProbeTimer {public bool Stopped; public void Stop(){Stopped=true;} }
class Factory { public void RenewServiceList(object groups){} }
class FakeDispatcher { public Action Before;public void Invoke(Action action){Before?.Invoke();action();} }
class Label {public string Text;}
class Proxy {public void Detach(){} }
class Cache {public string TempPath="temp";public string Password="old";public int Saves;public void SaveConfig(Account a,string p){Saves++;Password=p;}public void RemoveConfig(){} }
class LogHelper {public static void WriteExLog(string s){} public static void WriteLog(Exception e){} }
class Program {
 int serverRefreshBusy;FakeDispatcher Dispatcher=new FakeDispatcher();Factory serviceFactory=new Factory();
 void LogoutInvalidSession(string m){IsUserLogin=false;}void RechareLogout(bool value){IsUserLogin=false;}
 bool IsExplicitSessionFailure(HttpStatusCode s)=>s==HttpStatusCode.Forbidden;
 bool IsUserLogin=true,loginPresentationActive,IsRememberChecked=true;long connectionRequestVersion;
 string lastLoginUsername,lastLoginPassword;Stopwatch loginUiStopwatch;
 Info gInfo=new Info();Controller serviceController=new Controller();Cache localResource=new Cache();
 TimerStub mainTimer=new TimerStub(),sessionMaintenanceTimer=new TimerStub();LoginForm uCLogin=new LoginForm();ServerList uCServerList=new ServerList();
 Label txtUsername=new Label();Proxy proxifier=new Proxy();object Screen;Action Worker;string Error;
 void UnRegiserVpnService(){} void DisconnectAll(){} void ShowMessage(string s){Error=s;} void ShowControl(object c){Screen=c;}
 void RunLoginWithPresentation(Action a){Worker=a;loginPresentationActive=true;}
 bool ProcessInfo(Account a,string password,bool renew){gInfo.Password=password;IsUserLogin=true;return true;}
 object TryParseDeviceList(string s)=>null;void ShowDeviceLimitPopup(object d,string m){}
 static void Check(bool v,string m){if(!v)throw new Exception(m);Console.WriteLine("PASS "+m);}
'''
tests = r'''
 static async Task Main(){
 var list=new ServerList();list.PrepareServerChecksForLogin();
 Check(!list.probesPaused && list.probeSchedule.Remaining(DateTime.UtcNow)==TimeSpan.Zero && list.probeTimer.Stopped,"successful login resumes pending country before new services load");
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
 }
}
'''
with tempfile.TemporaryDirectory() as d:
 p=Path(d);(p/'Program.cs').write_text(prefix.replace('PREPARE_METHOD',prepare)+flow+login+refresh+tests+(root/'IRSpeedyVPN/Services/CountryProbeSchedule.cs').read_text().replace('using System;', ''))
 (p/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><NoWarn>CS0649;CS0414</NoWarn></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Checks.csproj'),'-v:q'],check=True)
