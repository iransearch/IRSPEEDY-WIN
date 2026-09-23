using IRSpeedyVPN.Resource;
using Newtonsoft.Json;
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
                var settings = JsonConvert.DeserializeObject<SplitTunnelSettings>(raw);
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
            if (settings == null || settings.Version != 1 || !Enum.IsDefined(typeof(SplitTunnelMode), settings.Mode)
                || settings.Apps == null || settings.Apps.Count > 2048
                || settings.Apps.Any(a => AppPathPattern.ForApp(a) == null))
                throw new InvalidOperationException("تنظیمات تقسیم تونل معتبر نیست؛ فهرست برنامه‌ها را دوباره ذخیره کنید.");
        }
    }
}
