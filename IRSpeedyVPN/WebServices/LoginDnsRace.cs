using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace IRSpeedyVPN.WebServices
{
    // DNS only: a completion-order race under one capped lookup clock. Every
    // provider is canceled and joined before returning, including on cancellation.
    internal static class LoginDnsRace
    {
        internal const int LookupBudgetMs = 5000;
        internal static string[] Resolve(string host, int timeoutMs, CancellationToken cancellation,
            Func<string, int, CancellationToken, string[]> query, Action<string> log,
            string[] providers, string[] names)
        {
            int budget = Math.Min(LookupBudgetMs, timeoutMs);
            var clock = Stopwatch.StartNew();
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 100) return new string[0];
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var replies = new BlockingCollection<ProviderReply>())
            {
                stop.CancelAfter(budget);
                for (int index = 0; index < providers.Length; index++)
                    Write(log, host, index, "started", 0, names);
                var workers = providers.Select((provider, index) => Task.Factory.StartNew(() =>
                {
                    var reply = new ProviderReply { Index = index, Outcome = "no-answer", Values = new string[0] };
                    try
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        int remaining = budget - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        if (remaining > 100)
                            reply.Values = (query(provider, remaining, stop.Token) ?? new string[0])
                                .Where(LoginDnsCache.IsPublicAddress).Distinct().ToArray();
                    }
                    catch (OperationCanceledException) { reply.Outcome = "canceled"; }
                    catch (Exception ex)
                    {
                        // Closing a losing socket can raise SocketException or
                        // ObjectDisposedException rather than OperationCanceledException.
                        reply.Outcome = stop.IsCancellationRequested ? "canceled"
                            : ex is TimeoutException ? "timeout" : "failed";
                    }
                    if (reply.Values.Length != 0) reply.Outcome = "valid-answer";
                    reply.ElapsedMs = clock.ElapsedMilliseconds;
                    replies.Add(reply);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                string[] result = new string[0];
                try
                {
                    for (int received = 0; received < workers.Length; received++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int remaining = budget - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        ProviderReply reply;
                        if (remaining <= 0 || !replies.TryTake(out reply, remaining, cancellation)) break;
                        if (reply.Values.Length != 0 && reply.ElapsedMs < budget)
                        {
                            Write(log, host, reply.Index, "winner", reply.ElapsedMs, names);
                            result = reply.Values; break;
                        }
                        Write(log, host, reply.Index, reply.Outcome, reply.ElapsedMs, names);
                    }
                }
                finally
                {
                    stop.Cancel(); Task.WaitAll(workers);
                    ProviderReply pending;
                    while (replies.TryTake(out pending))
                        Write(log, host, pending.Index, "joined-" + pending.Outcome, pending.ElapsedMs, names);
                }
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
        }
        private sealed class ProviderReply
        { internal int Index; internal string Outcome; internal string[] Values; internal long ElapsedMs; }
        private static void Write(Action<string> log, string host, int provider, string outcome, long elapsedMs, string[] names)
        {
            try { log?.Invoke("[LoginRecovery] host=" + host + " stage=provider provider="
                + names[provider] + " outcome=" + outcome + " elapsedMs=" + elapsedMs); }
            catch { }
        }
    }

}
