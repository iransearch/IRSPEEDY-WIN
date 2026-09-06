using System;
using Grpc.Core;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using IRSpeedyVPN.Services.Xray;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var root = JObject.Parse(Samples.BalancerConfig);
        // Regression: the old 60m * 3 policy left initial failures stale for hours.
        // Recovery must be scheduled in seconds without extending the startup probe.
        var ping = root["burstObservatory"]["pingConfig"];
        string interval = (string)ping["interval"];
        Check(interval.EndsWith("s", StringComparison.Ordinal)
            && double.TryParse(interval.Substring(0, interval.Length - 1),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out double seconds)
            && seconds >= 10 && seconds * (int)ping["sampling"] <= 30
            && (int)ping["sampling"] > 0, "Health recovery is scheduled too slowly");
        var rules = (JArray)root["routing"]["rules"];
        rules.Insert(1, new JObject { ["type"] = "field", ["balancerTag"] = "ai-balancer",
            ["domain"] = new JArray("domain:google.com") });
        ((JArray)root["routing"]["balancers"]).Add(new JObject {
            ["tag"] = "ai-balancer", ["fallbackTag"] = "ai-proxy-1" });
        var originals = rules.Select(r => r.DeepClone()).ToList();
        StartupRouting.AddRules(root, "smart-balancer-1", "smart-proxy-23");
        StartupRouting.AddRules(root, "ai-balancer", "ai-proxy-5");
        Check(!((JArray)root["routing"]["balancers"]).Any(b => b["fallbackTag"] != null), "Fallback survived");
        Check((string)rules[0]["outboundTag"] == "block", "UDP/443 policy changed");
        Check((string)rules[1]["outboundTag"] == "ai-proxy-5", "AI pin lost its priority");
        var automatic = rules.Where(r => r["ruleTag"] == null).ToList();
        Check(automatic.Count == originals.Count && automatic.Zip(originals, JToken.DeepEquals).All(x => x),
            "Automatic or bypass rules were modified");
        var pinned = rules.OfType<JObject>().Where(r => r["ruleTag"] != null).ToList();
        Check(pinned.Count == 3, "Both smart rules and the AI rule must be pinned");
        foreach (var pin in pinned)
        {
            var next = (JObject)rules[rules.IndexOf(pin) + 1];
            Check(next["balancerTag"] != null, "Pin is not immediately before its automatic rule");
            Check(pin["balancerTag"] == null, "Startup pin still uses the balancer");
        }
        // AI can hand off independently; smart pins and bypass rules stay intact.
        foreach (var pin in pinned.Where(p => ((string)p["ruleTag"]).StartsWith("startup-ai-balancer-"))) pin.Remove();
        Check(rules.Count(r => r["ruleTag"] != null) == 2, "AI handoff removed smart pins");
        foreach (var pin in rules.Where(r => r["ruleTag"] != null).ToList()) pin.Remove();
        Check(rules.Zip(originals, JToken.DeepEquals).All(x => x), "Handoff did not restore original policy");

        var untested = JObject.Parse(Samples.BalancerConfig);
        StartupRouting.AddRules(untested, "smart-balancer-1", null);
        Check(!((JArray)untested["routing"]["rules"]).Any(r => r["ruleTag"] != null), "Untested route was pinned");

        // Golden wire fixture from Xray's command.proto: balancer(1),
        // principle_target(6), repeated tag(1). Include an unknown varint field.
        byte[] reply = { 10, 20, 48, 1, 50, 16, 10, 14, 115, 109, 97, 114, 116, 45, 112, 114, 111, 120, 121, 45, 50, 51 };
        Check(XrayRoutingClient.DecodeTargets(reply).SequenceEqual(new[] { "smart-proxy-23" }), "Balancer decode failed");
        Check(XrayRoutingClient.DecodeTargets(new byte[] { 10, 2, 50, 0 }).Count == 0,
            "Explicit empty principle target is not healthy");
        foreach (var missing in new[] { new byte[0], new byte[] { 10, 2, 42, 0 } })
        {
            bool missingRejected = false;
            try { XrayRoutingClient.DecodeTargets(missing); }
            catch (InvalidOperationException) { missingRejected = true; }
            Check(missingRejected, "Missing strategy health was treated as a healthy API response");
        }
        bool rejected = false;
        try { XrayRoutingClient.DecodeTargets(new byte[] { 10, 127, 1 }); } catch { rejected = true; }
        Check(rejected, "Truncated response accepted");
        // A core that accepts TCP but never answers must not hang startup.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool timedOut = false;
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            new IRSpeedyVPN.Services.Libcore.LibcoreServiceClient("127.0.0.1", port, 300, 150).QueryURLTest();
        }
        catch { timedOut = true; }
        finally { accept.GetAwaiter().GetResult().Dispose(); listener.Stop(); }
        Check(timedOut && clock.ElapsedMilliseconds < 1500, "Unresponsive core exceeded the RPC deadline");
        RunNativePackagingChecks();
        // Native initialization must happen before either the test Server or client.
        string nativePath = NativeGrpcRuntime.Prepare();
        Check(Environment.GetEnvironmentVariable("GRPC_CSHARP_EXT_OVERRIDE_LOCATION") == nativePath,
            "Grpc.Core native path override was not set");
        RunGrpcChecks().GetAwaiter().GetResult();
        Console.WriteLine("Startup routing policy, protobuf, deadline and native gRPC checks passed.");
    }
    private static void RunNativePackagingChecks()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IRSpeedy-native-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (bool is64 in new[] { false, true })
            {
                string architecture = is64 ? "x64" : "x86";
                Func<System.IO.Stream> source = () => typeof(NativeGrpcRuntime).Assembly
                    .GetManifestResourceStream("IRSpeedy.NativeGrpc." + architecture);
                using (var stream = source())
                using (var reader = new System.IO.BinaryReader(stream))
                {
                    Check(reader.ReadUInt16() == 0x5a4d, "Native resource is not PE");
                    stream.Position = 0x3c;
                    int offset = reader.ReadInt32();
                    stream.Position = offset;
                    Check(reader.ReadUInt32() == 0x4550, "Missing PE signature");
                    Check(reader.ReadUInt16() == (is64 ? 0x8664 : 0x14c), "Incorrect embedded architecture");
                }
                string path = NativeGrpcRuntime.Extract(source, root, is64);
                Check(System.IO.File.Exists(path), "Native extraction failed");
                var written = System.IO.File.GetLastWriteTimeUtc(path);
                Check(NativeGrpcRuntime.Extract(source, root, is64) == path, "Cache path changed");
                Check(System.IO.File.GetLastWriteTimeUtc(path) == written, "Valid cache was rewritten");
                System.IO.File.WriteAllText(path, "incomplete/corrupt cache");
                NativeGrpcRuntime.Extract(source, root, is64);
                Check(System.IO.File.ReadAllBytes(path).Take(2).SequenceEqual(new byte[] { 77, 90 }),
                    "Corrupted native cache was not repaired");
                System.IO.File.Delete(path);
                Task.WaitAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                    Check(NativeGrpcRuntime.Extract(source, root, is64) == path, "Concurrent extraction failed"))).ToArray());
                Check(!System.IO.Directory.GetFiles(root, "*.tmp", System.IO.SearchOption.AllDirectories).Any(),
                    "Temporary native files were left behind");
            }
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    private static async Task RunGrpcChecks()
    {
        const string service = "xray.app.router.command.RoutingService";
        var bytes = Marshallers.Create<byte[]>(b => b, b => b);
        var info = new Method<byte[], byte[]>(MethodType.Unary, service, "GetBalancerInfo", bytes, bytes);
        var remove = new Method<byte[], byte[]>(MethodType.Unary, service, "RemoveRule", bytes, bytes);
        bool slow = false, rejectRemove = false;
        int infoCalls = 0, removeCalls = 0;
        var server = new Server();
        server.Ports.Add(new ServerPort("127.0.0.1", 0, ServerCredentials.Insecure));
        server.Services.Add(ServerServiceDefinition.CreateBuilder()
            .AddMethod(info, async (request, context) =>
            {
                Interlocked.Increment(ref infoCalls);
                if (slow) await Task.Delay(5000, context.CancellationToken);
                var reader = new IRSpeedyVPN.Services.Libcore.LibcoreProto.ProtoReader(request);
                Check(reader.TryReadField(out int field, out int wire) && field == 1 && wire == 2,
                    "Incorrect GetBalancerInfo request");
                if (reader.ReadString() == "ai-balancer") return new byte[] { 10, 2, 50, 0 };
                return new byte[] { 10, 18, 50, 16, 10, 14, 115, 109, 97, 114, 116, 45, 112, 114, 111, 120, 121, 45, 50, 51 };
            })
            .AddMethod(remove, (request, context) =>
            {
                Interlocked.Increment(ref removeCalls);
                if (rejectRemove) throw new RpcException(new Status(StatusCode.PermissionDenied, "test refusal"));
                return Task.FromResult(new byte[0]);
            }).Build());
        server.Start();
        var client = new XrayRoutingClient(server.Ports.Single().BoundPort);
        try
        {
            Check((await client.GetTargetsAsync("smart-balancer-1", CancellationToken.None))
                .SequenceEqual(new[] { "smart-proxy-23" }), "Native gRPC healthy target missing");
            Check((await client.GetTargetsAsync("ai-balancer", CancellationToken.None)).Count == 0,
                "AI was marked healthy without a result");
            await client.RemoveRuleAsync("startup-test", CancellationToken.None);
            Check(infoCalls == 2 && removeCalls == 1, "Persistent channel did not carry both methods");
            rejectRemove = true;
            bool rejected = false;
            try { await client.RemoveRuleAsync("startup-test", CancellationToken.None); }
            catch (RpcException ex) { rejected = ex.StatusCode == StatusCode.PermissionDenied
                && XrayRoutingClient.DescribeError(ex).Contains("test refusal"); }
            Check(rejected, "Failed gRPC removal was accepted or lost its error detail");
            slow = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool deadline = false;
            try { await client.GetTargetsAsync("smart-balancer-1", CancellationToken.None); }
            catch (RpcException ex) { deadline = ex.StatusCode == StatusCode.DeadlineExceeded; }
            Check(deadline && clock.ElapsedMilliseconds < 2000, "gRPC health query deadline failed");
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                bool cancelled = false;
                try { await client.GetTargetsAsync("smart-balancer-1", stop.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "A cancelled session issued a health query");
            }
        }
        finally { await client.CloseAsync(); await server.KillAsync(); }
    }

}

// Test-only host adapters; the linked routing and protobuf code is production code.
namespace IRSpeedyVPN
{
    internal static class AppServices
    {
        internal static readonly RuntimePaths ResourceManager = new RuntimePaths();
        internal sealed class RuntimePaths { internal string TempPath => System.IO.Path.GetTempPath(); }
    }
}
namespace IRSpeedyVPN.Common
{
    internal static class LogHelper { internal static void WriteExLog(string value) { } }
    internal static class FreePortManager
    {
        internal static int Dequeue() => throw new NotSupportedException("No core in policy tests");
        internal static void Enqueue(int port) { }
    }
}
namespace IRSpeedyVPN.Services.Xray
{
    internal static class SmartIpRouting
    {
        internal const string SmartBalancerTag = "smart-balancer-1", AiBalancerTag = "ai-balancer";
        internal const string SmartProxyPrefix = "smart-proxy-", AiProxyPrefix = "ai-proxy-";
    }
}
