// Production exit-routing methods are extracted by core_exit.py. Only the
// application context, reconnect action and listener PID lookup are inert.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;
namespace IRSpeedyVPN.Common
{
    internal static class CoreDiagnosticMetadata
    {
        internal static int ListeningPid;
        internal static int ListenerPid(int port) => ListeningPid;
    }
    internal static class VpnTimeZone { internal static void EndConnection(object service) { } }
}
namespace IRSpeedyVPN.Services
{
    internal sealed class FixtureInfo { internal TunnelPlusService CurrentService; }
    internal partial class TunnelPlusService
    {
        private readonly FixtureInfo gInfo;
        private bool IsConnected=true, userCancelRequested, suppressCoreExit;
        private long connectionGeneration;
        private readonly object connectionLifecycleGate=new object();
        private Process recoveryCoreProcess;
        private DateTime lastCoreStartUtc;
        private readonly Queue<DateTime> recentCoreExitsUtc=new Queue<DateTime>();
        private readonly object reconnectLock=new object();
        private const int CorePort=19810, MaxCoreExitsInWindow=10, CoreExitWindowSeconds=30, ImmediateExitSeconds=2;
        private static readonly object ownedExitGate=new object();
        private static Process ownedMainCoreProcess;
        private static DateTime ownedMainCoreStartedUtc;
        private static bool applicationExiting;
        internal readonly ConcurrentQueue<string> Decisions=new ConcurrentQueue<string>();
        internal int Reconnects, Disconnects;
        internal TunnelPlusService(FixtureInfo info) {gInfo=info;}
        private void DiagnosticExit(Process process) { }
        private void Diagnostic(string stage,string fields) => Decisions.Enqueue(stage+" "+fields);
        private void TryReconnect() => Interlocked.Increment(ref Reconnects);
        private void DisconnectLocked(bool check,bool silent,bool canceled) => Interlocked.Increment(ref Disconnects);
        internal void Subscribe(Process process) {process.Exited+=CoreProcess_Exited;}
        internal void Trigger(Process process) => CoreProcess_Exited(process,EventArgs.Empty);
        internal void Bind(Process process, DateTime started)
        {
            lock(ownedExitGate) {ownedMainCoreProcess=process;ownedMainCoreStartedUtc=started;}
            BindReusedCoreExitSource(CorePort);
        }
        internal bool BoundTo(Process process) => ReferenceEquals(recoveryCoreProcess,process);
        internal void Cancel() {userCancelRequested=true;}
        internal void Suppress() {suppressCoreExit=true;}
        internal void DisconnectState() {IsConnected=false;}
        internal void QueueThenChange(TunnelPlusService creator,Process process,Action change)
        {lock(connectionLifecycleGate){creator.Trigger(process);change();}}
        internal void NextGeneration() {Interlocked.Increment(ref connectionGeneration);}
        internal void ExhaustRetries() {for(int i=0;i<9;i++)recentCoreExitsUtc.Enqueue(DateTime.UtcNow);}
        internal void ExitApp(bool value) {applicationExiting=value;}
        internal DateTime StartTime => lastCoreStartUtc;
    }
    internal sealed class ResidentialFixture : TunnelPlusService
    {internal ResidentialFixture(FixtureInfo info):base(info) { }}
}
internal static class CoreExitChecks
{
    private static void Check(bool condition,string name)
    {if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private static async Task Until(Func<bool> condition)
    {var clock=Stopwatch.StartNew();while(!condition()){if(clock.ElapsedMilliseconds>2500)throw new Exception("exit callback did not finish");await Task.Delay(5);}}
    private static Process Start()
    {
        var process=new Process {StartInfo=new ProcessStartInfo("/bin/sleep","60") {UseShellExecute=false},EnableRaisingEvents=true};
        if(!process.Start())throw new Exception("fixture process failed to start");return process;
    }
    private static void Stop(Process process)
    {try{if(!process.HasExited)process.Kill();if(!process.WaitForExit(1500))throw new Exception("fixture process leaked");}finally{process.Dispose();}}
    private static TunnelPlusService Active(bool residential,FixtureInfo info) => residential
        ? (TunnelPlusService)new ResidentialFixture(info):new TunnelPlusService(info);
    private static async Task Main()
    {
        foreach(bool residential in new[]{false,true})
        {
            string tab=residential?"Residential":"VPN";
            var process=Start();
            try
            {
                var context=new FixtureInfo();var creator=new TunnelPlusService(context);creator.Cancel();creator.DisconnectState();
                var active=Active(residential,context);context.CurrentService=active;
                CoreDiagnosticMetadata.ListeningPid=process.Id;
                DateTime started=DateTime.UtcNow.AddSeconds(-5);active.Bind(process,started);creator.Subscribe(process);
                Check(active.BoundTo(process)&&active.StartTime==started,tab+" binds the reused app-owned listener and its actual launch time");
                process.Kill();Check(process.WaitForExit(1500),"real shared child process exits within its deadline");
                await Until(()=>active.Reconnects==1);
                Check(creator.Reconnects==0&&active.Disconnects==0,tab+" real Exited event recovers the active service despite a canceled/disconnected creator");
            }
            finally {Stop(process);}
            process=Start();
            try
            {
                var info=new FixtureInfo();var creator=new TunnelPlusService(info);var active=Active(residential,info);info.CurrentService=active;
                CoreDiagnosticMetadata.ListeningPid=process.Id;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));
                // A stale generation, cancellation and tab/service switches must
                // be checked again after the asynchronous callback takes its gate.
                active.QueueThenChange(creator,process,()=>active.NextGeneration());await Until(()=>active.Decisions.Count>0);
                Check(active.Reconnects==0,tab+" queued old-generation exit cannot revive a new connection");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));
                active.QueueThenChange(creator,process,()=>info.CurrentService=new TunnelPlusService(info));await Until(()=>active.Decisions.Count>0);
                Check(active.Reconnects==0,tab+" queued exit cannot recover a different active service");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));active.Cancel();creator.Trigger(process);await Until(()=>active.Decisions.Count>0);
                Check(active.Reconnects==0,tab+" manual disconnect never reconnects");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));active.Suppress();creator.Trigger(process);await Until(()=>active.Decisions.Count>0);
                Check(active.Reconnects==0,tab+" deliberately stopped Core exit stays suppressed");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));active.DisconnectState();creator.Trigger(process);await Until(()=>active.Decisions.Count>0);
                Check(active.Reconnects==0,tab+" idle connection cannot reconnect");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow);creator.Trigger(process);await Until(()=>active.Disconnects==1);
                Check(active.Reconnects==0,tab+" preserves the two-second immediate-exit stop policy");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));active.ExhaustRetries();creator.Trigger(process);await Until(()=>active.Disconnects==1);
                Check(active.Reconnects==0,tab+" preserves the ten-exits-in-thirty-seconds stop policy");
                active=Active(residential,info);info.CurrentService=active;CoreDiagnosticMetadata.ListeningPid=process.Id+1;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));creator.Trigger(process);await Until(()=>active.Decisions.Count>0);
                Check(!active.BoundTo(process)&&active.Reconnects==0,tab+" never adopts an external or mismatched listener");
                active=Active(residential,info);info.CurrentService=active;CoreDiagnosticMetadata.ListeningPid=process.Id;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));
                using(var unrelated=new Process()) {creator.Trigger(unrelated);await Until(()=>active.Decisions.Count>0);}
                Check(active.Reconnects==0,tab+" ignores auxiliary or stale process identity");
                active=Active(residential,info);info.CurrentService=active;active.Bind(process,DateTime.UtcNow.AddSeconds(-5));active.ExitApp(true);
                try {creator.Trigger(process);await Until(()=>active.Decisions.Count>0);Check(active.Reconnects==0,tab+" shutdown cannot revive the Core");}
                finally {active.ExitApp(false);}
                info.CurrentService=null;int decisions=creator.Decisions.Count;creator.Trigger(process);await Until(()=>creator.Decisions.Count>decisions);
                Check(creator.Reconnects==0,tab+" no active service means no recovery");
            }
            finally {Stop(process);}
        }
    }
}
