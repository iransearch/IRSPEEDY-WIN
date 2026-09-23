using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.SplitTunneling
{
    internal static class AppDiscoveryService
    {
        private static readonly object CacheGate = new object();
        private static List<SplitTunnelApp> cache;
        private static DateTime cacheTime;
        public static Task<List<SplitTunnelApp>> ReadAsync(CancellationToken cancellation, bool refresh = false)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (CacheGate)
                if (!refresh && cache != null && DateTime.UtcNow - cacheTime < TimeSpan.FromMinutes(2))
                    return Task.FromResult(new List<SplitTunnelApp>(cache));
            var completion = new TaskCompletionSource<List<SplitTunnelApp>>();
            // Shell COM objects have their own STA worker, never the WPF UI thread.
            var thread = new Thread(() =>
            {
                try
                {
                    var result = Read(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    lock (CacheGate) { cache = result; cacheTime = DateTime.UtcNow; }
                    completion.TrySetResult(new List<SplitTunnelApp>(result));
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }) { IsBackground = true, Name = "IRSpeedy App Discovery" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private static List<SplitTunnelApp> Read(CancellationToken cancellation)
        {
            var apps = new Dictionary<string, SplitTunnelApp>(StringComparer.OrdinalIgnoreCase);
            Action<SplitTunnelApp> add = app => { if (app != null && !apps.ContainsKey(app.Identity)) apps.Add(app.Identity, app); };
            foreach (var root in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            foreach (var shortcut in Shortcuts(Environment.GetFolderPath(root), cancellation))
            {
                cancellation.ThrowIfCancellationRequested();
                var info = ShellLinks.Read(shortcut);
                if (info == null) continue;
                var app = FromExecutable(info.TargetPath, info.Name, "StartMenu");
                if (app != null) app.ShortcutPath = shortcut;
                add(app);
            }
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                if (view == RegistryView.Registry64 && !Environment.Is64BitOperatingSystem) continue;
                try
                {
                    using (var key = RegistryKey.OpenBaseKey(hive, view))
                    {
                        ReadRegistry(key, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", false, add, cancellation);
                        ReadRegistry(key, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true, add, cancellation);
                    }
                }
                catch (Exception ex) when (IsAccessError(ex)) { }
            }
            foreach (int pid in ConnectionTables.PidsWithSockets())
            {
                cancellation.ThrowIfCancellationRequested();
                var handle = OpenProcess(0x1000, false, pid);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    int size = 32768;
                    var path = new StringBuilder(size);
                    if (QueryFullProcessImageName(handle, 0, path, ref size)) add(FromExecutable(path.ToString(), null, "Network"));
                }
                finally { CloseHandle(handle); }
            }
            return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static IEnumerable<string> Shortcuts(string root, CancellationToken token)
        {
            var pending = new Stack<string>();
            if (Directory.Exists(root)) pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = pending.Pop();
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir, "*.lnk");
                    dirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (IsAccessError(ex)) { continue; }
                foreach (string file in files) yield return file;
                foreach (string child in dirs)
                {
                    try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                    catch (Exception ex) when (IsAccessError(ex)) { }
                }
            }
        }

        private static void ReadRegistry(RegistryKey root, string location, bool uninstall, Action<SplitTunnelApp> add, CancellationToken token)
        {
            using (var parent = root.OpenSubKey(location))
            {
                if (parent == null) return;
                foreach (var key in parent.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using (var item = parent.OpenSubKey(key))
                        {
                            if (item == null) continue;
                            string value = (item.GetValue(uninstall ? "DisplayIcon" : "") as string ?? "").Trim();
                            if (value.StartsWith("\""))
                            {
                                int end = value.IndexOf('"', 1);
                                if (end < 0) continue;
                                value = value.Substring(1, end - 1);
                            }
                            else if (uninstall)
                            {
                                int comma = value.LastIndexOf(',');
                                int index;
                                if (comma > 0 && int.TryParse(value.Substring(comma + 1), out index)) value = value.Substring(0, comma);
                            }
                            add(FromExecutable(value, uninstall ? item.GetValue("DisplayName") as string : null, "Registry"));
                        }
                    }
                    catch (Exception ex) when (IsAccessError(ex) || ex is ArgumentException) { }
                }
            }
        }

        public static SplitTunnelApp FromExecutable(string value, string name = null, string source = "Manual")
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var path = AppPathPattern.NormalizeExecutable(value);
            if (path == null) return null;
            // Start Menu shortcuts for Squirrel commonly point to Update.exe,
            // which launches the real app from an app-* directory.
            if (Path.GetFileName(path).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var root = Path.GetDirectoryName(path);
                    var candidates = Directory.GetDirectories(root, "app-*")
                        .OrderByDescending(Directory.GetLastWriteTimeUtc)
                        .SelectMany(d => Directory.GetFiles(d, "*.exe"))
                        .Where(f => !Path.GetFileName(f).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileName(f).IndexOf("squirrel", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                    var main = candidates.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                        .Equals(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase));
                    // Ambiguous launchers must be chosen from the running app or manually.
                    if (main == null && candidates.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                        main = candidates.FirstOrDefault();
                    if (main == null) return null;
                    path = main;
                }
                catch (Exception ex) when (IsAccessError(ex)) { return null; }
            }
            string exe = Path.GetFileName(path);
            if (exe.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                || (name ?? "").IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (source != "Manual" && (IsWithin(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                || IsWithin(path, AppDomain.CurrentDomain.BaseDirectory))) return null;
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch (Exception ex) when (IsAccessError(ex) || ex is ArgumentException) { }
            }
            var app = new SplitTunnelApp { Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
                Path = path, Source = source };
            // Moonlight's Squirrel convention, bounded to one app root/executable.
            // Do not infer broad WindowsApps regex: package identity needs separate work.
            string dir = Path.GetDirectoryName(path);
            string parent = Path.GetDirectoryName(dir);
            if (Path.GetFileName(dir).StartsWith("app-", StringComparison.OrdinalIgnoreCase)
                && parent != null && File.Exists(Path.Combine(parent, "Update.exe")))
            { app.Kind = AppMatchKind.Versioned; app.Root = parent; app.ExeName = exe; }
            return app;
        }

        public static SplitTunnelApp FromFolder(string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\');
            if (!AppPathPattern.IsLocalPath(root) || !Directory.Exists(root) || root.Length <= 3
                || IsWithin(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                || IsWithin(AppDomain.CurrentDomain.BaseDirectory, root)
                || new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                    Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData }
                    .Any(f => string.Equals(root, Environment.GetFolderPath(f).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("پوشهٔ یک برنامه را انتخاب کنید؛ پوشهٔ ویندوز یا ریشهٔ برنامه‌ها مجاز نیست.");
            return new SplitTunnelApp { Name = Path.GetFileName(root), Path = root, Root = root, Kind = AppMatchKind.Folder, Source = "Manual" };
        }

        private static bool IsWithin(string path, string root) => !string.IsNullOrWhiteSpace(root)
            && (string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        private static bool IsAccessError(Exception ex) => ex is System.ComponentModel.Win32Exception || ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException;
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
