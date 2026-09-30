using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Services.Traffic
{
    internal sealed class TrafficUsage
    {
        public string Name;
        public string Path;
        public long Upload;
        public long Download;
        public TrafficUsage Copy() => (TrafficUsage)MemberwiseClone();
    }

    // Owned by the collector worker. UI receives detached snapshots, never this state.
    internal sealed class TrafficLedger
    {
        private readonly Dictionary<string, TrafficUsage> totals =
            new Dictionary<string, TrafficUsage>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CoreTrafficConnection> previous = new Dictionary<string, CoreTrafficConnection>();
        private HashSet<string> accountedClosed = new HashSet<string>();
        public DateTime SinceUtc { get; private set; } = DateTime.UtcNow;

        public TrafficUsage[] Snapshot() => totals.Values.Select(row => row.Copy()).ToArray();

        public void Restore(IEnumerable<TrafficUsage> rows, DateTime since)
        {
            totals.Clear();
            foreach (var row in rows) totals[row.Name] = row.Copy();
            SinceUtc = since;
        }

        public void BeginSession()
        {
            previous.Clear();
            accountedClosed.Clear();
        }

        public void Apply(QueryConnectionsResponse response)
        {
            var live = new Dictionary<string, CoreTrafficConnection>();
            var closed = new HashSet<string>();
            // Share the same baseline across both sets, including a close racing a poll.
            var credited = new Dictionary<string, CoreTrafficConnection>(previous);
            foreach (var connection in response.Active)
            {
                Credit(connection, credited);
                live[connection.Id] = connection;
            }
            foreach (var connection in response.Closed)
            {
                closed.Add(connection.Id);
                if (!accountedClosed.Contains(connection.Id)) Credit(connection, credited);
                live.Remove(connection.Id);
            }
            previous = live;
            accountedClosed = closed;
        }

        private void Credit(CoreTrafficConnection current, Dictionary<string, CoreTrafficConnection> baselines)
        {
            if (string.IsNullOrEmpty(current.Id)) return;
            baselines.TryGetValue(current.Id, out var prior);
            long up = Math.Max(0, current.Upload - (prior?.Upload ?? 0));
            long down = Math.Max(0, current.Download - (prior?.Download ?? 0));
            baselines[current.Id] = current;
            if (up == 0 && down == 0) return;
            // Match Throne's per-executable aggregation rather than per-PID rows.
            var name = string.IsNullOrWhiteSpace(current.Process) ? "نامشخص" : current.Process;
            if (!totals.TryGetValue(name, out var row))
                totals.Add(name, row = new TrafficUsage { Name = name });
            if (!string.IsNullOrEmpty(current.ProcessPath)) row.Path = current.ProcessPath;
            row.Upload = AddSaturated(row.Upload, up);
            row.Download = AddSaturated(row.Download, down);
        }

        private static long AddSaturated(long value, long delta) =>
            delta > long.MaxValue - value ? long.MaxValue : value + delta;

        public void Reset(QueryConnectionsResponse baseline, DateTime since)
        {
            totals.Clear();
            SinceUtc = since;
            BeginSession();
            // Existing connections keep their current baseline. Old closed ring entries
            // must never reappear as new usage after reset.
            foreach (var row in baseline.Active) previous[row.Id] = row;
            foreach (var row in baseline.Closed) accountedClosed.Add(row.Id);
        }
    }
}
