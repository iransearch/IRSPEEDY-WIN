using System;
using System.IO;

namespace IRSpeedyVPN.Common
{
    internal static class VpnTimeZone
    {
        private static readonly Lazy<VpnTimeZoneSession> Current = new Lazy<VpnTimeZoneSession>(() =>
            new VpnTimeZoneSession(new WindowsTimeZoneSystem(), new TimeZoneJournal(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRSpeedyVPN", "time-zone-recovery.json")),
                new TimeZoneLookup()));
        internal static VpnTimeZoneSession Session => Current.Value;
        internal static DateTime AccountNow => Current.IsValueCreated ? Session.AccountNow : DateTime.Now;
        internal static void Recover()
        {
            try { Session.Recover(); }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
        }
        internal static void BeginConnection(object owner)
        {
            try { Session.BeginConnection(owner); }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
        }
        internal static void EndConnection(object expectedOwner = null)
        {
            if (!Current.IsValueCreated) return;
            try { Session.EndConnection(expectedOwner); }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
        }
    }
}
