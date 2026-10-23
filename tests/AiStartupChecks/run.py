"""Exercise the production AI startup worker with a controllable Core transport.

Run: python tests/AiStartupChecks/run.py [path-to-dotnet]
Requires a .NET 8 SDK; no VPN, external network, or WPF runtime is used.
"""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/Services/TunnelPlusService.cs').read_text(encoding='utf-8-sig')
worker = source[source.index('        private void RecheckAiPoolAfterStart()'):
                source.index('        private void ReleaseAutoSelectorPlan()')]
fixture = r'''
using System; using System.Linq; using System.Collections.Generic;
using System.Diagnostics; using System.Threading; using System.Threading.Tasks;
using IRSpeedyVPN.Services.Libcore;
namespace IRSpeedyVPN.Services.Xray {
 class AutoSelectorPoolPlan { public const int AiActiveMemberLimit=8; public int AiMembers; }
}
namespace IRSpeedyVPN.Services.Libcore {
 class ErrorResp { public string Error=""; }
 class Status { public string Tag="ai-proxy"; public int RoundsCompleted; public bool Suspended; }
 class Response { public List<Status> Groups=new List<Status>(); }
 class LibcoreServiceClient {
  public static volatile int Queries, Rechecks, Rounds, Suspended, ThrowQuery, BlockingQuery;
  public static ManualResetEventSlim Release=new ManualResetEventSlim(false);
  public LibcoreServiceClient(string host,int port,int timeout) {}
  public Response QueryAutoSelectors(int timeout) {
   Interlocked.Increment(ref Queries);
   if(BlockingQuery!=0 && !Release.Wait(3000)) throw new TimeoutException();
   if(ThrowQuery!=0) throw new NotSupportedException();
   return new Response { Groups=new List<Status> { new Status { RoundsCompleted=Rounds, Suspended=Suspended!=0 } } };
  }
  public ErrorResp RecheckAutoSelector(string tag,int timeout) {
   if(tag!="ai-proxy" || timeout!=750) throw new Exception("Wrong recheck target/deadline");
   Interlocked.Increment(ref Rechecks); return new ErrorResp();
  }
  public static void Reset() { Queries=Rechecks=Rounds=Suspended=ThrowQuery=BlockingQuery=0; Release=new ManualResetEventSlim(false); }
 }
}
namespace IRSpeedyVPN.Services {
 class TunnelPlusService {
  Xray.AutoSelectorPoolPlan autoSelectorPlan;
  long connectionGeneration; volatile bool userCancelRequested; bool IsConnected=true;
  readonly object connectionLifecycleGate=new object(); const int CorePort=19810;
  public volatile int Errors;
  public TunnelPlusService(int members) { autoSelectorPlan=new Xray.AutoSelectorPoolPlan { AiMembers=members }; }
  void Diagnostic(string stage,string detail) { if(stage.EndsWith("unavailable")) Interlocked.Increment(ref Errors); }
  public void Start() { RecheckAiPoolAfterStart(); }
  public void Cancel() { userCancelRequested=true; Interlocked.Increment(ref connectionGeneration); }
  public void Replace() { lock(connectionLifecycleGate) { autoSelectorPlan=new Xray.AutoSelectorPoolPlan { AiMembers=9 }; Interlocked.Increment(ref connectionGeneration); } }
  public bool Connected => IsConnected;
'''
checks = r'''
 }
 class Program {
  static void Check(bool ok,string message) { if(!ok)throw new Exception(message); Console.WriteLine("PASS "+message); }
  static async Task Until(Func<bool> ready) { for(int i=0;i<400&&!ready();i++)await Task.Delay(10); if(!ready())throw new Exception("Worker did not reach expected state"); }
  static async Task Main() {
   LibcoreServiceClient.Reset(); new TunnelPlusService(8).Start(); await Task.Delay(150);
   Check(LibcoreServiceClient.Queries==0&&LibcoreServiceClient.Rechecks==0,"small AI pools keep only their native initial round");

   LibcoreServiceClient.Reset(); LibcoreServiceClient.BlockingQuery=1; var service=new TunnelPlusService(9);
   var clock=Stopwatch.StartNew(); service.Start();
   Check(clock.ElapsedMilliseconds<150,"startup returns without waiting for the AI worker or Core reply");
   await Until(()=>LibcoreServiceClient.Queries>0);
   Check(LibcoreServiceClient.Rechecks==0,"full sweep cannot preempt an unfinished initial round");
   LibcoreServiceClient.Rounds=1; LibcoreServiceClient.Release.Set();
   await Until(()=>LibcoreServiceClient.Rechecks==1); await Task.Delay(650);
   Check(LibcoreServiceClient.Rechecks==1&&service.Connected,"completed initial round buys one AI-only sweep and preserves the connection");

   LibcoreServiceClient.Reset(); LibcoreServiceClient.Rounds=1; LibcoreServiceClient.Suspended=1; service=new TunnelPlusService(9); service.Start();
   await Until(()=>LibcoreServiceClient.Queries>0); await Task.Delay(150);
   Check(LibcoreServiceClient.Rechecks==0,"suspended network does not start a full sweep");
   LibcoreServiceClient.Suspended=0; await Until(()=>LibcoreServiceClient.Rechecks==1); await Task.Delay(150);

   LibcoreServiceClient.Reset(); LibcoreServiceClient.BlockingQuery=1; service=new TunnelPlusService(9); service.Start();
   await Until(()=>LibcoreServiceClient.Queries>0); service.Cancel(); LibcoreServiceClient.Rounds=1; LibcoreServiceClient.Release.Set(); await Task.Delay(700);
   Check(LibcoreServiceClient.Rechecks==0,"disconnect cancels the pending background sweep");

   LibcoreServiceClient.Reset(); service=new TunnelPlusService(9); service.Start();
   await Until(()=>LibcoreServiceClient.Queries>0); service.Replace(); LibcoreServiceClient.Rounds=1; await Task.Delay(700);
   Check(LibcoreServiceClient.Rechecks==0,"old worker cannot recheck a replacement connection");

   LibcoreServiceClient.Reset(); LibcoreServiceClient.ThrowQuery=1; service=new TunnelPlusService(9); service.Start();
   await Until(()=>service.Errors==1);
   Check(LibcoreServiceClient.Rechecks==0&&service.Connected,"unsupported optional Core query leaves the VPN connected");
  }
 }
}
'''
with tempfile.TemporaryDirectory(prefix='ai-startup-checks-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(fixture + worker + checks, encoding='utf-8')
    (path / 'Checks.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<NoWarn>CS0649</NoWarn></PropertyGroup></Project>', encoding='utf-8')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet',
                    'run', '--project', str(path / 'Checks.csproj'), '-v:q'], check=True)
