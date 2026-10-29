using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;

internal sealed partial class RpcOperationProbe
{
    private Process coreProcess, recoveryCoreProcess;
    private bool coreOwned,userCancelRequested;
    private long connectionGeneration=1;
    private readonly object diagnosticOutputLock=new object();
    private string diagnosticConnectionId=new string('a',32),telemetryOutputCategory="unknown",telemetryOutputReason="unknown";
    private long telemetryOutputAt,telemetryOutputGeneration=-1;
    private sealed class Plan {internal int MainMembers=3,AiMembers=2;}
    private Plan autoSelectorPlan=new Plan();
    private int CorePort=19810;
    private const int CoreConnectTimeoutMs=8000,CoreConnectRetryDelayMs=200,CoreStartDeadlineMs=180;
    private string diagnosticXrayConfigId;
    private System.Threading.CancellationToken GetStartCancellation()=>System.Threading.CancellationToken.None;
    private bool PauseSharingBeforeCoreRestart()=>true;
    private void RecoverCoreAfterFailedStart(){}
    private void TryStopCore(){}
    private void DiagnosticStartFailure(string stage,string message,string phase){}
    internal bool Startup(int port){CorePort=port;string error;return StartupHarness("CONFIG SECRET",out error);}
    internal int EnsureCalls;
    internal bool FailEnsure;
    private void Diagnostic(string name,string fields="") { }
    private void EnsureCoreRunning(int port,ref Process process,ref bool owned)
    {EnsureCalls++;if(FailEnsure)throw new InvalidOperationException("SECRET");}
    internal T Run<T>(Func<T> call,Action<Exception> context=null,Action<Exception> recovered=null) => ExecuteCoreCall(client=>call(),"start",context,recovered);
    internal void Snapshot(Exception failure,CoreRuntimeIdentity identity)
    {
        using(var process=Process.GetCurrentProcess())
        {recoveryCoreProcess=process;AnnotateCoreStartup(failure,identity,"SECRET CONFIG","SECRET XRAY",true,"start");}
        recoveryCoreProcess=null;
    }
}
internal static class OperationChecks
{
    internal static void Run()
    {
        LogHelper.Captured.Clear();var probe=new RpcOperationProbe();int calls=0;
        var first=new TimeoutException("SECRET",new System.IO.IOException("SECRET",new System.Net.Sockets.SocketException(10004)));new CoreRpcTrace("start",30000){Stage="read-header"}.Attach(first,"deadline");
        int result=probe.Run(()=>{calls++;CoreRpcChecks.Require(LogHelper.Captured.Count==0,"no premature Sentry event while retry is still unresolved");if(calls==1)throw first;return 7;});
        CoreRpcChecks.Require(result==7&&calls==2&&probe.EnsureCalls==1&&LogHelper.Captured.Count==1,"actual retry wrapper preserves one retry and produces one recovered report");
        var recovered=LogHelper.Captured[0];CoreRpcChecks.Require(recovered.Tags["core.outcome"]=="retry-succeeded"&&recovered.Level==Sentry.SentryLevel.Warning&&recovered.Tags["core.attempts"]=="2","recovered report is a handled warning after the second result");
        CoreRpcChecks.Require(recovered.Tags.ContainsKey("core.attempt1_ms")&&recovered.Tags.ContainsKey("core.attempt2_ms")&&recovered.Tags.ContainsKey("core.retry_prepare_ms"),"both attempt timings and the retry preparation are retained");
        LogHelper.Captured.Clear();probe=new RpcOperationProbe();calls=0;Exception failure=null;
        try {probe.Run<int>(()=>{if(++calls==1)throw first;throw new TimeoutException("SECRET");});}catch(Exception ex){failure=ex;}
        CoreRpcChecks.Require(calls==2&&LogHelper.Captured.Count==0,"failed retry is not captured prematurely before the caller records the terminal outcome");
        var clean=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
        CoreRpcChecks.Require(clean.Tags["core.outcome"]=="retry-failed"&&clean.Tags["core.first_rpc_stage"]=="read-header"&&clean.Tags["core.first_close_reason"]=="deadline","final exception preserves initial timeout stage and reason");
        CoreRpcChecks.Require(clean.Tags["core.first_native_error"]==((System.Net.Sockets.SocketException)first.InnerException.InnerException).NativeErrorCode.ToString()&&clean.Tags["core.first_hresult"].Length==8,"final failure retains the initial native socket code as well as its timeout HRESULT");
        CoreRpcChecks.Require(clean.Tags["core.operation_id"]!=recovered.Tags["core.operation_id"],"consecutive operations never share request state");
        var callbackProbe=new RpcOperationProbe();int callbackCalls=0;
        int unaffected=callbackProbe.Run(()=>{if(++callbackCalls==1)throw first;return 9;},recovered:ex=>{throw new Exception("telemetry callback");});
        CoreRpcChecks.Require(unaffected==9&&callbackCalls==2,"a telemetry callback exception cannot alter a successful RPC result");
        var deferredProbe=new RpcOperationProbe();int deferredCalls=0;Exception deferred=null;
        deferredProbe.Run(()=>{if(++deferredCalls==1)throw new TimeoutException("SECRET");return new ErrorResp{Error="SECRET config error"};},recovered:ex=>deferred=ex);
        CoreRpcChecks.Require(deferred!=null&&LogHelper.Captured.Count==0,"startup can defer a recovered RPC report until the response payload is validated");
        probe=new RpcOperationProbe();calls=0;failure=null;
        try{probe.Run<int>(()=>{if(++calls==1)throw first;throw new OperationCanceledException();});}catch(Exception ex){failure=ex;}
        CoreRpcChecks.Require(failure is OperationCanceledException&&calls==2&&LogHelper.Captured.Count==0,"user cancellation during retry remains cancellation with no spurious error event");
        probe=new RpcOperationProbe{FailEnsure=true};calls=0;failure=null;
        try{probe.Run<int>(()=>{calls++;throw first;});}catch(Exception ex){failure=ex;}
        clean=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
        CoreRpcChecks.Require(calls==1&&clean.Tags["core.attempts"]=="1"&&clean.Tags["core.outcome"]=="retry-failed","retry preparation failure never invents a second RPC attempt");
        probe=new RpcOperationProbe();failure=new TimeoutException("SECRET");
        probe.Snapshot(failure,new CoreRuntimeIdentity{Path="C:/SECRET/private",Pid=42,Version="1.13.0",Commit=new string('b',40),Sha256=new string('c',64),Architecture="windows/amd64"});
        clean=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
        CoreRpcChecks.Require(clean.Tags["core.process_state"]=="alive"&&clean.Tags["core.runtime_sha256"].Length==64&&clean.Tags["core.main_members"]=="3","actual startup annotator snapshots process, runtime identity and pool counts");
        CoreRpcChecks.Require(clean.Tags["core.runtime_pid"]=="42"&&clean.Tags.ContainsKey("core.runtime_matches_process"),"runtime snapshot identity is distinct from a possibly replaced current process");
        CoreRpcChecks.Require(!clean.Tags.Values.Any(v=>v.Contains("SECRET")),"startup snapshot cannot export executable path or raw core/Xray configurations");
        probe=new RpcOperationProbe();calls=0;failure=null;
        try{probe.Run<int>(()=>{calls++;throw new System.IO.IOException("SECRET");});}catch(Exception ex){failure=ex;}
        clean=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
        CoreRpcChecks.Require(calls==1&&probe.EnsureCalls==0&&clean.Tags["core.outcome"]=="failed","telemetry does not add retries for ordinary transport failures");
        StartupWire(false,false).GetAwaiter().GetResult();
        StartupWire(true,false).GetAwaiter().GetResult();
        StartupWire(true,true).GetAwaiter().GetResult();
    }
    private static async Task StartupWire(bool retry,bool accept)
    {
        LogHelper.Captured.Clear();var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        try
        {
            int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;var probe=new RpcOperationProbe();
            var operation=Task.Run(()=>probe.Startup(port));
            int count=retry?5:3;
            for(int i=0;i<count;i++)
                using(var peer=await listener.AcceptTcpClientAsync())
                {
                    var stream=peer.GetStream();var header=await CoreRpcChecks.ReadFrame(stream);await CoreRpcChecks.ReadFrame(stream);
                    string expected=i==0?"IsPrivileged":i==1||i==3?"Stop":"Start";
                    CoreRpcChecks.Require(System.Text.Encoding.UTF8.GetString(header).Contains("LibcoreService."+expected),"actual startup caller sends the expected identity/Stop/Start RPC sequence");
                    if(retry&&i==2)
                    {
                        var closed=stream.ReadAsync(new byte[1],0,1);
                        CoreRpcChecks.Require(await Task.WhenAny(closed,Task.Delay(1500))==closed&&await closed==0,"first startup timeout closes its socket before retry");
                        CoreRpcChecks.Require(LogHelper.Captured.Count==0,"first startup timeout is not reported before the second payload arrives");
                        continue;
                    }
                    byte[] response;
                    if(i==count-1&&!accept)
                    {
                        var error=System.Text.Encoding.UTF8.GetBytes("permission denied SECRET https://private.invalid");
                        var body=new byte[]{0x0a,(byte)error.Length}.Concat(error).ToArray();
                        response=new byte[]{2,0x18,(byte)body.Length,(byte)body.Length}.Concat(body).ToArray();
                    }
                    else response=new byte[]{0,0};
                    await stream.WriteAsync(response,0,response.Length);
                }
            CoreRpcChecks.Require(await Task.WhenAny(operation,Task.Delay(2000))==operation&&await operation==accept,"actual startup response validation preserves success/failure behavior");
            CoreRpcChecks.Require(LogHelper.Captured.Count==1,"startup sends exactly one report for the final retry/payload outcome");
            var report=LogHelper.Captured[0];
            CoreRpcChecks.Require(report.Level==(accept?Sentry.SentryLevel.Warning:Sentry.SentryLevel.Error)
                &&report.Tags["core.outcome"]==(accept?"retry-succeeded":retry?"retry-failed":"failed"),"a rejected Start payload never becomes a recovered-success warning");
            CoreRpcChecks.Require(!report.Tags.Values.Any(v=>v.Contains("SECRET"))&&!report.SentryExceptions.Any(e=>e.Value.Contains("SECRET")),"actual rejected RPC payload cannot leak its error/configuration content");
            if(!accept)CoreRpcChecks.Require(report.Tags["core.rpc_stage"]=="server-error"&&report.Tags["core.failure_reason"]=="socket-access-denied","rejected payload reports a fixed actionable cause without its raw message");
        }
        finally{listener.Stop();}
    }

}

namespace IRSpeedyVPN.Common {internal static class CoreDiagnosticMetadata {internal static int ListenerPid(int port)=>-1;}}
namespace Traffic {internal sealed class TrafficUsageService {internal static readonly TrafficUsageService Instance=new TrafficUsageService();internal void StopSession(){}internal void StartSession(Func<int,QueryConnectionsResponse> reader){}}}
