using Grpc.Core;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Xray
{
    // One instance per connection. Never reloads Xray or interrupts existing streams.
    internal sealed class StartupRouting : IDisposable
    {
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Dictionary<string, List<string>> pending = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, HashSet<string>> observedGroups = new Dictionary<string, HashSet<string>>();
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
                if (((JArray)root["routing"]["balancers"]).Any(b => (string)b["tag"] == tag))
                {
                    pending.Add(tag, rules);
                    string prefix = tag == SmartIpRouting.AiBalancerTag
                        ? SmartIpRouting.AiProxyPrefix : SmartIpRouting.SmartProxyPrefix;
                    observedGroups[tag] = new HashSet<string>(((JArray)root["outbounds"] ?? new JArray())
                        .Select(o => (string)o["tag"]).Where(t => t != null && t.StartsWith(prefix, StringComparison.Ordinal)));
                    var strategy = ((JArray)root["routing"]["balancers"])
                        .First(b => (string)b["tag"] == tag)["strategy"]?["settings"];
                    LogHelper.WriteExLog("[StartupHealth] stage=pool-policy balancer=" + tag
                        + " configured=" + observedGroups[tag].Count + " maxRTT=" + (string)strategy?["maxRTT"]
                        + " tolerance=" + (string)strategy?["tolerance"]);
                    if (rules.Count == 0)
                        LogHelper.WriteExLog("[StartupRoute] stage=waiting-for-health balancer=" + tag
                            + " reason=no-tested-startup-route");
                }
            }
            if (pending.Count == 0)
                return;
            var ping = root["burstObservatory"]?["pingConfig"];
            LogHelper.WriteExLog("[StartupRoute] stage=health-policy interval=" + (string)ping?["interval"]
                + " sampling=" + (string)ping?["sampling"] + " timeout=" + (string)ping?["timeout"]);
            port = FreePortManager.Dequeue();
            root["api"] = new JObject
            {
                ["tag"] = "startup-api", ["listen"] = "127.0.0.1:" + port,
                ["services"] = new JArray("RoutingService", "ObservatoryService")
            };
            config = root.ToString();
        }

        public void Start(int httpPort)
        {
            lock (stop)
            {
                if (disposed || worker != null || port == 0) return;
                worker = Task.Run(async () =>
                {
                    var elapsed = Stopwatch.StartNew();
                    // These two header-only checks run off the connection/UI path.
                    // They measure the production route, not just core construction.
                    var readiness = pending.Keys.Select(group => ProbeFirstResponseAsync(httpPort, group)).ToArray();
                    var reportedErrors = new Dictionary<string, string>();
                    var nextHealthReport = new Dictionary<string, long>();
                    long nextObservation = 0;
                    bool observationSupported = true;
                    XrayRoutingClient client = null;
                    try
                    {
                        // Keep extraction off the login/UI path and ahead of JIT
                        // execution of Grpc.Core's native Channel initialization.
                        NativeGrpcRuntime.Prepare();
                        client = new XrayRoutingClient(port);
                        LogHelper.WriteExLog("[StartupRoute] stage=control-ready transport=grpc-core elapsedMs="
                            + elapsed.ElapsedMilliseconds);
                        while (!stop.IsCancellationRequested && pending.Count > 0)
                        {
                            foreach (string balancer in pending.Keys.ToList())
                            {
                                try
                                {
                                    // This asks the strategy itself, including its RTT and
                                    // failure thresholds, rather than guessing with a timer.
                                    var targets = await client.GetTargetsAsync(balancer, stop.Token);
                                    string prefix = balancer == SmartIpRouting.AiBalancerTag
                                        ? SmartIpRouting.AiProxyPrefix : SmartIpRouting.SmartProxyPrefix;
                                    if (!targets.Any(t => t.StartsWith(prefix, StringComparison.Ordinal)))
                                    {
                                        // A successful RPC with no eligible target is not an
                                        // API failure. Report it periodically instead of waiting silently.
                                        if (!nextHealthReport.TryGetValue(balancer, out var next)
                                            || elapsed.ElapsedMilliseconds >= next)
                                        {
                                            nextHealthReport[balancer] = elapsed.ElapsedMilliseconds + 30000;
                                            LogHelper.WriteExLog("[StartupRoute] stage=waiting-for-health balancer="
                                                + balancer + " reason=" + (targets.Count == 0
                                                    ? "no-eligible-target" : "unexpected-target-prefix")
                                                + " reportedTargets=" + targets.Count
                                                + " startupRules=" + pending[balancer].Count
                                                + " elapsedMs=" + elapsed.ElapsedMilliseconds);
                                        }
                                        continue;
                                    }
                                    foreach (string rule in pending[balancer].ToList())
                                    {
                                        stop.Token.ThrowIfCancellationRequested();
                                        await client.RemoveRuleAsync(rule, stop.Token);
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
                                    if (stop.IsCancellationRequested) break;
                                    string detail = XrayRoutingClient.DescribeError(ex);
                                    if (!reportedErrors.TryGetValue(balancer, out var previous) || previous != detail)
                                    {
                                        reportedErrors[balancer] = detail;
                                        LogHelper.WriteExLog("[StartupRoute] stage=handoff-pending balancer="
                                            + balancer + " " + detail);
                                    }
                                }
                            }
                            if (pending.Count == 0) break;
                            if (observationSupported && elapsed.ElapsedMilliseconds >= nextObservation)
                            {
                                nextObservation = elapsed.ElapsedMilliseconds + 30000;
                                try
                                {
                                    var statuses = await client.GetObservationAsync(stop.Token);
                                    foreach (string group in pending.Keys)
                                        LogHelper.WriteExLog("[StartupHealth] balancer=" + group + " "
                                            + XrayRoutingClient.SummarizeObservation(statuses, observedGroups[group])
                                            + " elapsedMs=" + elapsed.ElapsedMilliseconds);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    var rpc = ex as RpcException;
                                    if (rpc != null && rpc.StatusCode == StatusCode.Unimplemented)
                                        observationSupported = false;
                                    LogHelper.WriteExLog("[StartupHealth] stage=observation-unavailable exception="
                                        + ex.GetType().Name + (rpc == null ? "" : " grpcStatus=" + rpc.StatusCode));
                                }
                            }
                            await Task.Delay(elapsed.Elapsed < TimeSpan.FromSeconds(30) ? 1000 : 10000, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogHelper.WriteExLog("[StartupRoute] stage=control-unavailable "
                            + XrayRoutingClient.DescribeError(ex));
                    }
                    finally
                    {
                        if (client != null) await client.CloseAsync();
                        await Task.WhenAll(readiness);
                    }

                });
            }
        }

        private async Task ProbeFirstResponseAsync(int httpPort, string group)
        {
            var watch = Stopwatch.StartNew();
            string url = group == SmartIpRouting.AiBalancerTag
                ? "https://www.google.com/generate_204" : "https://www.youtube.com/generate_204";
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                deadline.CancelAfter(2000);
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Proxy = new System.Net.WebProxy("127.0.0.1", httpPort);
                request.AllowAutoRedirect = false;
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                using (deadline.Token.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (System.Net.HttpWebResponse)await request.GetResponseAsync())
                        {
                            LogHelper.WriteExLog("[ConnectionStartup] stage=route-response balancer=" + group
                                + " httpStatus=" + (int)response.StatusCode + " elapsedMs=" + watch.ElapsedMilliseconds);
                        }
                    }
                    catch (System.Net.WebException ex)
                    {
                        using (var response = ex.Response as System.Net.HttpWebResponse)
                        {
                            if (!stop.IsCancellationRequested)
                                LogHelper.WriteExLog("[ConnectionStartup] stage=route-probe-failed balancer=" + group
                                    + " reason=" + (deadline.IsCancellationRequested ? "timeout" : ex.Status.ToString())
                                    + " httpStatus=" + (response == null ? "none" : ((int)response.StatusCode).ToString())
                                    + " elapsedMs=" + watch.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!stop.IsCancellationRequested)
                            LogHelper.WriteExLog("[ConnectionStartup] stage=route-probe-failed balancer=" + group
                                + " reason=" + ex.GetType().Name + " elapsedMs=" + watch.ElapsedMilliseconds);
                    }
                }
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

    // Persistent native HTTP/2 channel supports net48 without relying on curl's
    // optional HTTP/2 build features or the OS HTTP stack. No subprocesses/files.
    internal sealed class XrayRoutingClient
    {
        private const string ServiceName = "xray.app.router.command.RoutingService";
        private static readonly Marshaller<byte[]> Bytes = Marshallers.Create<byte[]>(x => x, x => x);
        private static readonly Method<byte[], byte[]> GetInfo = new Method<byte[], byte[]>(
            MethodType.Unary, ServiceName, "GetBalancerInfo", Bytes, Bytes);
        private static readonly Method<byte[], byte[]> Remove = new Method<byte[], byte[]>(
            MethodType.Unary, ServiceName, "RemoveRule", Bytes, Bytes);
        private static readonly Method<byte[], byte[]> Observe = new Method<byte[], byte[]>(MethodType.Unary,
            "xray.core.app.observatory.command.ObservatoryService", "GetOutboundStatus", Bytes, Bytes);
        private readonly Channel channel;
        private readonly CallInvoker invoker;

        internal XrayRoutingClient(int port)
        {
            channel = new Channel("127.0.0.1", port, ChannelCredentials.Insecure, new[]
            {
                new ChannelOption("grpc.enable_http_proxy", 0),
                new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 256 * 1024)
            });
            invoker = channel.CreateCallInvoker();
        }

        internal async Task<List<string>> GetTargetsAsync(string balancer, CancellationToken stop)
        {
            return DecodeTargets(await CallAsync(GetInfo, balancer, stop).ConfigureAwait(false));
        }

        internal async Task RemoveRuleAsync(string rule, CancellationToken stop)
        {
            await CallAsync(Remove, rule, stop).ConfigureAwait(false);
        }

        internal Task CloseAsync() => channel.ShutdownAsync();

        internal async Task<List<ObservedOutbound>> GetObservationAsync(CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            var options = new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(900), cancellationToken: stop);
            using (var call = invoker.AsyncUnaryCall(Observe, null, options, new byte[0]))
                return DecodeObservation(await call.ResponseAsync.ConfigureAwait(false));
        }

        internal sealed class ObservedOutbound
        {
            internal string Tag = "", Reason = "none";
            internal bool Alive;
            internal long Delay, Samples, Failed;
        }

        internal static List<ObservedOutbound> DecodeObservation(byte[] payload)
        {
            var result = new List<ObservedOutbound>();
            foreach (var report in Fields(payload, 1))
                foreach (var bytes in Fields(report, 1))
                {
                    var status = new ObservedOutbound();
                    var reader = new LibcoreProto.ProtoReader(bytes);
                    while (reader.TryReadField(out int field, out int wire))
                    {
                        if (field == 1 && wire == 0) status.Alive = reader.ReadVarint() != 0;
                        else if (field == 2 && wire == 0) status.Delay = unchecked((long)reader.ReadVarint());
                        else if (field == 3 && wire == 2) status.Reason = ClassifyObservationError(reader.ReadString());
                        else if (field == 4 && wire == 2) status.Tag = reader.ReadString();
                        else if (field == 7 && wire == 2)
                        {
                            var measurement = new LibcoreProto.ProtoReader(reader.ReadBytes());
                            while (measurement.TryReadField(out int metric, out int metricWire))
                            {
                                if (metric == 1 && metricWire == 0) status.Samples = unchecked((long)measurement.ReadVarint());
                                else if (metric == 2 && metricWire == 0) status.Failed = unchecked((long)measurement.ReadVarint());
                                else measurement.SkipField(metricWire);
                            }
                        }
                        else reader.SkipField(wire);
                    }
                    result.Add(status);
                }
            return result;
        }

        internal static string ClassifyObservationError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "none";
            error = error.ToLowerInvariant();
            if (error.Contains("lookup") || error.Contains("resolve") || error.Contains("dns")) return "dns";
            if (error.Contains("certificate") || error.Contains("tls") || error.Contains("x509")) return "tls";
            if (error.Contains("timeout") || error.Contains("deadline")) return "timeout";
            if (error.Contains("refused")) return "refused";
            if (error.Contains("status") || error.Contains("204")) return "http-status";
            return "network-or-other";
        }

        internal static string SummarizeObservation(List<ObservedOutbound> statuses, HashSet<string> configured)
        {
            var members = statuses.Where(s => configured.Contains(s.Tag))
                .GroupBy(s => s.Tag).Select(g => g.Last()).ToList();
            var alive = members.Where(s => s.Alive).ToList();
            // Emit aggregate counts and fixed categories only: never raw errors,
            // subscription URLs, hostnames, or untrusted outbound tags.
            return "configured=" + configured.Count + " observed=" + members.Count
                + " missing=" + (configured.Count - members.Count) + " alive=" + alive.Count
                + " minAliveDelayMs=" + (alive.Count == 0 ? "none" : alive.Min(s => s.Delay).ToString())
                + " samples=" + members.Sum(s => s.Samples) + " failedSamples=" + members.Sum(s => s.Failed)
                + " errors=" + string.Join(",", members.GroupBy(s => s.Reason)
                    .OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count()));
        }

        private async Task<byte[]> CallAsync(Method<byte[], byte[]> method, string value, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            var writer = new LibcoreProto.ProtoWriter();
            writer.WriteStringField(1, value);
            var options = new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(900), cancellationToken: stop);
            using (var call = invoker.AsyncUnaryCall(method, null, options, writer.ToArray()))
                return await call.ResponseAsync.ConfigureAwait(false);
        }

        internal static string DescribeError(Exception error)
        {
            // Report actual gRPC status/detail, not a discarded subprocess stderr.
            var rpc = error as RpcException;
            var detail = rpc == null ? error.GetBaseException().Message : rpc.Status.Detail;
            detail = (detail ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (detail.Length > 300) detail = detail.Substring(0, 300);
            return "exception=" + error.GetType().Name
                + (rpc == null ? "" : " grpcStatus=" + rpc.StatusCode)
                + " detail=" + detail;
        }

        internal static List<string> DecodeTargets(byte[] payload)
        {
            var result = new List<string>();
            bool hasPrincipleTarget = false;
            foreach (byte[] message in Fields(payload, 1))
                foreach (byte[] principle in Fields(message, 6))
                {
                    hasPrincipleTarget = true;
                    foreach (byte[] tag in Fields(principle, 1))
                        result.Add(System.Text.Encoding.UTF8.GetString(tag));
                }
            // Xray can return gRPC OK while swallowing GetPrincipleTarget errors.
            // A present, empty message means no healthy candidates; an absent
            // message means the core did not supply strategy health at all.
            if (!hasPrincipleTarget)
                throw new InvalidOperationException("GetBalancerInfo omitted principle_target; core strategy health is unavailable. Check the core routing log and API compatibility.");
            return result;
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
    }
}
