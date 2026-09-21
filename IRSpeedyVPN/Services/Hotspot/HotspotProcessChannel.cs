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
        private string performanceAttempt = Guid.NewGuid().ToString("N");
        private Stopwatch activationClock;
        internal static bool Installed => HotspotPayload.Included;
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
            string helperPath;
            try { helperPath = MeasureStartup("payload-prepare", HotspotPayload.Prepare); }
            catch { throw new HotspotChannelException("helper-extraction-failed"); }
            cleanupConfirmed = true; // No start/recover request has been issued by this process yet.
            replies = new BlockingCollection<JObject>(128);
            var queue = replies;
            var start = new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(helperPath),
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            process = new Process { StartInfo = start };
            try
            {
                MeasureStartup("process-launch", () =>
                {
                    if (!process.Start()) throw new HotspotChannelException("helper-start-failed");
                    return true;
                });
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
            var ready = MeasureStartup("helper-ready", () =>
            {
                var response = Read(15000);
                Check(response);
                if ((string)response["state"] != "ready" || (int?)response["protocol"] != 2)
                    throw new HotspotChannelException("helper-protocol-mismatch");
                return response;
            });
            if ((bool?)ready["recoveryRequired"] == true)
            {
                cleanupConfirmed = false;
                MeasureStartup("recovery", () => Request("recover"));
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
            LogStartupTimings(value);
            LogDiagnosticEvidence(value);
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
            performanceAttempt = Guid.NewGuid().ToString("N");
            activationClock = Stopwatch.StartNew();
            WriteDiagnostic("app", "version=" + typeof(HotspotProcessChannel).Assembly.GetName().Version
                + " osVersion=" + Environment.OSVersion.Version + " process64Bit=" + Environment.Is64BitProcess);
            try
            {
                MeasureStartup("activation-total", () => { StartCore(tun, ssid, password); return true; });
            }
            catch (Exception ex)
            {
                WriteDiagnostic("channel-failure", "type=" + SafeToken(ex.GetType().Name)
                    + " hresult=" + ex.HResult.ToString("X8")
                    + " code=" + (ex is HotspotChannelException ? SafeToken(((HotspotChannelException)ex).Code) : "unknown"));
                throw;
            }
            finally { activationClock.Stop(); }
        }
        private void StartCore(TunContext tun, string ssid, string password)
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

        private T MeasureStartup<T>(string stage, Func<T> action)
        {
            var clock = Stopwatch.StartNew();
            bool success = false;
            try { var result = action(); success = true; return result; }
            finally
            {
                WritePerformance("app", stage, clock.ElapsedMilliseconds,
                    activationClock == null ? 0 : activationClock.ElapsedMilliseconds, success);
            }
        }

        private void WritePerformance(string clock, string stage, long elapsedMs, long totalMs, bool success)
        {
            try
            {
                LogHelper.WriteLog("[HotspotPerformance] attempt=" + performanceAttempt
                    + " clock=" + clock + " stage=" + stage + " elapsedMs=" + elapsedMs
                    + " totalMs=" + totalMs + " outcome=" + (success ? "ok" : "error"));
            }
            catch { /* Diagnostics must never break startup or cleanup. */ }
        }

        private void LogStartupTimings(JObject value)
        {
            // Success replies contain secrets: only allowlisted timing fields may be logged.
            try
            {
                var timings = value["timings"] as JArray;
                if (timings == null) return;
                int count = 0;
                foreach (var item in timings)
                {
                    if (++count > 32) break;
                    var entry = item as JObject;
                    if (entry == null || entry["Stage"]?.Type != JTokenType.String) continue;
                    string stage = (string)entry["Stage"];
                    switch (stage)
                    {
                        case "preflight-adapters": case "backend-prepare": case "journal-create":
                        case "journal-start": case "publisher-start": case "adapter-ready":
                        case "ics-bind": case "ics-verify": case "clients-enable":
                        case "rollback": case "session-start": break;
                        default: continue;
                    }
                    if (entry["ElapsedMs"]?.Type != JTokenType.Integer ||
                        entry["TotalMs"]?.Type != JTokenType.Integer ||
                        entry["Success"]?.Type != JTokenType.Boolean) continue;
                    long elapsed = (long)entry["ElapsedMs"], total = (long)entry["TotalMs"];
                    if (elapsed < 0 || total < 0) continue;
                    WritePerformance("helper", stage, elapsed, total, (bool)entry["Success"]);
                }
            }
            catch { /* Ignore malformed optional telemetry, not the protocol result. */ }
        }

        private void WriteDiagnostic(string stage, string detail)
        {
            try { LogHelper.WriteLog("[HotspotDiagnostic] attempt=" + performanceAttempt
                + " stage=" + stage + " " + detail); }
            catch { }
        }

        private void LogFields(string stage, JObject value, params string[] fields)
        {
            if (value == null) return;
            var detail = new StringBuilder();
            foreach (string field in fields)
            {
                var token = value[field];
                if (token == null) continue;
                if (token.Type != JTokenType.String && token.Type != JTokenType.Integer &&
                    token.Type != JTokenType.Boolean && token.Type != JTokenType.Null) continue;
                if (detail.Length > 0) detail.Append(' ');
                detail.Append(field == "stage" ? "sourceStage" : field).Append('=');
                if (token.Type == JTokenType.String &&
                    (field == "connectionError" || field == "coreCheckError" || field == "cleanupError"))
                    token = new JValue(((string)token).Replace(':', '-'));
                detail.Append(token.Type == JTokenType.Null ? "none" :
                    SafeToken(token.Type == JTokenType.String ? token : new JValue(token.ToString())));
            }
            WriteDiagnostic(stage, detail.ToString());
        }

        private void LogAdapters(string stage, JToken value)
        {
            var adapters = value as JArray;
            if (adapters == null) return;
            WriteDiagnostic(stage, "count=" + adapters.Count + " logged=" + Math.Min(adapters.Count, 64));
            for (int i = 0; i < adapters.Count && i < 64; i++)
                LogFields(stage + "-adapter", adapters[i] as JObject,
                    "Key", "IsTun", "IsSelectedTun", "IsWifiDirect", "Up", "Role", "Status", "Type", "Present");
        }

        private void LogDiagnosticEvidence(JObject reply)
        {
            // Never serialize whole replies, backend objects, adapter names or raw GUIDs.
            try
            {
                LogFields("environment", reply["environment"] as JObject, "osVersion",
                    "runtimeVersion", "helperVersion", "processArchitecture", "osArchitecture", "backend");
                if ((string)reply["state"] == "ready")
                    LogFields("helper-ready", reply, "protocol", "recoveryRequired");
                if ((bool?)reply["ok"] == false)
                    LogFields("failure", reply, "code", "reason", "stage", "exceptionType", "hresult",
                        "primaryType", "primaryHresult", "cleanupHresult", "cleanupConfirmed",
                        "leaseAgeMs", "coreCheckError", "cleanupError");
                LogAdapters("session-preflight", reply["preflight"]);
                // Only emit backend evidence for start/error responses, not each heartbeat/status.
                if ((bool?)reply["ok"] != false && (string)reply["state"] != "active") return;
                var backend = reply["backendState"] as JObject;
                if (backend != null)
                {
                    LogFields("publisher", backend, "mode", "publisherStatus", "publisherError",
                        "publisherCreated", "connectionError", "pendingConnections");
                    LogAdapters("wfd-preflight", backend["preflight"]);
                    var ics = backend["ics"] as JObject ?? backend;
                    var attempts = ics["enableAttempts"] as JArray;
                    if (attempts != null)
                        for (int i = 0; i < attempts.Count && i < 16; i++)
                            LogFields("ics-attempt", attempts[i] as JObject, "Attempt", "Role", "Result", "Hresult");
                    var preparation = ics["preparation"] as JArray;
                    if (preparation != null)
                        for (int i = 0; i < preparation.Count && i < 16; i++)
                            if (preparation[i].Type == JTokenType.String)
                                WriteDiagnostic("ics-preparation", "result=" + SafeToken(preparation[i]));
                }
                var observations = reply["observations"] as JArray;
                if (observations != null)
                {
                    WriteDiagnostic("ics-observations", "count=" + observations.Count);
                    // Last snapshots are most useful for settle failures; bound log volume.
                    for (int i = Math.Max(0, observations.Count - 8); i < observations.Count; i++)
                    {
                        var observation = observations[i] as JObject;
                        if (observation == null) continue;
                        LogFields("ics-observation", observation, "Phase", "Poll");
                        var adapters = observation["Adapters"] as JArray;
                        if (adapters == null) continue;
                        for (int j = 0; j < adapters.Count && j < 32; j++)
                        {
                            var adapter = adapters[j] as JObject;
                            if (adapter == null) continue;
                            string role = JToken.DeepEquals(adapter["Id"], observation["PublicId"]) ? "public" :
                                JToken.DeepEquals(adapter["Id"], observation["PrivateId"]) ? "private" : "other";
                            LogFields("ics-" + role, adapter, "Up", "Role");
                        }
                    }
                }
                LogFields("health", reply["health"] as JObject, "Reason", "BackendOn", "ExceptionType", "Hresult");
            }
            catch { /* Optional diagnostics cannot change the command's outcome. */ }
        }
    }
}
