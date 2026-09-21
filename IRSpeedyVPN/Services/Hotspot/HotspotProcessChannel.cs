using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IRSpeedyVPN.Resource;

namespace IRSpeedyVPN.Services.Hotspot
{
    // One caller at a time (coordinator gate). Pipes are inherited from the already
    // elevated application, not a machine-wide unauthenticated IPC endpoint.
    internal sealed class HotspotProcessChannel : IHotspotChannel
    {
        private Process process;
        private BlockingCollection<JObject> replies;
        private long sequence;
        private bool stopped;
        private bool cleanupConfirmed;
        private static string HelperPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Hotspot",
            Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86", "IRSpeedyHotspotHelper.exe");
        internal static bool Installed => File.Exists(HelperPath);
        internal static bool SupportedWindows
        {
            get
            {
                // Environment.OSVersion is compatibility-shimmed by the existing app manifest.
                try
                {
                    int build;
                    return int.TryParse(Convert.ToString(Microsoft.Win32.Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "")), out build)
                        && build >= 19041;
                }
                catch { return false; }
            }
        }
        private void Open()
        {
            if (!SupportedWindows) throw new HotspotChannelException("windows-10-2004-or-later-required");
            if (!Installed) throw new HotspotChannelException("helper-missing");
            cleanupConfirmed = true; // No start/recover request has been issued by this process yet.
            replies = new BlockingCollection<JObject>(128);
            var queue = replies;
            var start = new ProcessStartInfo(HelperPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(HelperPath),
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) throw new HotspotChannelException("helper-start-failed");
            }
            catch
            {
                process.Dispose(); process = null;
                throw new HotspotChannelException("helper-start-failed");
            }
            var stdout = process.StandardOutput;
            var stderr = process.StandardError;
            Task.Run(() =>
            {
                try
                {
                    string line;
                    while ((line = stdout.ReadLine()) != null)
                    {
                        if (line.Length > 262144 || !queue.TryAdd(JObject.Parse(line))) break;
                    }
                }
                catch { }
                finally { queue.CompleteAdding(); }
            });
            Task.Run(() => { try { while (stderr.ReadLine() != null) { } } catch { } });
            var ready = Read(15000);
            Check(ready);
            if ((string)ready["state"] != "ready" || (int?)ready["protocol"] != 2) throw new HotspotChannelException("helper-protocol-mismatch");
            if ((bool?)ready["recoveryRequired"] == true)
            {
                cleanupConfirmed = false;
                Request("recover");
                cleanupConfirmed = true;
                // A legacy backend exits after recovery. A fresh process always uses Wi-Fi Direct.
                DisposeProcess();
                Open();
            }
            cleanupConfirmed = true;
        }
        private JObject Read(int timeout)
        {
            JObject value;
            if (!replies.TryTake(out value, timeout)) throw new HotspotChannelException("helper-response-timeout");
            if ((bool?)value["cleanupConfirmed"] == true) cleanupConfirmed = true;
            return value;
        }
        private static string SafeToken(JToken token)
        {
            var value = (string)token;
            if (string.IsNullOrEmpty(value) || value.Length > 100) return "unknown";
            foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_')) return "unknown";
            return value;
        }
        private void Check(JObject value)
        {
            if ((bool?)value["ok"] == true) return;
            string code = SafeToken(value["code"]);
            // Never write the whole reply: successful replies contain the Wi-Fi password.
            LogHelper.WriteLog("[Hotspot] code=" + code + " reason=" + SafeToken(value["reason"])
                + " stage=" + SafeToken(value["stage"]) + " hresult=" + SafeToken(value["hresult"]));
            string reason = SafeToken(value["reason"]);
            bool tunnelLost = code == "session-health-or-lease-lost" &&
                (reason == "core-exited" || reason == "core-pid-reused" || reason == "tun-missing" || reason == "tun-down");
            throw new HotspotChannelException(code, tunnelLost);
        }
        private JObject Request(string command, JObject value = null)
        {
            value = value ?? new JObject();
            long id = ++sequence;
            value["command"] = command; value["requestId"] = id;
            var write = process.StandardInput.WriteLineAsync(value.ToString(Formatting.None));
            if (!write.Wait(3000)) throw new HotspotChannelException("helper-write-timeout");
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 30000)
            {
                var reply = Read(Math.Max(1, 30000 - (int)clock.ElapsedMilliseconds));
                if (reply["requestId"]?.Type == JTokenType.Integer && (long)reply["requestId"] == id)
                { Check(reply); return reply; }
                if ((bool?)reply["ok"] == false)
                {
                    if (command == "stop" && cleanupConfirmed) return new JObject { ["ok"] = true, ["state"] = "stopped" };
                    Check(reply);
                }
            }
            throw new HotspotChannelException("helper-response-timeout");
        }
        public void Start(TunContext tun, string ssid, string password)
        {
            Open();
            cleanupConfirmed = false;
            var reply = Request("start", new JObject
            {
                ["tunId"] = tun.Id.ToString(), ["corePid"] = tun.Pid,
                ["coreStartedUtcTicks"] = tun.StartedUtcTicks,
                ["ssid"] = ssid, ["password"] = password, ["experimental"] = true, ["integrated"] = true
            });
            if ((string)reply["state"] != "active" || (string)reply["ssid"] != ssid || (string)reply["password"] != password)
                throw new HotspotChannelException("helper-protocol-mismatch");
        }
        public int Poll()
        {
            var heartbeat = Request("heartbeat");
            if ((bool?)heartbeat["active"] != true) throw new HotspotChannelException("session-inactive");
            var status = Request("status");
            if ((bool?)status["active"] != true || (bool?)status["recoveryRequired"] == true)
                throw new HotspotChannelException("session-inactive");
            return (int?)status["clientCount"] ?? 0;
        }
        public void Stop()
        {
            if (stopped) return;
            if (process == null) { stopped = true; return; }
            if (!cleanupConfirmed)
            {
                if (process.HasExited)
                {
                    // A hard exit is not proof of ICS cleanup. Recover the ownership journal.
                    DisposeProcess(); Open();
                }
                else
                {
                    var response = Request("stop");
                    if ((string)response["state"] != "stopped") throw new HotspotChannelException("cleanup-not-confirmed");
                    cleanupConfirmed = true;
                }
            }
            DisposeProcess();
            stopped = true;
        }
        private void DisposeProcess()
        {
            if (process == null) return;
            try { process.StandardInput.Close(); } catch { }
            // EOF lets the helper complete its finally cleanup. Do not kill it mid-ICS mutation.
            if (!process.WaitForExit(10000)) throw new HotspotChannelException("helper-exit-timeout");
            process.Dispose(); process = null;
        }
        public void Dispose() { DisposeProcess(); }
    }
}
