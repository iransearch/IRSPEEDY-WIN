using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    // The display scan groups private probe copies by row. Display order is separate.
    internal sealed class ServerRefreshPlan
    {
        internal sealed class Owner { internal IVPNService Service; internal Url Url; }
        internal sealed class Candidate
        {
            internal Url Probe;
            internal readonly List<Owner> Owners = new List<Owner>();
        }
        internal sealed class Country
        {
            internal IVPNService Service;
            internal readonly List<Candidate> Pending = new List<Candidate>();
        }

        internal static List<Country> Countries(IEnumerable<IVPNService> services,
            Func<IVPNService, Url, bool> failedLastRound, Random random)
        {
            var countries = new List<Country>();
            foreach (var service in services.Where(s => s.IsUrlTestSupported))
            {
                var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
                foreach (var url in service.GetServerUrls() ?? new List<Url>())
                {
                    if (url == null || string.IsNullOrWhiteSpace(url.url)) continue;
                    string key = ServerCheckCache.ProbeIdentity(service, url);
                    if (!candidates.TryGetValue(key, out var candidate))
                    {
                        candidate = new Candidate { Probe = Copy(url) };
                        candidates.Add(key, candidate);
                    }
                    candidate.Owners.Add(new Owner { Service = service, Url = url });
                }
                var country = new Country { Service = service };
                var shuffled = candidates.Values.ToList();
                Shuffle(shuffled, random);
                country.Pending.AddRange(shuffled.OrderBy(candidate =>
                    failedLastRound(service, candidate.Owners[0].Url) ? 0 : 1));
                countries.Add(country);
            }
            Shuffle(countries, random);
            return countries;
        }

        internal static int Compare(Candidate first, ServerProbeMeasurement a, Candidate second, ServerProbeMeasurement b)
        {
            int result = a.Latency.CompareTo(b.Latency);
            if (result != 0) return result;
            result = a.FailureCount.CompareTo(b.FailureCount);
            if (result != 0) return result;
            result = b.SampleCount.CompareTo(a.SampleCount);
            if (result != 0) return result;
            return StringComparer.OrdinalIgnoreCase.Compare(Id(first), Id(second));
        }

        private static string Id(Candidate candidate)
        {
            var owner = candidate.Owners[0];
            return candidate.Probe.id > 0 ? candidate.Probe.id.ToString(CultureInfo.InvariantCulture)
                : ServerCheckCache.ProbeIdentity(owner.Service, owner.Url);
        }

        private static Url Copy(Url url) => new Url
        {
            id = url.id, url = url.url, extra_field_1 = url.extra_field_1,
            extra_field_2 = url.extra_field_2, chainproxy = url.chainproxy, is_subscription = url.is_subscription
        };

        private static void Shuffle<T>(List<T> items, Random random)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var item = items[i];
                items[i] = items[j];
                items[j] = item;
            }
        }
    }
}
