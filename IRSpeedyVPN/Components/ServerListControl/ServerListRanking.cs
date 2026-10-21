using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Components.ServerListControl
{
    // Share ranking between the existing live groups and the MVVM snapshots.
    internal static class ServerListRanking
    {
        internal static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, bool> lastSuccessful,
            Func<T, long> latency, Func<T, string> country, Func<T, int> id)
        {
            return items.OrderByDescending(lastSuccessful).ThenBy(latency)
                .ThenBy(country, StringComparer.CurrentCulture).ThenBy(id);
        }
    }
}
