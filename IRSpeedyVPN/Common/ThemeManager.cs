using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;

namespace IRSpeedyVPN.Common
{
    /// <summary>
    /// The application owns one palette. Views keep dynamic resource references,
    /// so a change also reaches cached pages and windows created later.
    /// This preference is independent of the account and VPN configuration.
    /// </summary>
    public sealed class ThemeManager : INotifyPropertyChanged
    {
        private const string PreferenceKey = @"Software\IRSpeedyVPN\Appearance";
        private const string PreferenceName = "Theme";
        private ResourceDictionary lightPalette;
        private ResourceDictionary darkPalette;
        private int paletteIndex = -1;
        private bool isDark;

        public static ThemeManager Instance { get; } = new ThemeManager();
        private ThemeManager() { }
        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsDark
        {
            get => isDark;
            set => Apply(value, true);
        }

        public void Initialize()
        {
            Application.Current.Dispatcher.VerifyAccess();
            if (paletteIndex >= 0) return;
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            for (int i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i].Source?.OriginalString.EndsWith("Palette.Light.xaml", StringComparison.Ordinal) == true)
                {
                    lightPalette = dictionaries[i];
                    paletteIndex = i;
                    break;
                }
            }
            if (paletteIndex < 0) throw new InvalidOperationException("Application theme palette is missing.");
            Apply(ReadPreference(), false);
        }

        // Also used by the Windows WPF smoke check without changing the user's preference.
        internal void Apply(bool dark, bool persist)
        {
            Application.Current.Dispatcher.VerifyAccess();
            if (paletteIndex < 0) Initialize();
            if (dark == isDark) return;

            if (dark && darkPalette == null)
                darkPalette = new ResourceDictionary
                {
                    Source = new Uri("/IRSpeedyVPN;component/Themes/Palette.Dark.xaml", UriKind.Relative)
                };

            // Replace the palette at its original precedence. Do not append dictionaries
            // on each click, recreate pages, or enter any connection/startup code.
            Application.Current.Resources.MergedDictionaries[paletteIndex] = dark ? darkPalette : lightPalette;
            isDark = dark;
            if (persist) SavePreference(dark);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        }

        private static bool ReadPreference()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PreferenceKey))
                    return string.Equals(key?.GetValue(PreferenceName) as string, "Dark", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            return false;
        }

        private static void SavePreference(bool dark)
        {
            try
            {
                // One small registry value; synchronous ordering makes rapid toggles
                // and immediate application exit retain the last selected mode.
                using (var key = Registry.CurrentUser.CreateSubKey(PreferenceKey))
                    key?.SetValue(PreferenceName, dark ? "Dark" : "Light", RegistryValueKind.String);
            }
            catch (IOException ex) { Trace.TraceWarning("Theme preference was not saved: " + ex.GetType().Name); }
            catch (UnauthorizedAccessException ex) { Trace.TraceWarning("Theme preference was not saved: " + ex.GetType().Name); }
            catch (SecurityException ex) { Trace.TraceWarning("Theme preference was not saved: " + ex.GetType().Name); }
        }
    }
}
