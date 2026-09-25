using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Hotspot
{
    internal enum DirectSharingSupport { Checking, Supported, WindowsUnsupported, PayloadMissing, HardwareUnsupported, Unknown }

    internal static class DirectSharingAvailability
    {
        internal static bool CanSelect(DirectSharingSupport support, string state, string error)
        {
            if (support == DirectSharingSupport.WindowsUnsupported) return false;
            // A live session proves support. Never strand its Stop/recovery controls
            // because a new capability query timed out or the radio was removed.
            return support == DirectSharingSupport.Supported || NeedsStopAccess(state, error);
        }

        internal static bool NeedsStopAccess(string state, string error) =>
            state == "active" || state == "starting" || state == "paused"
            || (state == "error" && error == "cleanup-not-confirmed");

        internal static string Message(DirectSharingSupport support)
        {
            switch (support)
            {
                case DirectSharingSupport.Supported: return "";
                case DirectSharingSupport.Checking: return "در حال بررسی پشتیبانی دستگاه از اشتراک مستقیم…";
                case DirectSharingSupport.WindowsUnsupported: return "اشتراک مستقیم به ویندوز ۱۰ نسخهٔ ۲۰۰۴ یا جدیدتر نیاز دارد.";
                case DirectSharingSupport.PayloadMissing: return "این نسخهٔ برنامه قابلیت اشتراک مستقیم را ندارد؛ نسخهٔ کامل را دریافت کنید.";
                case DirectSharingSupport.HardwareUnsupported: return "کارت Wi-Fi یا درایور این دستگاه از اشتراک مستقیم پشتیبانی نمی‌کند.";
                default: return "پشتیبانی اشتراک مستقیم تأیید نشد؛ وضعیت Wi-Fi و درایور را بررسی کنید و دوباره وارد حساب شوید.";
            }
        }

        internal static DirectSharingSupport Check(bool supportedWindows, bool installed, Func<string> readCapabilities)
        {
            if (!supportedWindows) return DirectSharingSupport.WindowsUnsupported;
            if (!installed) return DirectSharingSupport.PayloadMissing;
            try { return Parse(readCapabilities()); }
            catch { return DirectSharingSupport.Unknown; }
        }

        // Only the GO (Group Owner / access-point) role proves this feature's
        // hardware capability. Hosted Network, Miracast and virtual-adapter names
        // are deliberately NOT evidence. Unknown/localized output fails closed.
        private static readonly HashSet<string> SupportedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Supported", "Unterstützt", "Pris en charge", "Compatible", "Admitido", "Soportado",
            "Supportato", "Suportado", "Поддерживается", "Obsługiwane", "Destekleniyor",
            "支持", "支援", "サポートされています", "サポートあり", "지원됨", "پشتیبانی می‌شود", "مدعوم"
        };
        private static readonly HashSet<string> UnsupportedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Not supported", "Nicht unterstützt", "Non pris en charge", "Non compatible", "No compatible",
            "No admitido", "No soportado", "Non supportato", "Não suportado", "Не поддерживается",
            "Nieobsługiwane", "Desteklenmiyor", "不支持", "不支援", "サポートされていません", "サポートなし",
            "지원되지 않음", "پشتیبانی نمی‌شود", "غير مدعوم"
        };

        internal static DirectSharingSupport Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output) || output.Length > 262144) return DirectSharingSupport.Unknown;
            bool negative = false, unknown = false;
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int colon = line.IndexOfAny(new[] { ':', '：' });
                    if (colon < 0) continue;
                    string key = Regex.Replace(line.Substring(0, colon).Trim(), @"\s+", " ")
                        .Replace('‑', '-').Replace('‐', '-');
                    if (!string.Equals(key, "Wi-Fi Direct GO", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(key, "Wi-Fi Direct Group Owner", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = Regex.Replace(line.Substring(colon + 1).Trim(), @"\s+", " ");
                    if (SupportedValues.Contains(value)) return DirectSharingSupport.Supported;
                    if (UnsupportedValues.Contains(value)) negative = true;
                    else unknown = true;
                }
            }
            // Multiple adapters: a supported GO wins; an unrecognized adapter
            // result must not turn another adapter's No into a system-wide No.
            return negative && !unknown ? DirectSharingSupport.HardwareUnsupported : DirectSharingSupport.Unknown;
        }
    }

    // One check per successful login, independent of sharing-window lifetime.
    // Replacing the task prevents an old login's delayed result from winning.
    internal sealed class DirectSharingSession
    {
        private volatile Task<DirectSharingSupport> current = Task.FromResult(DirectSharingSupport.Checking);
        internal DirectSharingSupport Current
        {
            get
            {
                var task = current;
                return task.Status == TaskStatus.RanToCompletion ? task.Result : DirectSharingSupport.Checking;
            }
        }
        internal void BeginLogin(Func<DirectSharingSupport> check)
        {
            current = Task.Run(() =>
            {
                try { return check(); }
                catch { return DirectSharingSupport.Unknown; }
            });
        }
    }
}
