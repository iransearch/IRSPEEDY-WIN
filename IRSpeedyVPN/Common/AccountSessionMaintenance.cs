using System;
using System.Diagnostics;
using System.Threading;

namespace IRSpeedyVPN.Common
{
    // Account lifetime and monotonic check deadline are independent of VPN/UI lifetime.
    internal sealed class AccountSessionMaintenance
    {
        internal const int IntervalMilliseconds = 30 * 60 * 1000;
        internal const int BusyRetryMilliseconds = 60 * 1000;
        private readonly object gate = new object();
        private readonly Func<long> now;
        private long generation, nextCheckAt;
        private bool active;

        internal AccountSessionMaintenance(Func<long> clock = null)
        {
            var elapsed = Stopwatch.StartNew();
            now = clock ?? (() => elapsed.ElapsedMilliseconds);
        }

        internal long Generation { get { lock (gate) return generation; } }
        internal long Begin(bool verified)
        {
            lock (gate)
            {
                active = true;
                nextCheckAt = now() + (verified ? IntervalMilliseconds : 0);
                return ++generation;
            }
        }
        internal long End()
        {
            lock (gate) { active = false; return ++generation; }
        }
        internal bool IsCurrent(long version)
        {
            lock (gate) return active && version == generation;
        }
        internal bool RequestStarted(long version)
        {
            lock (gate)
            {
                if (!active || version != generation) return false;
                nextCheckAt = now() + IntervalMilliseconds;
                return true;
            }
        }
        internal int DelayMilliseconds(bool retryIfDue = false)
        {
            lock (gate)
            {
                if (!active) return Timeout.Infinite;
                long remaining = Math.Max(0, nextCheckAt - now());
                return remaining == 0 && retryIfDue ? BusyRetryMilliseconds : (int)Math.Min(int.MaxValue, remaining);
            }
        }
    }
}
