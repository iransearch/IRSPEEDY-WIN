// Fixture mirrors the existing LibcoreAutoSelectors status fields, without the decoder.
using System;
using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class AutoSelectorMember
    {
        public string Tag = "", State = "", LastError = "";
        public int Rank, AverageMs, DeviationMs, MinMs, MaxMs, Samples, Failures, Probes, DialTotal, DialFail;
        public bool Selected, SelectedUdp, Qualified, Active;
        public long LastOkMs, LastProbeMs, CooldownUntilMs;
    }

    internal sealed class AutoSelectorStatus
    {
        public string Tag = "", Phase = "", Selected = "", SelectedUdp = "", BalanceMode = "", LastSwitchReason = "", Pinned = "";
        public bool Balance, Suspended;
        public int MembersTotal, MembersProbed, MembersAlive, MembersQualified, MembersCooldown, ProbesInFlight, RoundsCompleted;
        public long SuspendedSinceMs, LastRoundMs, NextRoundMs, LastSwitchMs;
        public readonly List<AutoSelectorMember> Members = new List<AutoSelectorMember>();
    }

    internal sealed class QueryAutoSelectorsResponse
    {
        public readonly List<AutoSelectorStatus> Groups = new List<AutoSelectorStatus>();
    }

}
