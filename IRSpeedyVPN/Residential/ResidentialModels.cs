using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IRSpeedyVPN.Common.Json;

namespace IRSpeedyVPN.Residential
{
    [JsonConvertible]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class ResidentialConfig
    {
        [JsonProperty("enabled")]
        public bool enabled { get; set; }
        [JsonProperty("subscription_url")]
        public string subscription_url { get; set; }
        public bool IsUsable => enabled && ValidUrl(subscription_url);
        internal static bool ValidUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https"
                && !string.IsNullOrEmpty(uri.Host) && uri.UserInfo.Length == 0
                && uri.Fragment.Length == 0 && uri.Port > 0;
        }
    }

    internal static class ResidentialPresentation
    {
        internal const double ResidentialPingDisplayFactor = 0.7;
        internal static string DisplayIsp(string value) => Regex.Replace(value ?? "", @"\s*[\(\[]\s*AS\d+\s*[\)\]]", "", RegexOptions.IgnoreCase)
            .Trim().TrimEnd(' ', '-', '–', '—', '·');
        // Keep separator decisions in the model; no conditional punctuation in XAML.
        internal static string LocationSeparator(string city, string displayIsp) =>
            !string.IsNullOrWhiteSpace(city) && !string.IsNullOrEmpty(displayIsp) ? " · " : "";
        internal static string LocationLine(string city, string displayIsp) =>
            (city ?? "").Trim() + LocationSeparator(city, displayIsp) + (displayIsp ?? "");
        internal static string ConnectionUri(string value) => (value ?? "").Split('#')[0];
        internal static long DisplayPing(long measured) => measured <= 0 ? measured
            : Math.Max(1, (long)Math.Round(measured * ResidentialPingDisplayFactor, MidpointRounding.AwayFromZero));
        internal static string Digest(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        internal static bool IsEco(object groupId, string name)
        {
            decimal id;
            if (decimal.TryParse(Convert.ToString(groupId, CultureInfo.InvariantCulture), NumberStyles.Number,
                CultureInfo.InvariantCulture, out id) && id == 39) return true;
            name = (name ?? "").Normalize(NormalizationForm.FormKC).Replace('ك', 'ک').Replace('ي', 'ی').Replace('\u200c', ' ');
            return Regex.Split(name, @"[^\p{L}\p{N}]+").Any(s => s.Equals("eco", StringComparison.OrdinalIgnoreCase) || s == "اکو");
        }
        private static string Amount(long bytes, double unit) => (bytes / unit).ToString("0.0", CultureInfo.InvariantCulture);
        internal static string FormatBytes(long bytes) => bytes >= 1073741824L
            ? Amount(bytes, 1073741824d) + " GB" : Amount(bytes, 1048576d) + " MB";
        internal static string Usage(long used, long total)
        {
            if (total == 0) return FormatBytes(used) + " / ∞";
            double unit = total >= 1073741824L ? 1073741824d : 1048576d;
            return Amount(used, unit) + " / " + Amount(total, unit) + (unit > 1048576d ? " GB" : " MB");
        }
    }

    internal sealed class ResidentialTraffic
    {
        internal long Used { get; private set; }
        internal long Total { get; private set; }
        internal long Remaining => Total == 0 ? 0 : Math.Max(0, Total - Used);
        internal double Ratio => Total == 0 ? 1 : Math.Max(0, Math.Min(1, (double)Remaining / Total));
        internal bool Exhausted => Total > 0 && Used >= Total;
        internal string RemainingText => Total == 0 ? "∞" : ResidentialPresentation.FormatBytes(Remaining);
        internal string UsageText => ResidentialPresentation.Usage(Used, Total);
        internal static ResidentialTraffic Parse(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return null;
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in header.Split(';'))
            {
                var pair = part.Trim().Split('=');
                if (pair.Length != 2) return null;
                string key = pair[0].Trim().ToLowerInvariant();
                if (key != "upload" && key != "download" && key != "total") continue;
                long value;
                if (values.ContainsKey(key) || !long.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)) return null;
                values.Add(key, value);
            }
            if (!values.ContainsKey("upload") || !values.ContainsKey("download") || !values.ContainsKey("total")) return null;
            long up = values["upload"], down = values["download"];
            return new ResidentialTraffic { Used = up > long.MaxValue - down ? long.MaxValue : up + down, Total = values["total"] };
        }
    }
}

