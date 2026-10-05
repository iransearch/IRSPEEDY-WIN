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

    internal static partial class LibcoreProto
    {
        // Throne libcore.proto, fields 1..21. This is an idempotent status query;
        // it neither schedules probes nor resets the selector's counters.
        public static QueryAutoSelectorsResponse DecodeQueryAutoSelectors(byte[] data)
        {
            var result = new QueryAutoSelectorsResponse();
            var reader = new ProtoReader(data ?? Array.Empty<byte>());
            while (reader.TryReadField(out int field, out int wire))
            {
                if (field == 1 && wire == WireLengthDelimited) result.Groups.Add(ReadAutoSelector(reader.ReadBytes()));
                else reader.SkipField(wire);
            }
            return result;
        }

        private static AutoSelectorStatus ReadAutoSelector(byte[] data)
        {
            var row = new AutoSelectorStatus();
            var reader = new ProtoReader(data);
            while (reader.TryReadField(out int field, out int wire))
            {
                if (field == 20 && wire == WireLengthDelimited)
                {
                    row.Members.Add(ReadAutoSelectorMember(reader.ReadBytes()));
                    continue;
                }
                if (wire == WireLengthDelimited && (field >= 1 && field <= 4 || field == 6 || field == 19 || field == 21))
                {
                    string value = reader.ReadString();
                    switch (field)
                    {
                        case 1: row.Tag = value; break;
                        case 2: row.Phase = value; break;
                        case 3: row.Selected = value; break;
                        case 4: row.SelectedUdp = value; break;
                        case 6: row.BalanceMode = value; break;
                        case 19: row.LastSwitchReason = value; break;
                        case 21: row.Pinned = value; break;
                    }
                }
                else if (wire == WireVarint && (field == 5 || field >= 7 && field <= 18))
                {
                    long value = unchecked((long)reader.ReadVarint());
                    switch (field)
                    {
                        case 5: row.Balance = value != 0; break;
                        case 7: row.Suspended = value != 0; break;
                        case 8: row.SuspendedSinceMs = value; break;
                        case 9: row.MembersTotal = unchecked((int)value); break;
                        case 10: row.MembersProbed = unchecked((int)value); break;
                        case 11: row.MembersAlive = unchecked((int)value); break;
                        case 12: row.MembersQualified = unchecked((int)value); break;
                        case 13: row.MembersCooldown = unchecked((int)value); break;
                        case 14: row.ProbesInFlight = unchecked((int)value); break;
                        case 15: row.RoundsCompleted = unchecked((int)value); break;
                        case 16: row.LastRoundMs = value; break;
                        case 17: row.NextRoundMs = value; break;
                        case 18: row.LastSwitchMs = value; break;
                    }
                }
                else reader.SkipField(wire);
            }
            return row;
        }

        private static AutoSelectorMember ReadAutoSelectorMember(byte[] data)
        {
            var row = new AutoSelectorMember();
            var reader = new ProtoReader(data);
            while (reader.TryReadField(out int field, out int wire))
            {
                if (wire == WireLengthDelimited && (field == 1 || field == 3 || field == 20))
                {
                    string value = reader.ReadString();
                    if (field == 1) row.Tag = value;
                    else if (field == 3) row.State = value;
                    else row.LastError = value;
                }
                else if (wire == WireVarint && (field == 2 || field >= 4 && field <= 19))
                {
                    long value = unchecked((long)reader.ReadVarint());
                    switch (field)
                    {
                        case 2: row.Rank = unchecked((int)value); break;
                        case 4: row.Selected = value != 0; break;
                        case 5: row.SelectedUdp = value != 0; break;
                        case 6: row.Qualified = value != 0; break;
                        case 7: row.Active = value != 0; break;
                        case 8: row.AverageMs = unchecked((int)value); break;
                        case 9: row.DeviationMs = unchecked((int)value); break;
                        case 10: row.MinMs = unchecked((int)value); break;
                        case 11: row.MaxMs = unchecked((int)value); break;
                        case 12: row.Samples = unchecked((int)value); break;
                        case 13: row.Failures = unchecked((int)value); break;
                        case 14: row.Probes = unchecked((int)value); break;
                        case 15: row.DialTotal = unchecked((int)value); break;
                        case 16: row.DialFail = unchecked((int)value); break;
                        case 17: row.LastOkMs = value; break;
                        case 18: row.LastProbeMs = value; break;
                        case 19: row.CooldownUntilMs = value; break;
                    }
                }
                else reader.SkipField(wire);
            }
            return row;
        }
    }
}
