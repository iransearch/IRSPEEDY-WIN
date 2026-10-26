"""Execute the actual presentation wrapper with a fake dispatcher during shutdown."""
from pathlib import Path
import subprocess, sys, tempfile

root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
wrapper = source[source.index('        private async void RunLoginWithPresentation('):source.index('        private void SetLoginStage(')]
# Await the same async-void body so a leaked shutdown exception fails the harness.
wrapper = wrapper.replace('private async void RunLoginWithPresentation', 'private async Task RunLoginWithPresentation')
prefix = r'''
using System; using System.Threading; using System.Threading.Tasks; using System.Diagnostics;
namespace System.Windows.Threading { enum DispatcherPriority { ContextIdle } }
enum Visibility { Visible, Collapsed }
class Element { public Visibility Visibility; }
class Box { public bool IsEnabled; public object Content; }
class DispatcherStub {
 public Action Before;
 public async Task InvokeAsync(Action action, System.Windows.Threading.DispatcherPriority priority) { await Task.Yield(); Before?.Invoke(); action(); }
}
class Loader : Element {
 public int Finishes, Fades;
 public void SetStage(int stage) { }
 public Task FinishStagesAsync(bool success) { Finishes++; return Task.CompletedTask; }
 public Task FadeOutAsync() { Fades++; return Task.CompletedTask; }
}
class LogHelper { public static void WriteLog(Exception error) { } }
class Program {
 readonly CancellationTokenSource loginApplicationExit = new CancellationTokenSource();
 bool loginPresentationActive, loginServerListPending, loginServerListPrepared, IsUserLogin;
 readonly Box TransitionBox = new Box(); readonly DispatcherStub Dispatcher = new DispatcherStub();
 readonly Loader uCLoginLoading = new Loader();
 readonly Element panelHeaderIcons = new Element(), btnSettings = new Element(), HeaderDivider = new Element(), txtVersion = new Element();
 readonly object uCLogin = new object(), uCServerList = new object(), uCUserInfo = new object();
 int Prepared, Hidden; string Error;
 void ShowMessage(string value) { if(loginApplicationExit.IsCancellationRequested) throw new Exception("closing dispatcher touched"); Error=value; }
 Task PrepareLoginResultAsync() { Prepared++; return Task.CompletedTask; }
 void HideLoading() { Hidden++; } void StartPostLoginChecks() { }
 static void Check(bool ok, string name) { if(!ok)throw new Exception(name); Console.WriteLine("PASS "+name); }
'''
tests = r'''
 static async Task Main() {
  var exiting = new Program();
  await exiting.RunLoginWithPresentation(() => { exiting.loginApplicationExit.Cancel(); throw new OperationCanceledException(); });
  Check(!exiting.loginPresentationActive && exiting.Error==null && exiting.Prepared==0 && exiting.uCLoginLoading.Finishes==0,
   "actual async presentation catches exit cancellation and skips closing-window UI");
  var rendering = new Program();
  rendering.Dispatcher.Before=()=>{rendering.loginApplicationExit.Cancel(); throw new OperationCanceledException();};
  await rendering.RunLoginWithPresentation(()=>throw new Exception("worker must not start"));
  Check(!rendering.loginPresentationActive && rendering.Error==null,
   "exit during the initial dispatcher render has no uncaught async-void exception");
  var waiting = new Program();
  var pending=waiting.RunLoginWithPresentation(()=>{});
  await Task.Delay(100); waiting.loginApplicationExit.Cancel(); await pending;
  Check(!waiting.loginPresentationActive && waiting.Prepared==0 && waiting.uCLoginLoading.Fades==0,
   "exit during minimum loading delay cancels cleanup before touching the closing window");
  var timeout = new Program(); var clock=Stopwatch.StartNew();
  await timeout.RunLoginWithPresentation(()=>throw new TimeoutException("Login network deadline expired. Initial: exit=28"));
  Check(timeout.Error=="ارتباط با سرور برقرار نیست" && clock.ElapsedMilliseconds>=3980 && timeout.Hidden==1 && !timeout.loginPresentationActive,
   "login budget expiry retains generic UI error and the full four-second minimum presentation");
 }
}
'''
dotnet, compiler, refs = sys.argv[1:]
with tempfile.TemporaryDirectory(prefix='login-ui-exit-') as directory:
    path = Path(directory)
    (path/'Program.cs').write_text(prefix+wrapper+tests)
    output = path/'checks.dll'
    subprocess.run([dotnet, compiler, '-nologo', '-langversion:7.3', '-target:exe', '-out:'+str(output)]
        + ['-r:'+str(dll) for dll in Path(refs).glob('*.dll')]+[str(path/'Program.cs')], check=True)
    (path/'checks.runtimeconfig.json').write_text('{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
    subprocess.run([dotnet, str(output)], check=True)
