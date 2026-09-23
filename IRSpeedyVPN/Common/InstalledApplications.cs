using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Common
{
    internal sealed class InstalledApplication : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string ExecutableName => System.IO.Path.GetFileName(Path);
        public ImageSource Icon { get; set; }
        private bool selected;
        public bool Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal static class InstalledApplications
    {
        // Only registered executable paths. Do not recursively scan a user's disk,
        // execute apps, infer executables from uninstall commands, or invent entries.
        public static List<InstalledApplication> Read()
        {
            var result = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                if (view == RegistryView.Registry64 && !Environment.Is64BitOperatingSystem) continue;
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        ReadKey(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", false, result);
                        ReadKey(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true, result);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException) { }
            }
            return result.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private static void ReadKey(RegistryKey root, string key, bool uninstall, Dictionary<string, InstalledApplication> result)
        {
            using (var parent = root.OpenSubKey(key))
            {
                if (parent == null) return;
                foreach (var name in parent.GetSubKeyNames())
                {
                    try
                    {
                        using (var child = parent.OpenSubKey(name))
                        {
                            if (child == null) continue;
                            string path = (child.GetValue(uninstall ? "DisplayIcon" : "") as string ?? "").Trim();
                            if (path.StartsWith("\""))
                            {
                                int end = path.IndexOf('"', 1);
                                if (end < 0) continue;
                                path = path.Substring(1, end - 1);
                            }
                            else if (uninstall && path.LastIndexOf(',') > 0)
                            {
                                int iconIndex;
                                int comma = path.LastIndexOf(',');
                                if (int.TryParse(path.Substring(comma + 1), out iconIndex)) path = path.Substring(0, comma).Trim();
                            }
                            path = Environment.ExpandEnvironmentVariables(path);
                            if (!System.IO.Path.IsPathRooted(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                            path = System.IO.Path.GetFullPath(path);
                            if (result.ContainsKey(path)) continue;
                            string title = uninstall ? child.GetValue("DisplayName") as string : null;
                            if (string.IsNullOrWhiteSpace(title)) title = FileVersionInfo.GetVersionInfo(path).FileDescription;
                            if (string.IsNullOrWhiteSpace(title)) title = System.IO.Path.GetFileNameWithoutExtension(path);
                            ImageSource icon = null;
                            using (var native = System.Drawing.Icon.ExtractAssociatedIcon(path))
                            {
                                if (native != null)
                                {
                                    icon = Imaging.CreateBitmapSourceFromHIcon(native.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                                    icon.Freeze();
                                }
                            }
                            result.Add(path, new InstalledApplication { Name = title, Path = path, Icon = icon });
                        }
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException || ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException) { }
                }
            }
        }
    }
}
