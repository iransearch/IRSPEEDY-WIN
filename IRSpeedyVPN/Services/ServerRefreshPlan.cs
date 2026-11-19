using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    // Snapshot configuration and priority separately from the displayed order.
    internal sealed class ServerRefreshPlan
    {
        internal sealed class Candidate
        {
            internal IVPNService Service;
            internal Url Original, Probe;
            internal string Id;
            internal bool PreviousFailure;
            internal int Order;
        }

        internal sealed class Outcome
        {
            internal Url Winner;
            internal long Latency = -1;
            internal readonly List<Url> Failed = new List<Url>();
        }

        private readonly Candidate[] candidates;
        internal int Total => candidates.Length;

        internal ServerRefreshPlan(IEnumerable<IVPNService> services, Random random = null,
            Func<IVPNService, Url, bool> failed = null)
        {
            candidates = services.Where(s => s.IsUrlTestSupported).Distinct().SelectMany(service =>
                (service.GetServerUrls() ?? new List<Url>())
                .Where(url => url != null && !string.IsNullOrWhiteSpace(url.url))
                .GroupBy(url => ServerCheckCache.ProbeConfigIdentity(service, url), StringComparer.Ordinal)
                .Select(configs =>
                {
                    // Aliases of one configuration within the same row are one
                    // measurement. Distinct rows always own their own admission.
                    var url = configs.OrderBy(u => ServerId(u, configs.Key), StringComparer.OrdinalIgnoreCase).First();
                    return new Candidate
                    {
                        Service = service, Original = url, Id = ServerId(url, configs.Key),
                        PreviousFailure = failed?.Invoke(service, url) == true,
                        Probe = new Url { id = url.id, url = url.url, extra_field_1 = url.extra_field_1,
                            extra_field_2 = url.extra_field_2, chainproxy = url.chainproxy,
                            is_subscription = url.is_subscription }
                    };
                })).ToArray();

            random = random ?? new Random();
            for (int i = candidates.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var member = candidates[i]; candidates[i] = candidates[j]; candidates[j] = member;
            }
            for (int i = 0; i < candidates.Length; i++) candidates[i].Order = i;
        }

        private static string ServerId(Url url, string fallback)
            => url.id != 0 ? url.id.ToString(CultureInfo.InvariantCulture) : fallback;

        internal Task RunAsync(Func<Candidate, CancellationToken, Task<DisplayProbeResult>> probe,
            Action<IVPNService, Outcome> completed, CancellationToken cancellation,
            Action<IVPNService, long> progress = null)
            => CountryProbeScheduler.RunAsync(candidates, probe, completed, progress, cancellation);
    }

    // No dispatcher, cache or Core dependency. One admission loop owns all
    // country transitions, so an early win cannot race with another admission.
    internal static class CountryProbeScheduler
    {
        private const int Capacity = 8;
        private static readonly SemaphoreSlim Slots = new SemaphoreSlim(Capacity, Capacity);

        private sealed class Job
        {
            internal ServerRefreshPlan.Candidate Candidate;
            internal Task Ready = Task.CompletedTask;
            internal Task<DisplayProbeResult> Running;
        }

        private sealed class Country
        {
            internal IVPNService Service;
            internal List<Job> Waiting;
            internal int Running;
            internal bool Satisfied;
            internal ServerRefreshPlan.Candidate Winner;
            internal DisplayProbeResult Best;
            internal readonly ServerRefreshPlan.Outcome Outcome = new ServerRefreshPlan.Outcome();
        }

        internal static async Task RunAsync(IEnumerable<ServerRefreshPlan.Candidate> candidates,
            Func<ServerRefreshPlan.Candidate, CancellationToken, Task<DisplayProbeResult>> probe,
            Action<IVPNService, ServerRefreshPlan.Outcome> completed, Action<IVPNService, long> progress,
            CancellationToken cancellation)
        {
            var countries = candidates.GroupBy(c => c.Service).Select(g => new Country
            {
                Service = g.Key,
                Waiting = g.OrderByDescending(c => c.PreviousFailure).ThenBy(c => c.Order)
                    .Select(c => new Job { Candidate = c }).ToList()
            }).ToList();
            var running = new List<KeyValuePair<Country, Job>>();
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (stop.Token.Register(() => canceled.TrySetResult(true)))
                try
                {
                    while (countries.Count != 0)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        foreach (var pair in running.Where(p => p.Value.Running.IsCompleted).ToArray())
                        {
                            running.Remove(pair);
                            var country = pair.Key;
                            var job = pair.Value;
                            var result = await job.Running.ConfigureAwait(false);
                            stop.Token.ThrowIfCancellationRequested();
                            country.Running--;
                            if (!result.Measured)
                            {
                                if (!country.Satisfied)
                                {
                                    job.Ready = Task.Delay(250, stop.Token);
                                    country.Waiting.Add(job);
                                }
                            }
                            else if (!result.Success)
                                country.Outcome.Failed.Add(job.Candidate.Original);
                            else
                            {
                                if (country.Winner == null || IsBetter(job.Candidate, result, country))
                                {
                                    country.Winner = job.Candidate;
                                    country.Best = result;
                                    country.Outcome.Winner = job.Candidate.Original;
                                    country.Outcome.Latency = result.Latency;
                                    progress?.Invoke(country.Service, result.Latency);
                                }
                                if (result.Latency < 300)
                                {
                                    country.Satisfied = true;
                                    country.Waiting.Clear(); // No synthetic failures.
                                }
                            }
                        }

                        foreach (var country in countries.Where(c => c.Running == 0 && c.Waiting.Count == 0).ToArray())
                        {
                            countries.Remove(country);
                            completed(country.Service, country.Outcome);
                        }
                        if (countries.Count == 0) break;

                        int active = countries.Count(c => !c.Satisfied);
                        int cap = active == 0 ? 0 : (Capacity + active - 1) / active;
                        if (running.Count < Capacity)
                        {
                            var next = countries.Where(c => !c.Satisfied && c.Running < cap)
                                .Select(c => new KeyValuePair<Country, Job>(c, c.Waiting.FirstOrDefault(j => j.Ready.IsCompleted)))
                                .Where(p => p.Value != null)
                                .OrderByDescending(p => p.Value.Candidate.PreviousFailure)
                                .ThenBy(p => p.Value.Candidate.Order).FirstOrDefault();
                            if (next.Value != null)
                            {
                                next.Key.Waiting.Remove(next.Value);
                                next.Key.Running++;
                                next.Value.Running = ProbeAsync(next.Value.Candidate, probe, stop.Token);
                                running.Add(next);
                                continue; // Consume any completed result before admitting again.
                            }
                        }

                        // Include a ready task that completed after the admission check.
                        // Only eligible countries can wake us, to avoid a full-slot spin.
                        var delayed = running.Count == Capacity ? Enumerable.Empty<Task>() : countries
                            .Where(c => !c.Satisfied && c.Running < cap).SelectMany(c => c.Waiting.Select(j => j.Ready));
                        var wakes = running.Select(p => (Task)p.Value.Running).Concat(delayed)
                            .Concat(new[] { (Task)canceled.Task });
                        await Task.WhenAny(wakes).ConfigureAwait(false);
                    }
                }
                finally
                {
                    stop.Cancel();
                    // Don't let a new scan or a connection race with cleanup.
                    try { await Task.WhenAll(running.Select(p => p.Value.Running)).ConfigureAwait(false); }
                    catch { /* The admitting loop propagates its original failure or cancellation. */ }
                }
            }
        }

        private static async Task<DisplayProbeResult> ProbeAsync(ServerRefreshPlan.Candidate candidate,
            Func<ServerRefreshPlan.Candidate, CancellationToken, Task<DisplayProbeResult>> probe, CancellationToken cancellation)
        {
            if (!Slots.Wait(0)) return DisplayProbeResult.Deferred;
            try { return await probe(candidate, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { return DisplayProbeResult.Deferred; }
            finally { Slots.Release(); }
        }

        private static bool IsBetter(ServerRefreshPlan.Candidate candidate, DisplayProbeResult result, Country country)
        {
            int compared = result.CompareTo(country.Best);
            return compared < 0 || (compared == 0 && StringComparer.OrdinalIgnoreCase.Compare(candidate.Id, country.Winner.Id) < 0);
        }
    }
}
