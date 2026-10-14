using System;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;

internal static class Program
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(10); }
        throw new Exception("Timed out waiting for test state");
    }
    static async Task Main()
    {
        var pools = new QueryAutoSelectorsResponse();
        var ai = new AutoSelectorStatus { Tag="ai-proxy", MembersQualified=1 };
        ai.Members.Add(new AutoSelectorMember { Qualified=true, LastOkMs=200, LastProbeMs=200 });
        pools.Groups.Add(ai);
        Check(!MainPoolRecoveryProof.Confirmed(pools, 100), "AI-only success claimed main recovery");
        var main = new AutoSelectorStatus { Tag="proxy", MembersQualified=1 };
        var member = new AutoSelectorMember { Qualified=true, LastOkMs=50, LastProbeMs=50 };
        main.Members.Add(member); pools.Groups.Add(main);
        Check(!MainPoolRecoveryProof.Confirmed(pools, 100), "Old healthy sample claimed recovery");
        member.LastOkMs=210; member.LastProbeMs=200;
        Check(!MainPoolRecoveryProof.Confirmed(pools, 100), "Successful local dial erased failed probe");
        member.LastOkMs=200;
        Check(MainPoolRecoveryProof.Confirmed(pools, 100), "Fresh qualified main proof was rejected");
        main.Suspended=true;
        Check(!MainPoolRecoveryProof.Confirmed(pools, 100), "Suspended main was declared recovered");
        Console.WriteLine("PASS recovery requires fresh qualified main-pool proof, independent of AI/direct routes");
        int calls = 0;
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var session = new NetworkRecoverySession(token =>
        {
            int call = Interlocked.Increment(ref calls);
            return call == 1 ? first.Task : call == 2 ? second.Task : third.Task;
        }, (ms, token) => retry.Task))
        {
            session.Observe(true);
            Check(calls == 0 && session.Status == ConnectionRecoveryStatus.None, "Routine online signal started health probes");
            session.Observe(false);
            Check(calls == 0 && session.Status == ConnectionRecoveryStatus.WaitingForInternet, "Probed while offline");
            session.Observe(true);
            await Until(() => calls == 1);
            session.Observe(true);
            Check(calls == 1 && session.Status == ConnectionRecoveryStatus.Recovering, "Duplicated pending recovery worker");
            session.Observe(false);
            first.SetResult(true);
            await Task.Delay(40);
            Check(session.Status == ConnectionRecoveryStatus.WaitingForInternet, "Stale success overwrote a new outage");
            session.Observe(true);
            await Until(() => calls == 2);
            second.SetResult(false);
            await Until(() => session.Status == ConnectionRecoveryStatus.Impaired);
            Check(session.Status != ConnectionRecoveryStatus.None, "Local acceptance/failure claimed recovery");
            retry.SetResult(true);
            await Until(() => calls == 3);
            third.SetResult(true);
            await Until(() => session.Status == ConnectionRecoveryStatus.None);
        }
        Console.WriteLine("PASS offline suspension, one worker, stale-result rejection and external proof before recovery");

        int afterDispose = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new NetworkRecoverySession(token => { Interlocked.Increment(ref afterDispose); return pending.Task; });
        old.Observe(false); old.Observe(true);
        await Until(() => afterDispose == 1);
        old.Dispose(); pending.SetResult(false);
        await Task.Delay(40);
        old.Observe(true);
        Check(old.Status == ConnectionRecoveryStatus.None && afterDispose == 1, "Disposed connection revived recovery/probes");
        Console.WriteLine("PASS disconnect invalidates pending recovery without waiting for it");
    }
}
