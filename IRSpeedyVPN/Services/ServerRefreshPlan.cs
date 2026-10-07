using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    // One shuffled queue of configurations, independent of country and UI order.
    // Private URL copies prevent in-flight Core work from mutating visible rows.
    internal sealed class ServerRefreshPlan
    {
        internal sealed class Owner { internal IVPNService Service; internal Url Url; }
        internal sealed class Candidate
        {
            internal Url Probe;
            internal readonly List<Owner> Owners = new List<Owner>();
            internal bool Completed;
        }
        private readonly List<Candidate> queue;
        private readonly Dictionary<IVPNService, int> remaining = new Dictionary<IVPNService, int>();
        private int cursor;
        internal ServerRefreshPlan(IEnumerable<IVPNService> services, Random random = null)
        {
            var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
            foreach (var service in services.Where(s => s.IsUrlTestSupported))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var url in service.GetServerUrls() ?? new List<Url>())
                {
                    if (url == null || string.IsNullOrWhiteSpace(url.url)) continue;
                    string key = JsonConvert.SerializeObject(new object[] { url.url, url.extra_field_1, url.extra_field_2,
                        url.chainproxy, url.irancell, url.is_subscription });
                    Candidate candidate;
                    if (!candidates.TryGetValue(key, out candidate))
                    {
                        candidate = new Candidate { Probe = new Url { url = url.url, extra_field_1 = url.extra_field_1,
                            extra_field_2 = url.extra_field_2, chainproxy = url.chainproxy, irancell = url.irancell,
                            is_subscription = url.is_subscription } };
                        candidates.Add(key, candidate);
                    }
                    candidate.Owners.Add(new Owner { Service = service, Url = url });
                    if (seen.Add(key))
                    {
                        int count; remaining.TryGetValue(service, out count); remaining[service] = count + 1;
                    }
                }
            }
            queue = candidates.Values.ToList();
            random = random ?? new Random();
            for (int i = queue.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var item = queue[i]; queue[i] = queue[j]; queue[j] = item;
            }
        }
        internal int Total => queue.Count;
        internal bool HasPending => cursor < queue.Count;
        internal List<Candidate> TakeBatch(int capacity = 15)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            var batch = new List<Candidate>();
            var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // The Core keys outbounds by link. Same link with different chain/SNI
            // metadata must be tested in separate batches, retaining queue order.
            // SNI/chain lookup is case-insensitive, so case variants also split.
            while (cursor < queue.Count && batch.Count < capacity && links.Add(queue[cursor].Probe.url))
                batch.Add(queue[cursor++]);
            return batch;
        }
        internal IVPNService[] Complete(Candidate candidate)
        {
            if (candidate.Completed) return Array.Empty<IVPNService>();
            candidate.Completed = true;
            return candidate.Owners.Select(o => o.Service).Distinct()
                .Where(service => --remaining[service] == 0).ToArray();
        }
    }
}
