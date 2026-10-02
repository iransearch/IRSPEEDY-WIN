using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Resource;
using System;
using System.Globalization;

namespace IRSpeedyVPN.Common
{
    internal static class LastSuccessfulServer
    {
        private const string SettingName = "LastSuccessfulServer";

        internal static string Key(IVPNService service)
        {
            return service == null ? "" : service.GetType().FullName + ":"
                + service.ID.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Read()
        {
            try { return RegHelper.GetSettingValue(SettingName) ?? ""; }
            catch (Exception ex) { LogHelper.WriteLog(ex); return ""; }
        }

        // Called only after the current connection (including Proxifier) succeeds.
        // Global Smart is a fixed card, not one of the numbered country rows.
        internal static bool TryRecord(IVPNService service, out string key)
        {
            key = "";
            if (service == null || (service is ISmartFastConnection smart
                && smart.IsSmartFast && service.SelectedServerUrl == null)) return false;

            key = Key(service);
            try { RegHelper.SetSettingValue(SettingName, key); }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            return true;
        }
    }
}
