using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal static class InitialProbeScheduler
    {
        // One action owns its slot through alternate probing and resource cleanup.
        internal static void Run(IReadOnlyList<Action> jobs, int limit,
            Func<bool> cancelled, Action<Exception> failed)
        {
            if (limit < 1 || limit > 10) throw new ArgumentOutOfRangeException(nameof(limit));
            int next = -1;
            var workers = new Task[Math.Min(limit, jobs.Count)];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Factory.StartNew(() =>
                {
                    while (!cancelled())
                    {
                        int index = Interlocked.Increment(ref next);
                        if (index >= jobs.Count || cancelled()) return;
                        try { jobs[index](); }
                        catch (Exception ex) { failed(ex); }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            // Drains the whole category, including cancelled in-flight actions.
            Task.WaitAll(workers);
        }
    }
}
