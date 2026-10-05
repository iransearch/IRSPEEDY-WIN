using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class ConnectionStateChecks
{
    private static int passed;
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); passed++; }
    internal static async Task Run()
    {
        DecoderAndPrivacy(); ErrorClassificationAndGate();
        await RpcSnapshot(); await RpcSnapshot(true); await RpcFailure(false); await RpcFailure(true); await StaleSnapshot();
        Console.WriteLine("PASS core state diagnostic checks: " + passed);
    }

    private static byte[] Number(int field, long value) => Varint((ulong)(field << 3)).Concat(Varint(unchecked((ulong)value))).ToArray();
    private static byte[] Bytes(int field, byte[] value) => Varint((ulong)((field << 3) | 2)).Concat(Varint((ulong)value.Length)).Concat(value).ToArray();
    private static byte[] Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    private static byte[] Message(params byte[][] fields) => fields.SelectMany(f => f).ToArray();
    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 128) { bytes.Add((byte)(value | 128)); value >>= 7; }
        bytes.Add((byte)value); return bytes.ToArray();
    }
    private static byte[] PoolBody() => Bytes(1, Message(
        Text(1, "proxy"), Text(2, "ready"), Text(3, "smart-proxy-1"), Text(4, "smart-proxy-2"),
        Number(5, 1), Text(6, "round-robin"), Number(7, 0), Number(8, 0), Number(9, 11),
        Number(10, 8), Number(11, 3), Number(12, 3), Number(13, 1), Number(14, 0), Number(15, 2),
        Number(16, 1700000000000), Number(17, 1700000900000), Number(18, 1700000000000),
        Text(19, "PRIVATE_SWITCH_REASON"), Text(21, "private.example"), Number(90, 22),
        Bytes(20, Message(Text(1, "smart-proxy-1"), Number(2, 1), Text(3, "ok"), Number(4, 1), Number(5, 0),
            Number(6, 1), Number(7, 1), Number(8, 123), Number(9, 4), Number(10, 119), Number(11, 129),
            Number(12, 10), Number(13, 1), Number(14, 12), Number(15, 7), Number(16, 5),
            Number(17, 1700000000000), Number(18, 1700000000000), Number(19, 1700000030000),
            Text(20, "dial tcp private.example:443 password=PRIVATE_VALUE: i/o timeout"), Text(99, "future-field")))));
    private static byte[] FlowBody() => Message(
        Bytes(1, Message(Text(1, "flow-private-id"), Number(3, 60), Number(4, 0), Text(5, "proxy"), Text(6, "tcp"),
            Text(7, "api.ipify.org:443"), Text(9, "api.ipify.org"), Text(10, "PRIVATE_PROCESS"), Text(11, "PRIVATE_PATH"),
            Text(12, "proxy"), Text(12, "smart-proxy-1"), Text(14, "192.0.2.1:1234"))),
        Bytes(2, Message(Text(1, "closed-private-id"), Number(3, 10), Number(4, 8), Text(5, "PRIVATE_TAG"), Text(6, "udp"),
            Text(7, "private.example:443"), Text(9, "private.example"), Text(12, "PRIVATE_CHAIN"))));

    private static void DecoderAndPrivacy()
    {
        var pools = LibcoreProto.DecodeQueryAutoSelectors(PoolBody());
        var group = pools.Groups.Single(); var member = group.Members.Single();
        Check(group.Tag == "proxy" && group.Selected == "smart-proxy-1" && group.SelectedUdp == "smart-proxy-2"
            && group.Balance && group.BalanceMode == "round-robin" && !group.Suspended, "Selector identity/selection wire mapping");
        Check(group.MembersTotal == 11 && group.MembersProbed == 8 && group.MembersAlive == 3 && group.MembersQualified == 3
            && group.MembersCooldown == 1 && group.ProbesInFlight == 0 && group.RoundsCompleted == 2, "Runtime counts mapping");
        Check(group.LastRoundMs == 1700000000000 && group.NextRoundMs == 1700000900000 && group.LastSwitchMs == 1700000000000
            && group.Pinned == "private.example", "64-bit timestamps and pinned field 21");
        Check(member.Tag == "smart-proxy-1" && member.Rank == 1 && member.Selected && !member.SelectedUdp && member.Qualified && member.Active
            && member.AverageMs == 123 && member.DeviationMs == 4 && member.MinMs == 119 && member.MaxMs == 129, "Member identity/latency mapping");
        Check(member.Samples == 10 && member.Failures == 1 && member.Probes == 12 && member.DialTotal == 7 && member.DialFail == 5
            && member.LastOkMs == 1700000000000 && member.LastProbeMs == 1700000000000 && member.CooldownUntilMs == 1700000030000,
            "Probe/dial counters and timestamps mapping");
        Check(LibcoreProto.DecodeQueryAutoSelectors(Array.Empty<byte>()).Groups.Count == 0, "Empty response represented explicitly");
        try { LibcoreProto.DecodeQueryAutoSelectors(new byte[] { 10, 7, 10 }); throw new Exception("Truncated status accepted"); }
        catch (EndOfStreamException) { passed++; }
        var flows = LibcoreProto.DecodeQueryConnections(FlowBody());
        Check(flows.Active.Single().DiagnosticTarget == "public-ip-ipify" && flows.Active.Single().Chain.SequenceEqual(new[] { "proxy", "smart-proxy-1" })
            && flows.Closed.Single().DiagnosticTarget == "other", "Flow metadata/provider classification");
        Check(LibcoreProto.DiagnosticTarget("api.ipify.org.evil.example:443") == "other"
            && LibcoreProto.DiagnosticTarget("user@api.ipify.org:443") == "other", "Unrelated destinations never whitelisted");
        var lines = new List<string>(); Action<string, string> write = (s, f) => lines.Add(s + " " + f);
        ConnectionStateDiagnostic.Pools(pools, 1700000001000, write); ConnectionStateDiagnostic.Flows(flows, write);
        string output = string.Join("\n", lines);
        Check(output.Contains("alive=3 qualified=3") && output.Contains("lastProbeAgeMs=1000")
            && output.Contains("cooldownRemainingMs=29000") && output.Contains("dialTotal=7 dialFail=5 lastOkAgeMs=1000"), "Useful pool diagnosis preserved");
        Check(output.Contains("lastDialReason=timeout") && output.Contains("target=public-ip-ipify chain=proxy,smart-proxy-1")
            && output.Contains("upload=60 download=0 zeroDownload=1"), "IP route/zero-download evidence preserved");
        Check(!output.Contains("PRIVATE_") && !output.Contains("private.example") && !output.Contains("192.0.2.1")
            && !output.Contains("flow-private-id") && !output.Contains("api.ipify.org"), "No process/path/domain/IP/error leakage");
        Check(ConnectionStateDiagnostic.Tag("smart-proxy-1\npassword=secret").StartsWith("opaque-"), "Tag log injection blocked");
        string category, details;
        Check(CoreRoutingSignal.TryRead("[Info] [17] app/dispatcher: taking detour [smart-proxy-1] for [tcp:api.ipify.org:443]", out category, out details)
            && category == "public-ip-route" && details.Contains("outbound=smart-proxy-1") && !details.Contains("api.ipify.org"), "IP route classification");
        Check(LogPolicy.UsefulDiagnostic("core-output", "category=public-ip-route") && !LogPolicy.UsefulDiagnostic("core-output", "category=pool-route"), "Retain only diagnostic route signals");
        Check(CoreRoutingSignal.FailureMembers("outbound/socks[smart-proxy-2] timeout PRIVATE_TAG ai-proxy-3 smart-proxy-4.evil")
            == "smart-proxy-2,ai-proxy-3", "Failure tags restricted to generated members");
        for (int i = 0; i < 80; i++) group.Members.Add(new AutoSelectorMember { Tag = "PRIVATE_MEMBER_" + i });
        lines.Clear(); ConnectionStateDiagnostic.Pools(pools, 1700000001000, write);
        Check(lines.Count(l => l.StartsWith("pool-member-state ")) == 64 && lines.Any(l => l.Contains("omittedMembers=17")), "Large snapshots have a logged cap");
    }

    private static void ErrorClassificationAndGate()
    {
        var socket = new SocketException((int)SocketError.AccessDenied);
        string error = NetworkFailureDiagnostic.ExceptionFields(new System.Net.Http.HttpRequestException("PRIVATE_URL",
            new WebException("PRIVATE_AUTH", socket)));
        // NativeErrorCode is errno on Linux and WSA error on Windows.
        Check(error.Contains("socketError=" + socket.SocketErrorCode + " nativeError=" + socket.NativeErrorCode)
            && !error.Contains("PRIVATE_"), "Inner native socket code retained safely");
        Check(NetworkFailureDiagnostic.CoreReason("write udp requested address is not valid in its context") == "address-unavailable"
            && NetworkFailureDiagnostic.CoreReason("timeout: no recent network activity") == "quic-idle-timeout", "UDP address/QUIC causes distinguished");
        var gate = new ConnectionSnapshotGate();
        Check(gate.TryEnter(1, 1000, true) && !gate.TryEnter(1, 4000, false), "Concurrent requests coalesce"); gate.Exit();
        Check(!gate.TryEnter(1, 1500, false) && !gate.TryEnter(1, 4000, true) && gate.TryEnter(1, 4000, false), "Error bursts capped without starving explicit diagnosis"); gate.Exit();
        Check(gate.TryEnter(2, 4001, true), "New connection resets error cooldown"); gate.Exit();
        Check(gate.TryEnter(2, 34001, true), "Later failures remain observable"); gate.Exit();
    }

    private static async Task<byte[]> Frame(NetworkStream stream)
    {
        int length = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            var one = new byte[1];
            if (await stream.ReadAsync(one, 0, 1) != 1) throw new EndOfStreamException();
            length |= (one[0] & 127) << shift;
            if ((one[0] & 128) != 0) continue;
            if (length < 0 || length > 100000) throw new IOException();
            var body = new byte[length]; int offset = 0;
            while (offset < length) { int n = await stream.ReadAsync(body, offset, length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
            return body;
        }
        throw new IOException();
    }
    private static async Task Request(NetworkStream stream, string method)
    {
        byte[] header = await Frame(stream), body = await Frame(stream);
        Check(Encoding.UTF8.GetString(header).Contains("LibcoreService." + method) && body.Length == 0, "Read-only empty request: " + method);
    }
    private static async Task Respond(NetworkStream stream, byte[] body)
    {
        // protorpc ResponseHeader raw_response_len=3.
        byte[] header = Number(3, body.Length);
        byte[] frame = Message(Varint((ulong)header.Length), header, Varint((ulong)body.Length), body);
        await stream.WriteAsync(frame, 0, frame.Length);
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 300 && !ready(); i++) { ConnectionDiagnostics.Flush(); await Task.Delay(10); }
        Check(ready(), "Diagnostic worker did not settle");
    }

    private static async Task RpcSnapshot(bool unsupported = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var records = new ConcurrentQueue<string>(); LogHelper.Sink = records.Enqueue;
        var service = TunnelPlusService.SampleFixture(((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            const string request = "11111111111111111111111111111111";
            service.RequestConnectionState("public-ip-failed", request);
            using (var peer = await listener.AcceptTcpClientAsync())
            {
                await Request(peer.GetStream(), "QueryAutoSelectors");
                if (unsupported)
                {
                    byte[] header = Text(2, "unknown method PRIVATE_CORE_ERROR");
                    byte[] frame = Message(Varint((ulong)header.Length), header);
                    await peer.GetStream().WriteAsync(frame, 0, frame.Length);
                }
                else await Respond(peer.GetStream(), PoolBody());
            }
            using (var peer = await listener.AcceptTcpClientAsync()) { await Request(peer.GetStream(), "QueryConnections"); await Respond(peer.GetStream(), FlowBody()); }
            await Wait(() => records.Any(l => l.Contains("stage=connection-state-end")));
            Check((unsupported ? records.Any(l => l.Contains("stage=pool-state-unavailable")) && records.All(l => !l.Contains("stage=pool-state "))
                : records.Any(l => l.Contains("stage=pool-state ") && l.Contains("balanceMode=round-robin")))
                && records.Any(l => l.Contains("stage=flow-route-state ") && l.Contains("download=0")), "Unavailable API distinguished while flow snapshot continues");
            Check(records.All(l => !l.Contains("PRIVATE_") && !l.Contains("private.example")), "RPC snapshot privacy");
            Check(records.Any(l => l.Contains("relatedRequest=" + request)), "Snapshot joins public-IP request GUID");
            service.RequestConnectionState("core-dial-error"); await Task.Delay(60);
            Check(!listener.Pending(), "Immediate duplicate error does not query again");
        }
        finally { service.EndConnection(); listener.Stop(); }
    }

    private static async Task RpcFailure(bool cancel)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var cts = new CancellationTokenSource())
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var call = Task.Run(() => new LibcoreServiceClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port).QueryAutoSelectors(250, cts.Token));
                using (var peer = await listener.AcceptTcpClientAsync())
                {
                    await Request(peer.GetStream(), "QueryAutoSelectors"); if (cancel) cts.Cancel();
                    try { await call; throw new Exception("Stalled RPC succeeded"); }
                    catch (OperationCanceledException) when (cancel) { passed++; }
                    catch (IOException) when (!cancel) { passed++; }
                    Check(clock.ElapsedMilliseconds < 1500, "Read-only query obeys deadline/cancellation");
                }
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task StaleSnapshot()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var records = new ConcurrentQueue<string>(); LogHelper.Sink = records.Enqueue;
        var service = TunnelPlusService.SampleFixture(((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            string previous = service.DiagnosticIdentity;
            service.RequestConnectionState("public-ip-failed");
            using (var peer = await listener.AcceptTcpClientAsync())
            {
                await Request(peer.GetStream(), "QueryAutoSelectors"); service.NextConnection(); await Respond(peer.GetStream(), PoolBody());
            }
            await Task.Delay(100); ConnectionDiagnostics.Flush();
            Check(records.All(l => !l.Contains("stage=pool-state ") && !l.Contains("stage=flow-route-state ")) && !listener.Pending(),
                "Late snapshot discarded; no flow RPC sent to new connection");
            service.RequestConnectionState("public-ip-failed", expectedIdentity: previous);
            await Task.Delay(60); Check(!listener.Pending(), "Late lookup cannot diagnose different connection");
        }
        finally { service.EndConnection(); listener.Stop(); }
    }
}
