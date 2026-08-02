using IRSpeedyVPN.Resource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SpeedyShieldSetting : Window
    {
        private const string RegistryKeyName = "ShieldFilters";
        private const string RegistryFilesKeyName = "ShieldFilterFiles";
        private const char Sep = ';';

        // Filter keys (ShieldFilters)
        private const string K_GOOGLE_ADS = "google-ads";
        private const string K_TRACKERS = "category-ads-all";
        private const string K_ADULT = "category-porn";
        private const string K_SOCIALS = "social";
        private const string K_CRYPTO = "category-cryptocurrency";
        private const string K_GAMBLING = "category-gambling";
        private const string K_PHISHING = "phishing";
        private const string K_MALWARE = "malware";

        // Files (ShieldFilterFiles)
        private const string F_GOOGLE_ADS = "geosite-google-ads.srs";
        private const string F_TRACKERS = "geosite-category-ads-all.srs";
        private static readonly string[] F_ADULT = { "geosite-category-porn.srs", "geosite-nsfw.srs" };
        private const string F_SOCIALS = "geosite-social.srs";
        private const string F_CRYPTO = "geosite-category-cryptocurrency.srs";
        private const string F_GAMBLING = "gambling"; // special: no file, but must be saved
        private static readonly string[] F_PHISHING = { "geosite-phishing.srs", "geoip-phishing.srs" };
        private static readonly string[] F_MALWARE = { "geosite-malware.srs", "geoip-malware.srs" };

        // map filter key -> files to write
        private readonly Dictionary<string, string[]> _filterFilesMap = new Dictionary<string, string[]>()
        {
            { K_GOOGLE_ADS, new[] { F_GOOGLE_ADS } },
            { K_TRACKERS,   new[] { F_TRACKERS } },
            { K_ADULT,      F_ADULT },
            { K_SOCIALS,    new[] { F_SOCIALS } },
            { K_CRYPTO,     new[] { F_CRYPTO } },
            { K_GAMBLING,   new[] { F_GAMBLING } }, // must remain
            { K_PHISHING,   F_PHISHING },
            { K_MALWARE,    F_MALWARE },
        };

        public SpeedyShieldSetting()
        {
            InitializeComponent();

            var selected = ReadListFromRegistry(RegistryKeyName);

            GoogleAdsToggle.IsChecked = selected.Contains(K_GOOGLE_ADS);
            TrackersToggle.IsChecked = selected.Contains(K_TRACKERS);
            AdultToggle.IsChecked = selected.Contains(K_ADULT);
            SocialsToggle.IsChecked = selected.Contains(K_SOCIALS);
            CryptoToggle.IsChecked = selected.Contains(K_CRYPTO);
            GamblingToggle.IsChecked = selected.Contains(K_GAMBLING);
            PhishingToggle.IsChecked = selected.Contains(K_PHISHING);
            MalwareToggle.IsChecked = selected.Contains(K_MALWARE);
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e) => Close();

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            var filters = new List<string>();
            var files = new List<string>();

            void AddIfOn(bool? isOn, string key)
            {
                if (isOn != true) return;

                filters.Add(key);

                if (_filterFilesMap.TryGetValue(key, out var mapped) && mapped != null)
                    files.AddRange(mapped);
            }

            AddIfOn(GoogleAdsToggle.IsChecked, K_GOOGLE_ADS);
            AddIfOn(TrackersToggle.IsChecked, K_TRACKERS);
            AddIfOn(AdultToggle.IsChecked, K_ADULT);
            AddIfOn(SocialsToggle.IsChecked, K_SOCIALS);
            AddIfOn(CryptoToggle.IsChecked, K_CRYPTO);
            AddIfOn(GamblingToggle.IsChecked, K_GAMBLING); // writes "gambling" in files (as you want)
            AddIfOn(PhishingToggle.IsChecked, K_PHISHING);
            AddIfOn(MalwareToggle.IsChecked, K_MALWARE);

            // Write registry
            RegHelper.SetSettingValue(RegistryKeyName, string.Join(Sep.ToString(), filters));

            // Distinct, stable order (preserve first appearance)
            var distinctFiles = DistinctPreserveOrder(files, StringComparer.OrdinalIgnoreCase);
            RegHelper.SetSettingValue(RegistryFilesKeyName, string.Join(Sep.ToString(), distinctFiles));

            Close();
        }

        // --------------------
        // helpers
        // --------------------

        private static HashSet<string> ReadListFromRegistry(string keyName)
        {
            var raw = RegHelper.GetSettingValue(keyName) ?? string.Empty;
            return raw.Split(new[] { Sep }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> DistinctPreserveOrder(IEnumerable<string> items, IEqualityComparer<string> comparer)
        {
            var seen = new HashSet<string>(comparer);
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                var v = item.Trim();
                if (seen.Add(v))
                    yield return v;
            }
        }
    }
}
