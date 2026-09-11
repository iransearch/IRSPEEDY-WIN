using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    internal sealed class InitialCountryTest
    {
        internal readonly Action<Action<long>>[] Servers;
        internal readonly Action<long> Progress;
        internal readonly Action Completed;
        private long best = long.MaxValue;

        internal InitialCountryTest(IEnumerable<Action<Action<long>>> servers,
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
        // Snapshot ordering: A1, B1, C1, A2, B2, C2, ... . Each server action
        // includes its conditional retry, so it must return before the next starts.
        internal static void Run(IReadOnlyList<InitialCountryTest> countries,
            Func<bool> cancelled, Action<Exception> failed)
        {
            foreach (var country in countries.Where(c => c.Servers.Length == 0))
            {
                if (cancelled()) return;
                country.Completed();
            }

            int rounds = countries.Select(c => c.Servers.Length).DefaultIfEmpty(0).Max();
            for (int round = 0; round < rounds; round++)
            {
                foreach (var country in countries)
                {
                    if (cancelled()) return;
                    if (round >= country.Servers.Length) continue;
                    try
                    {
                        country.Servers[round](latency =>
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
                    if (round == country.Servers.Length - 1)
                        country.Completed();
                }
            }
        }
    }
