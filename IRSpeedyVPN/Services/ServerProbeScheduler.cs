using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal static class ServerProbeScheduler
    {
        internal const int Capacity = 8;
        private const int RetryDelayMs = 250;
        private const int MaxConsecutiveLocalFailures = 3;
        private const int SatisfiedLatencyMs = 300;
        private static readonly SemaphoreSlim Slots = new SemaphoreSlim(Capacity, Capacity);

        private sealed class Row
        {
            internal ServerRefreshPlan.Country Country;
            internal int Running;
            internal bool Satisfied;
            internal ServerRefreshPlan.Candidate Winner;
            internal ServerProbeMeasurement Best;
            internal readonly Dictionary<Url, long> Measurements = new Dictionary<Url, long>();
            internal readonly Dictionary<ServerRefreshPlan.Candidate, Task> Deferred =
                new Dictionary<ServerRefreshPlan.Candidate, Task>();
        }

        private sealed class Flight
        {
            internal Row Row;
            internal ServerRefreshPlan.Candidate Candidate;
            internal Task<ServerProbeMeasurement?> Work;
        }

        internal static async Task RunAsync(IEnumerable<ServerRefreshPlan.Country> plan,
            Func<ServerRefreshPlan.Candidate, CancellationToken, Task<ServerProbeMeasurement>> probe,
            Action<IVPNService, Url, IReadOnlyDictionary<Url, long>> complete,
            CancellationToken cancellation, Func<int, CancellationToken, Task> delay = null)
        {
            var rows = plan.Select(country => new Row { Country = country }).ToList();
            var running = new List<Flight>(Capacity);
            var cursor = 0;
            delay = delay ?? Task.Delay;
            // Only this loop publishes results or admits work. Slots are held
            // until the worker has stopped and released its native resources.
            try
            {
                while (rows.Count > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    foreach (var finished in running.Where(f => f.Work.IsCompleted).ToArray())
                    {
                        var attempt = await finished.Work.ConfigureAwait(false);
                        cancellation.ThrowIfCancellationRequested();
                        running.Remove(finished);
                        var row = finished.Row;
                        var candidate = finished.Candidate;
                        row.Running--;
                        var result = attempt.GetValueOrDefault();
                        if (result.Measured)
                            candidate.ConsecutiveLocalFailures = 0;
                        else if (attempt.HasValue && ++candidate.ConsecutiveLocalFailures >= MaxConsecutiveLocalFailures)
                            result = ServerProbeMeasurement.Failed;

                        if (!result.Measured)
                        {
                            if (!row.Satisfied)
                            {
                                row.Deferred[candidate] = delay(RetryDelayMs, cancellation);
                                row.Country.Pending.Add(candidate);
                            }
                            continue;
                        }

                        foreach (var owner in candidate.Owners)
                            row.Measurements[owner.Url] = result.Latency;
                        if (result.Successful && (row.Winner == null ||
                            ServerRefreshPlan.Compare(candidate, result, row.Winner, row.Best) < 0))
                        {
                            row.Winner = candidate;
                            row.Best = result;
                        }
                        if (result.Successful && result.Latency < SatisfiedLatencyMs)
                        {
                            row.Satisfied = true;
                            row.Country.Pending.Clear();
                            row.Deferred.Clear();
                        }
                    }

                    foreach (var row in rows.Where(r => r.Running == 0 && r.Country.Pending.Count == 0).ToArray())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        complete(row.Country.Service, row.Winner?.Owners[0].Url, row.Measurements);
                        rows.Remove(row);
                    }
                    if (rows.Count == 0) return;

                    int active = rows.Count(r => !r.Satisfied);
                    int cap = active == 0 ? 0 : (Capacity + active - 1) / active;
                    var wait = new List<Task>();
                    int examined = 0;
                    while (running.Count < Capacity && examined < rows.Count)
                    {
                        cursor %= rows.Count;
                        var row = rows[cursor++];
                        examined++;
                        if (row.Running >= cap || row.Satisfied) continue;
                        var candidate = row.Country.Pending.FirstOrDefault(c =>
                            !row.Deferred.TryGetValue(c, out var cooldown) || cooldown.IsCompleted);
                        if (candidate == null) continue;
                        cancellation.ThrowIfCancellationRequested();
                        row.Country.Pending.Remove(candidate);
                        row.Deferred.Remove(candidate);
                        row.Running++;
                        running.Add(new Flight { Row = row, Candidate = candidate, Work = ProbeAsync(candidate, probe, cancellation) });
                        examined = 0;
                        // A synchronous (or already completed) result may satisfy
                        // the row before we admit the next candidate.
                        if (running[running.Count - 1].Work.IsCompleted) break;
                    }
                    wait.AddRange(running.Select(f => (Task)f.Work));
                    if (running.Count < Capacity)
                        wait.AddRange(rows.Where(row => !row.Satisfied && row.Running < cap)
                            .SelectMany(row => row.Deferred.Values).Where(task => !task.IsCompleted));
                    if (wait.Count > 0) await Task.WhenAny(wait).ConfigureAwait(false);
                }
            }
            finally
            {
                // Drain before a connection/rebind can reuse the probe resources.
                try { await Task.WhenAll(running.Select(f => f.Work)).ConfigureAwait(false); }
                catch { /* The scheduling error or cancellation is already propagated. */ }
            }
        }

        private static async Task<ServerProbeMeasurement?> ProbeAsync(ServerRefreshPlan.Candidate candidate,
            Func<ServerRefreshPlan.Candidate, CancellationToken, Task<ServerProbeMeasurement>> probe,
            CancellationToken cancellation)
        {
            // Null means the probe never acquired a slot. A default measurement
            // means admitted work ended without measuring.
            if (!await Slots.WaitAsync(0, cancellation).ConfigureAwait(false)) return null;
            try { return await probe(candidate, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return default(ServerProbeMeasurement); }
            finally { Slots.Release(); }
        }
    }
}
