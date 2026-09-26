using IRSpeedyVPN.Resource;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.Services.Hotspot
{
    internal static class DirectHotspot
    {
        internal static readonly HotspotCoordinator Controller = new HotspotCoordinator(() => new HotspotProcessChannel());
        // Independent of WPF windows and the dispatcher: closing the popup or dragging
        // the main window must not starve the helper's ten-second lease.
        private static readonly Timer Timer = new Timer(_ => Poll(), null, 2000, 2000);
        internal static void StopPollingForExit() => Timer.Dispose();
        private static int polling;
        private static void Poll()
        {
            if (Interlocked.Exchange(ref polling, 1) != 0) return;
            try { Controller.Poll(); }
            catch { /* The coordinator preserves its failure state for the UI. */ }
            finally { Volatile.Write(ref polling, 0); }
        }
        internal static string Ssid
        {
            get
            {
                string value = RegHelper.GetSettingValue("DirectHotspotSsid");
                if (string.IsNullOrEmpty(value))
                {
                    value = "IRSPEEDY-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
                    RegHelper.SetSettingValue("DirectHotspotSsid", value);
                }
                return value;
            }
        }
        internal static string Password
        {
            get
            {
                try
                {
                    var saved = RegHelper.GetSettingValue("DirectHotspotSecret");
                    if (!string.IsNullOrEmpty(saved))
                    {
                        string value = Encoding.ASCII.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved),
                            null, DataProtectionScope.CurrentUser));
                        if (HotspotCoordinator.ValidPassword(value)) return value;
                    }
                }
                catch { }
                string fresh = GeneratePassword();
                SavePassword(fresh);
                return fresh;
            }
        }
        internal static string GeneratePassword()
        {
            var result = new StringBuilder(10);
            using (var rng = RandomNumberGenerator.Create())
            {
                var one = new byte[1];
                while (result.Length < 10)
                {
                    rng.GetBytes(one);
                    if (one[0] < 250) result.Append((char)('0' + one[0] % 10));
                }
            }
            return result.ToString();
        }
        internal static void SavePassword(string value)
        {
            if (!HotspotCoordinator.ValidPassword(value)) throw new InvalidOperationException("invalid-password");
            RegHelper.SetSettingValue("DirectHotspotSecret", Convert.ToBase64String(ProtectedData.Protect(
                Encoding.ASCII.GetBytes(value), null, DataProtectionScope.CurrentUser)));
        }
    }
}
