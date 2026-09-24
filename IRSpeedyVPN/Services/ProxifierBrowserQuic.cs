using IRSpeedyVPN.Common;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    // Owned by one Proxifier connection, never by TUN/System Proxy/Telegram-only mode.
    // A dynamic WFP session also removes its filters if IRSpeedy crashes or is killed.
    internal sealed class ProxifierBrowserQuic : IDisposable
    {
        private readonly object gate = new object();
        private readonly HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private BrowserQuicFilterSession session;
        private Timer timer;
        private bool disposed;
        private int refreshing;

        internal static bool AppliesTo(ProxifierType mode)
        {
            return mode == ProxifierType.Global || mode == ProxifierType.Normal;
        }

        public void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ProxifierBrowserQuic));
                if (session != null) return;
                session = new BrowserQuicFilterSession();
            }
            Refresh(null);
            lock (gate)
            {
                // Include portable browsers started after connection and newly installed paths.
                if (!disposed)
                {
                    LogHelper.WriteLog("[Proxifier QUIC] Temporary IPv4/IPv6 UDP/443 filters: browsers=" + installed.Count);
                    timer = new Timer(Refresh, null, 5000, 5000);
                }
            }
        }

        private void Refresh(object unused)
        {
            if (Interlocked.Exchange(ref refreshing, 1) != 0) return;
            try
            {
                foreach (string path in BrowserExecutableDiscovery.Find())
                {
                    lock (gate)
                    {
                        if (disposed) return;
                        if (installed.Contains(path)) continue;
                        try
                        {
                            session.AddBrowser(path);
                            installed.Add(path);
                        }
                        catch (Exception ex)
                        {
                            if (reported.Add(path))
                                LogHelper.WriteLog("[Proxifier QUIC] Could not protect " + Path.GetFileName(path) + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                    if (!disposed && reported.Add("discovery"))
                        LogHelper.WriteLog("[Proxifier QUIC] Browser discovery failed: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref refreshing, 0); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                timer?.Dispose();
                timer = null;
                session?.Dispose();
                session = null;
                installed.Clear();
            }
        }
    }

    internal static class BrowserExecutableDiscovery
    {
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe",
            "vivaldi.exe", "chromium.exe", "waterfox.exe", "librewolf.exe", "floorp.exe", "zen.exe"
        };

        internal static bool IsBrowser(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            // Accept Windows paths when running the policy tests on non-Windows hosts too.
            return Names.Contains(path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1));
        }

        internal static string ParseExecutable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = Environment.ExpandEnvironmentVariables(value.Trim());
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = value.IndexOf('"', 1);
                return end > 1 ? value.Substring(1, end - 1) : null;
            }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe < 0 ? null : value.Substring(0, exe + 4);
        }

        internal static IEnumerable<string> Find()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = value =>
            {
                string path = ParseExecutable(value);
                if (IsBrowser(path) && Path.IsPathRooted(path) && File.Exists(path)) paths.Add(Path.GetFullPath(path));
            };
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                if (view == RegistryView.Registry64 && !Environment.Is64BitOperatingSystem) continue;
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        foreach (string name in Names)
                            using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name))
                                add(key?.GetValue(null) as string);
                        using (var clients = root.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
                            if (clients != null)
                                foreach (string client in clients.GetSubKeyNames())
                                    using (var command = clients.OpenSubKey(client + @"\shell\open\command"))
                                        add(command?.GetValue(null) as string);
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                foreach (string relative in new[] { @"Google\Chrome\Application\chrome.exe",
                    @"Microsoft\Edge\Application\msedge.exe", @"Mozilla Firefox\firefox.exe",
                    @"BraveSoftware\Brave-Browser\Application\brave.exe", @"Vivaldi\Application\vivaldi.exe",
                    @"Chromium\Application\chrome.exe", @"Programs\Opera\opera.exe" })
                    add(Path.Combine(root, relative));
            }

            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (!IsBrowser(process.ProcessName + ".exe")) continue;
                    // QueryFullProcessImageName works across x86/x64, unlike MainModule.
                    IntPtr handle = OpenProcess(0x1000, false, process.Id);
                    if (handle == IntPtr.Zero) continue;
                    try
                    {
                        int size = 32768;
                        var path = new StringBuilder(size);
                        if (QueryFullProcessImageName(handle, 0, path, ref size)) add(path.ToString());
                    }
                    finally { CloseHandle(handle); }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return paths;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
