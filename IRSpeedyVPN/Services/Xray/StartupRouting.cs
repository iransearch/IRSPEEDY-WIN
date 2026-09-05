using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Xray
{
    // One instance per connection. Never reloads Xray or interrupts existing streams.
    internal sealed class StartupRouting : IDisposable
    {
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Dictionary<string, List<string>> pending = new Dictionary<string, List<string>>();
        private readonly int port;
        private Task worker;
        private bool disposed;

        internal static void AddRules(JObject root, string balancerTag, string testedTag)
        {
            foreach (var balancer in ((JArray)root["routing"]["balancers"]).OfType<JObject>())
                balancer.Remove("fallbackTag");
            if (string.IsNullOrEmpty(testedTag))
                return;
            var rules = (JArray)root["routing"]["rules"];
            foreach (var rule in rules.OfType<JObject>().ToList())
            {
                if ((string)rule["balancerTag"] != balancerTag)
                    continue;
                var startup = (JObject)rule.DeepClone();
                startup.Remove("balancerTag");
                startup["outboundTag"] = testedTag;
                startup["ruleTag"] = "startup-" + balancerTag + "-" + Guid.NewGuid().ToString("N");
                rules.Insert(rules.IndexOf(rule), startup);
            }
            LogHelper.WriteExLog("[StartupRoute] stage=pinned balancer=" + balancerTag + " outbound=" + testedTag);
        }

        public StartupRouting(ref string config)
        {
            var root = JObject.Parse(config);
            foreach (string tag in new[] { SmartIpRouting.SmartBalancerTag, SmartIpRouting.AiBalancerTag })
            {
                var rules = ((JArray)root["routing"]["rules"]).OfType<JObject>()
                    .Select(r => (string)r["ruleTag"])
                    .Where(r => r != null && r.StartsWith("startup-" + tag + "-", StringComparison.Ordinal)).ToList();
                if (rules.Count > 0)
                    pending.Add(tag, rules);
            }
            if (pending.Count == 0)
                return;
            port = FreePortManager.Dequeue();
            root["api"] = new JObject
            {
                ["tag"] = "startup-api", ["listen"] = "127.0.0.1:" + port,
                ["services"] = new JArray("RoutingService")
            };
            config = root.ToString();
        }

        public void Start()
        {
            lock (stop)
            {
                if (disposed || worker != null || port == 0) return;
                worker = Task.Run(async () =>
                {
                    var elapsed = Stopwatch.StartNew();
                    bool reportedError = false;
                    try
                    {
                        while (!stop.IsCancellationRequested && pending.Count > 0)
                        {
                            foreach (string balancer in pending.Keys.ToList())
                            {
                                try
                                {
                                    // This asks the strategy itself, including its RTT and
                                    // failure thresholds, rather than guessing with a timer.
                                    var targets = XrayRoutingClient.GetTargets(port, balancer, stop.Token);
                                    string prefix = balancer == SmartIpRouting.AiBalancerTag
                                        ? SmartIpRouting.AiProxyPrefix : SmartIpRouting.SmartProxyPrefix;
                                    if (!targets.Any(t => t.StartsWith(prefix, StringComparison.Ordinal))) continue;
                                    foreach (string rule in pending[balancer].ToList())
                                    {
                                        stop.Token.ThrowIfCancellationRequested();
                                        XrayRoutingClient.RemoveRule(port, rule, stop.Token);
                                        pending[balancer].Remove(rule);
                                    }
                                    pending.Remove(balancer);
                                    LogHelper.WriteExLog("[StartupRoute] stage=automatic balancer=" + balancer
                                        + " healthyTargets=" + targets.Count + " elapsedMs=" + elapsed.ElapsedMilliseconds);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    // Keep the tested rule on API failure; never silently
                                    // switch to an unready pool or restart the user's tunnel.
                                    if (!reportedError)
                                    {
                                        reportedError = true;
                                        LogHelper.WriteExLog("[StartupRoute] stage=handoff-pending exception="
                                            + ex.GetType().Name + " detail=" + ex.Message);
                                    }
                                }
                            }
                            if (pending.Count == 0) break;
                            await Task.Delay(elapsed.Elapsed < TimeSpan.FromSeconds(30) ? 1000 : 10000, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) { }

                });
            }
        }

        public void Dispose()
        {
            lock (stop)
            {
                if (disposed) return;
                disposed = true;
                stop.Cancel();
                if (port > 0)
                {
                    if (worker == null || worker.IsCompleted) FreePortManager.Enqueue(port);
                    else worker.ContinueWith(_ => FreePortManager.Enqueue(port), TaskScheduler.Default);
                }
            }
        }
    }

    // .NET Framework's HttpWebRequest has no h2c support. Use the architecture-
    // matched bundled curl for these small loopback gRPC calls, no extra DLLs.
    internal static class XrayRoutingClient
    {
        internal static List<string> GetTargets(int port, string balancer, CancellationToken stop)
        {
            return DecodeTargets(Call(port, "GetBalancerInfo", balancer, stop));
        }

        internal static List<string> DecodeTargets(byte[] payload)
        {
            var result = new List<string>();
            foreach (byte[] message in Fields(payload, 1))
                foreach (byte[] principle in Fields(message, 6))
                    foreach (byte[] tag in Fields(principle, 1))
                        result.Add(System.Text.Encoding.UTF8.GetString(tag));
            return result;
        }

        internal static void RemoveRule(int port, string rule, CancellationToken stop)
        {
            Call(port, "RemoveRule", rule, stop);
        }

        private static IEnumerable<byte[]> Fields(byte[] data, int wanted)
        {
            var reader = new LibcoreProto.ProtoReader(data);
            while (reader.TryReadField(out int field, out int wire))
            {
                if (field == wanted && wire == 2) yield return reader.ReadBytes();
                else reader.SkipField(wire);
            }
        }

        private static byte[] Call(int port, string method, string value, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            var writer = new LibcoreProto.ProtoWriter();
            writer.WriteStringField(1, value);
            byte[] body = writer.ToArray();
            byte[] frame = new byte[5 + body.Length];
            for (int i = 0; i < 4; i++) frame[4 - i] = (byte)(body.Length >> (8 * i));
            Buffer.BlockCopy(body, 0, frame, 5, body.Length);
            string headers = Path.GetTempFileName();
            try
            {
                string curl = Path.Combine(AppServices.ResourceManager.TempPath, "curl",
                    Environment.Is64BitOperatingSystem ? "curl64.exe" : "curl32.exe");
                using (var process = new Process())
                using (var output = new MemoryStream())
                {
                    process.StartInfo = new ProcessStartInfo(curl)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        Arguments = "--disable --silent --show-error --noproxy \"*\" --http2-prior-knowledge"
                            + " --connect-timeout 0.3 --max-time 1.2 -H \"Content-Type: application/grpc\""
                            + " -H \"TE: trailers\" -H \"grpc-timeout: 900m\" --data-binary @-"
                            + " --dump-header \"" + headers + "\" http://127.0.0.1:" + port
                            + "/xray.app.router.command.RoutingService/" + method
                    };
                    process.Start();
                    using (stop.Register(() => { try { process.Kill(); } catch { } }))
                    {
                        var read = process.StandardOutput.BaseStream.CopyToAsync(output);
                        var errors = process.StandardError.ReadToEndAsync();
                        process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
                        process.StandardInput.Close();
                        if (!process.WaitForExit(1700))
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("Xray routing API deadline exceeded.");
                        }
                        read.GetAwaiter().GetResult();
                        errors.GetAwaiter().GetResult();
                        stop.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0)
                            throw new IOException("Routing curl exit=" + process.ExitCode);
                        string responseHeaders = File.ReadAllText(headers);
                        var status = Regex.Matches(responseHeaders, @"(?im)^grpc-status:\s*(\d+)\s*$");
                        if (!Regex.IsMatch(responseHeaders, @"(?m)^HTTP/2 200\b")
                            || status.Count == 0 || status[status.Count - 1].Groups[1].Value != "0")
                            throw new IOException("Xray routing API did not acknowledge success.");
                        var response = output.ToArray();
                        if (response.Length < 5 || response[0] != 0)
                            throw new InvalidDataException("Invalid gRPC response frame.");
                        long length = ((long)response[1] << 24) | ((long)response[2] << 16)
                            | ((long)response[3] << 8) | response[4];
                        if (length != response.Length - 5)
                            throw new InvalidDataException("Invalid gRPC response length.");
                        return response.Skip(5).ToArray();
                    }
                }
            }
            finally { try { File.Delete(headers); } catch { } }
        }
    }
}
