using System;

namespace IRSpeedyVPN.Services
{
    internal sealed class CountryProbeSchedule
    {
        private DateTime dueUtc = DateTime.MinValue;
        internal void RestartNow() { dueUtc = DateTime.MinValue; }
        internal void Completed(DateTime nowUtc) { dueUtc = nowUtc.AddMinutes(3); }
        internal TimeSpan Remaining(DateTime nowUtc)
        {
            return dueUtc > nowUtc ? dueUtc - nowUtc : TimeSpan.Zero;
        }
    }
}
