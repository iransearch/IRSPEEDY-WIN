// Execute the production startup worker against controllable RPC/lifecycle boundaries.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Services
{
    internal partial class TunnelPlusService
    {
        internal object autoSelectorPlan = new object();
        internal bool userCancelRequested, IsConnected = true;
        internal long connectionGeneration = 1;
        internal readonly object connectionLifecycleGate = new object();
        internal readonly Global gInfo = new Global();
        internal int CorePort = 19810;
        internal readonly List<string> Events = new List<string>();
        internal TunnelPlusService() { gInfo.CurrentService = this; }
        internal Task Run(CancellationToken token) => RecheckResidentialStartupPoolAsync(1, token);
        private void Diagnostic(string stage, string fields) => Events.Add(stage + " " + fields);
    }
    internal sealed class Global { internal object CurrentService; }
}
namespace IRSpeedyVPN.Residential { internal sealed class ResidentialService : TunnelPlusService { } }
namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class ErrorResp { internal string Error; }
    internal sealed class LibcoreServiceClient
    {
        internal static Func<QueryAutoSelectorsResponse> OnQuery;
        internal static int Queries, Actions;
        internal static string Tag, Error;
        internal static bool ThrowAction;
        internal LibcoreServiceClient(string host, int port, int timeout) { }
        internal QueryAutoSelectorsResponse QueryAutoSelectors(int timeout, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Queries++; return OnQuery(); }
        internal ErrorResp RecheckAutoSelector(string tag, int timeout)
        { Tag=tag;Actions++;if(ThrowAction)throw new InvalidOperationException();return new ErrorResp{Error=Error}; }
    }
}
internal static class StartupRecoveryChecks
{
    private static AutoSelectorStatus Failed() => new AutoSelectorStatus { Tag="proxy", MembersTotal=1, MembersProbed=1, RoundsCompleted=1 };
    private static QueryAutoSelectorsResponse Response(AutoSelectorStatus status)
    { var response=new QueryAutoSelectorsResponse();if(status!=null)response.Groups.Add(status);return response; }
    private static void Reset(Func<AutoSelectorStatus> status)
    { LibcoreServiceClient.Queries=LibcoreServiceClient.Actions=0;LibcoreServiceClient.Tag=LibcoreServiceClient.Error=null;LibcoreServiceClient.ThrowAction=false;LibcoreServiceClient.OnQuery=()=>Response(status()); }
    private static void Check(bool condition, string name)
    { if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name); }
    private static async Task Main()
    {
        var row=new IRSpeedyVPN.Residential.ResidentialService();Reset(Failed);
        await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==1&&LibcoreServiceClient.Queries==2&&LibcoreServiceClient.Tag=="proxy", "failed Residential initial round rechecks only main proxy exactly once");
        Check(row.Events.Exists(e=>e.Contains("accepted=True")&&e.Contains("attempts=1")), "accepted startup recovery is logged without server links");
        var healthy=Failed();healthy.MembersAlive=1;Reset(()=>healthy);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"healthy main pool never rechecks");
        healthy.MembersAlive=0;healthy.MembersQualified=1;Reset(()=>healthy);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"qualified main pool never rechecks");
        Reset(Failed);await new TunnelPlusService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Queries==0&&LibcoreServiceClient.Actions==0,"ordinary VPN worker remains untouched");
        var suspended=Failed();suspended.Suspended=true;Reset(()=>suspended);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"suspended main pool is left to existing network recovery");
        Reset(()=>null);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"missing selector status never implies a dead pool");
        var incomplete=Failed();incomplete.MembersProbed=0;Reset(()=>incomplete);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"incomplete round cannot trigger a recheck");
        int reads=0;var pending=Failed();pending.RoundsCompleted=0;pending.ProbesInFlight=1;
        Reset(()=>++reads==1?pending:Failed());await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Queries==3&&LibcoreServiceClient.Actions==1,"startup waits for the native initial probes instead of canceling them");
        reads=0;healthy.MembersAlive=1;Reset(()=>++reads==1?Failed():healthy);await new IRSpeedyVPN.Residential.ResidentialService().Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"main pool healing during grace is not rechecked");
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(Failed);
        using(var cancel=new CancellationTokenSource()) { cancel.Cancel();await row.Run(cancel.Token); }
        Check(LibcoreServiceClient.Queries==0,"canceled connection starts no RPC");
        row=new IRSpeedyVPN.Residential.ResidentialService();
        using(var cancel=new CancellationTokenSource()) {
            Reset(()=>{cancel.Cancel();return Failed();});await row.Run(cancel.Token);
            Check(LibcoreServiceClient.Actions==0,"disconnect during grace cancels the worker without a recheck");
        }
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(()=>{Interlocked.Increment(ref row.connectionGeneration);return Failed();});await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"stale generation cannot recheck a replacement connection");
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(()=>{row.autoSelectorPlan=new object();return Failed();});await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"replaced plan cannot be rechecked by the old worker");
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(()=>{row.gInfo.CurrentService=new object();return Failed();});await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0,"another active service cannot receive the old worker action");
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(Failed);LibcoreServiceClient.ThrowAction=true;await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==1&&row.Events.Exists(e=>e.Contains("unavailable")),"unsupported recheck stops after one action and logs its exception type");
        row=new IRSpeedyVPN.Residential.ResidentialService();Reset(Failed);LibcoreServiceClient.Error="unsupported";await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==1&&row.Events.Exists(e=>e.Contains("accepted=False")),"rejected recovery does not loop or claim success");
        var blockedRow=new IRSpeedyVPN.Residential.ResidentialService();Reset(Failed);
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
            var owner=new Thread(()=>{lock(blockedRow.connectionLifecycleGate){entered.Set();release.Wait();}});
            owner.Start();entered.Wait();var work=blockedRow.Run(CancellationToken.None);
            try { Check(await Task.WhenAny(work,Task.Delay(3000))==work,"busy lifecycle gate never strands the background worker");await work; }
            finally { release.Set();if(!owner.Join(2000))throw new Exception("test lock owner leaked"); }
            Check(LibcoreServiceClient.Actions==0,"config replacement owns the gate and suppresses stale recovery");
        }
        Reset(()=>pending);var deadline=System.Diagnostics.Stopwatch.StartNew();
        row=new IRSpeedyVPN.Residential.ResidentialService();await row.Run(CancellationToken.None);
        Check(LibcoreServiceClient.Actions==0&&deadline.ElapsedMilliseconds>=19000&&deadline.ElapsedMilliseconds<23000
            &&row.Events.Exists(e=>e.Contains("budget-expired")),"pending startup round stops at the 20-second budget without canceling native probes or rechecking");
        using(var cancel=new CancellationTokenSource(100)) {
            Reset(()=>pending);await new IRSpeedyVPN.Residential.ResidentialService().Run(cancel.Token);
            Check(LibcoreServiceClient.Actions==0,"cancellation releases a waiting worker before its first query");
        }
    }
}
