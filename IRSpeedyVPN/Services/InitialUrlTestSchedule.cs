using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal sealed class InitialCountryTest
    {
        internal readonly Action<int, Action<long>>[] Servers;
        internal readonly Action<long> Progress;
        internal readonly Action Completed;
        private long best = long.MaxValue;

        internal InitialCountryTest(IEnumerable<Action<int, Action<long>>> servers,
            Action<long> progress, Action completed)
        {
            Servers = servers.ToArray();
            Progress = progress;
            Completed = completed;
        }

        internal void Report(long latency)
        {
            if (latency <= 0 || latency >= best) return;
            best = latency;
            Progress(latency);
        }
    }

    internal static class InitialUrlTestSchedule
    {
        internal const int MaxConcurrentCountries = 5;
        private static readonly object BatchGate = new object();

        internal static void Drain()
        {
            lock (BatchGate) { }
        }

        // Five stable worker slots refill within the current round. A round barrier
        // keeps every country's second member behind ALL first members and retries.
        internal static void Run(IReadOnlyList<InitialCountryTest> countries,
            Func<bool> cancelled, Action<Exception> failed, Action cleanup = null,
            int maxConcurrency = MaxConcurrentCountries)
        {
            if (maxConcurrency < 1 || maxConcurrency > MaxConcurrentCountries)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            lock (BatchGate)
            {
                try { RunRounds(countries, cancelled, failed, maxConcurrency); }
                finally { cleanup?.Invoke(); }
            }
        }

        private static void RunRounds(IReadOnlyList<InitialCountryTest> countries,
            Func<bool> cancelled, Action<Exception> failed, int maxConcurrency)
        {
            foreach (var country in countries.Where(c => c.Servers.Length == 0))
            {
                if (cancelled()) return;
                country.Completed();
            }
            int rounds = countries.Select(c => c.Servers.Length).DefaultIfEmpty(0).Max();
            for (int round = 0; round < rounds; round++)
            {
                if (cancelled()) return;
                var eligible = countries.Where(c => round < c.Servers.Length).ToArray();
                int next = -1;
                int currentRound = round;
                var workers = Enumerable.Range(0, Math.Min(maxConcurrency, eligible.Length))
                    .Select(slot => Task.Factory.StartNew(() =>
                {
                    while (!cancelled())
                    {
                        int index = Interlocked.Increment(ref next);
                        if (index >= eligible.Length || cancelled()) return;
                        var country = eligible[index];
                        try
                        {
                            country.Servers[currentRound](slot, latency =>
                            {
                                if (!cancelled()) country.Report(latency);
                            });
                        }
                        catch (Exception ex)
                        {
                            if (cancelled()) return;
                            failed(ex);
                        }
                        if (cancelled()) return;
                        if (currentRound == country.Servers.Length - 1)
                            country.Completed();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                // Drain even after cancellation/errors before reusing slots or resources.
                Task.WaitAll(workers);
            }
        }
    }
}
