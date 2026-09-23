using IRSpeedyVPN.Resource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace IRSpeedyVPN.Services.SplitTunneling
{
    internal static class SplitTunnelStore
    {
        private const string Key = "SplitTunnelSettingsV1";
        public static SplitTunnelSettings Load()
        {
            var raw = RegHelper.GetSettingValue(Key);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var document = JObject.Parse(raw);
                int version = (int?)document["Version"] ?? 1;
                if (version != 1 && version != 2)
                    throw new InvalidOperationException("نسخهٔ تنظیمات تقسیم تونل پشتیبانی نمی‌شود.");
                // Both old modes now mean selected apps use VPN, as requested.
                // Keep the list and enabled state, but never persist a mode switch.
                document.Remove("Mode");
                document["Version"] = 2;
                var settings = document.ToObject<SplitTunnelSettings>();
                Validate(settings);
                settings.CustomApps = settings.CustomApps.Concat(settings.Apps.Where(a => a.Source == "Manual"))
                    .GroupBy(a => a.Identity, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                Validate(settings);
                return settings;
            }
            // Retain the preview selections, but require an explicit enable before
            // previously cosmetic settings can change the user's traffic routing.
            var legacy = RegHelper.GetSettingValue("SplitTunnelPreviewApplications");
            var result = new SplitTunnelSettings();
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                foreach (var path in JsonConvert.DeserializeObject<string[]>(legacy) ?? new string[0])
                    if (AppPathPattern.IsLocalPath(path)) result.Apps.Add(new SplitTunnelApp
                    { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path, Source = "Saved" });
            }
            return result;
        }

        public static void Save(SplitTunnelSettings settings)
        {
            Validate(settings);
            RegHelper.SetSettingValue(Key, JsonConvert.SerializeObject(settings));
        }

        private static void Validate(SplitTunnelSettings settings)
        {
            if (settings == null || settings.Version != 2
                || settings.Apps == null || settings.Apps.Count > 2048
                || settings.Apps.Any(a => AppPathPattern.ForApp(a) == null)
                || settings.CustomApps == null || settings.CustomApps.Count > 2048
                || settings.CustomApps.Any(a => AppPathPattern.ForApp(a) == null || a.Source != "Manual"))
                throw new InvalidOperationException("تنظیمات تقسیم تونل معتبر نیست؛ فهرست برنامه‌ها را دوباره ذخیره کنید.");
        }
    }
}
