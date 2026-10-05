using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    internal static class ConnectionStateDiagnostic
    {
        internal static string Tag(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            if (value == "proxy" || value == "ai-proxy" || value == "direct" || value == "block"
                || Regex.IsMatch(value, @"\A(?:smart|ai)-proxy-[0-9]{1,5}\z")) return value;
            return "opaque-" + ConnectionDiagnostics.Fingerprint(value);
        }

        private static string Enum(string value, params string[] allowed)
            => allowed.Contains(value) ? value : "unknown";
        private static long Age(long now, long time) => time <= 0 ? -1 : Math.Max(0, now - time);
        private static long Until(long now, long time) => time <= 0 ? -1 : Math.Max(0, time - now);

        internal static void Pools(QueryAutoSelectorsResponse response, long now, Action<string, string> write)
        {
            if (response == null) throw new InvalidOperationException("Missing selector response.");
            write("pool-state-summary", "groups=" + response.Groups.Count + " returnedGroups=" + Math.Min(8, response.Groups.Count));
            foreach (var group in response.Groups.Take(8))
            {
                string identity = "pool=" + Tag(group.Tag) + " coreTagId=" + CoreDiagnosticMetadata.Hash(group.Tag);
                write("pool-state", identity + " phase=" + Enum(group.Phase, "starting", "probing", "ready", "suspended")
                    + " balancing=" + group.Balance + " balanceMode=" + Enum(group.BalanceMode, "rotate", "connection", "round-robin")
                    + " selectedTcp=" + Tag(group.Selected) + " selectedUdp=" + Tag(group.SelectedUdp) + " pinned=" + Tag(group.Pinned)
                    + " suspended=" + group.Suspended + " suspendedAgeMs=" + Age(now, group.SuspendedSinceMs)
                    + " total=" + group.MembersTotal + " probed=" + group.MembersProbed + " alive=" + group.MembersAlive
                    + " qualified=" + group.MembersQualified + " cooldown=" + group.MembersCooldown
                    + " probesInFlight=" + group.ProbesInFlight + " rounds=" + group.RoundsCompleted
                    + " lastRoundAgeMs=" + Age(now, group.LastRoundMs) + " nextRoundInMs=" + Until(now, group.NextRoundMs)
                    + " lastSwitchAgeMs=" + Age(now, group.LastSwitchMs)
                    + " switchReasonId=" + ConnectionDiagnostics.Fingerprint(group.LastSwitchReason)
                    + " returnedMembers=" + group.Members.Count + " omittedMembers=" + Math.Max(0, group.Members.Count - 64));
                // Prioritize the selected and qualified members if a large pool is truncated.
                foreach (var member in group.Members.OrderByDescending(m => m.Selected || m.SelectedUdp)
                    .ThenByDescending(m => m.Qualified).ThenBy(m => m.Rank).Take(64))
                    write("pool-member-state", identity + " member=" + Tag(member.Tag) + " coreMemberTagId=" + CoreDiagnosticMetadata.Hash(member.Tag)
                        + " rank=" + member.Rank + " state=" + Enum(member.State, "ok", "degraded", "untested", "dead", "cooldown")
                        + " selectedTcp=" + member.Selected + " selectedUdp=" + member.SelectedUdp
                        + " qualified=" + member.Qualified + " activeTier=" + member.Active
                        + " averageMs=" + member.AverageMs + " deviationMs=" + member.DeviationMs
                        + " minMs=" + member.MinMs + " maxMs=" + member.MaxMs
                        + " samples=" + member.Samples + " failures=" + member.Failures + " probes=" + member.Probes
                        + " dialTotal=" + member.DialTotal + " dialFail=" + member.DialFail
                        + " lastOkAgeMs=" + Age(now, member.LastOkMs) + " lastProbeAgeMs=" + Age(now, member.LastProbeMs)
                        + " cooldownRemainingMs=" + Until(now, member.CooldownUntilMs)
                        + " lastDialReason=" + NetworkFailureDiagnostic.CoreReason(member.LastError)
                        + " lastDialErrorId=" + ConnectionDiagnostics.Fingerprint(member.LastError));
            }
        }

        internal static void Flows(QueryConnectionsResponse response, Action<string, string> write)
        {
            if (response == null) throw new InvalidOperationException("Missing connection response.");
            var buckets = new Dictionary<string, FlowCounts>();
            foreach (var row in response.Active) AddFlow(buckets, row, true);
            foreach (var row in response.Closed) AddFlow(buckets, row, false);
            write("flow-state-summary", "active=" + response.Active.Count + " recentlyClosed=" + response.Closed.Count
                + " routeBuckets=" + buckets.Count + " omittedBuckets=" + Math.Max(0, buckets.Count - 20)
                + " counters=cumulative-retained-connections");
            foreach (var bucket in buckets.OrderByDescending(b => b.Key.Contains("target=public-ip-"))
                .ThenByDescending(b => b.Value.Active).ThenBy(b => b.Key, StringComparer.Ordinal).Take(20))
                write("flow-route-state", bucket.Key + " active=" + bucket.Value.Active + " recentlyClosed=" + bucket.Value.Closed
                    + " upload=" + bucket.Value.Upload + " download=" + bucket.Value.Download
                    + " zeroDownload=" + bucket.Value.ZeroDownload);
        }

        private sealed class FlowCounts
        {
            internal int Active, Closed, ZeroDownload;
            internal long Upload, Download;
        }
        private static long Sum(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + Math.Max(0, b);
        private static void AddFlow(Dictionary<string, FlowCounts> buckets, CoreTrafficConnection row, bool active)
        {
            string key = "outbound=" + Tag(row.Outbound) + " network=" + Enum(row.Network, "tcp", "udp")
                + " target=" + Enum(row.DiagnosticTarget, "other", "public-ip-ipify", "public-ip-amazon", "public-ip-icanhazip")
                + " chain=" + (row.Chain.Count == 0 ? "none" : string.Join(",", row.Chain.Take(6).Select(Tag)))
                + " omittedChainTags=" + Math.Max(0, row.Chain.Count - 6);
            FlowCounts counts;
            if (!buckets.TryGetValue(key, out counts)) buckets.Add(key, counts = new FlowCounts());
            if (active) counts.Active++; else counts.Closed++;
            counts.Upload = Sum(counts.Upload, row.Upload); counts.Download = Sum(counts.Download, row.Download);
            if (row.Download == 0) counts.ZeroDownload++;
        }
    }

    // At most one background snapshot per service. Ordinary error bursts are
    // sampled once per 30 seconds; explicit lookup/test failures can capture
    // their own context after two seconds. No timer or probe is started here.
    internal sealed class ConnectionSnapshotGate
    {
        private readonly object gate = new object();
        private long generation = long.MinValue, nextAny, nextCoreError;
        private bool busy;
        internal bool TryEnter(long connection, long nowMs, bool coreError)
        {
            lock (gate)
            {
                if (busy) return false;
                if (connection != generation) { generation = connection; nextAny = nextCoreError = 0; }
                if (nowMs < nextAny || coreError && nowMs < nextCoreError) return false;
                busy = true; nextAny = nowMs + 2000;
                if (coreError) nextCoreError = nowMs + 30000;
                return true;
            }
        }
        internal void Exit() { lock (gate) busy = false; }
    }
}
