using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Hotspot
{
    internal static class DirectSharingProbe
    {
        internal static readonly DirectSharingSession Session = new DirectSharingSession();
        internal static void BeginLoginCheck() => Session.BeginLogin(Check);
        // Called on a worker, never on the WPF dispatcher. This read-only command
        // does not start an AP, load the helper, recover ICS, or require a VPN/TUN.
        internal static DirectSharingSupport Check() => DirectSharingAvailability.Check(
            HotspotProcessChannel.SupportedWindows, HotspotProcessChannel.Installed, ReadCapabilities);

        private static string ReadCapabilities()
        {
            var encoding = Encoding.GetEncoding(CultureInfo.InstalledUICulture.TextInfo.OEMCodePage);
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"),
                    Arguments = "wlan show wirelesscapabilities",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
                };
                if (!process.Start()) return null;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                if (process.ExitCode != 0 || !Task.WaitAll(new Task[] { output, error }, 1000)) return null;
                return output.Result;
            }
        }
    }
}
