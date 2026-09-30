using IRSpeedyVPN.Services.Libcore;
using IRSpeedyVPN.Services.Traffic;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "irspeedy-traffic-checks-" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        try
        {
            LedgerChecks();
            ConfigChecks();
            StoreChecks(temp);
            CollectorChecks(temp);
            RpcChecks();
            Console.WriteLine("PASS: traffic deltas, closed-ring deduplication, reset, restart, storage recovery, collector serialization, configuration and RPC deadlines.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }

    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static CoreTrafficConnection C(string id, long up, long down, string name = "firefox.exe") =>
        new CoreTrafficConnection { Id = id, Upload = up, Download = down, Process = name, ProcessPath = @"C:\Apps\" + name };
    static QueryConnectionsResponse R(CoreTrafficConnection active = null, params CoreTrafficConnection[] closed)
    {
        var response = new QueryConnectionsResponse();
        if (active != null) response.Active.Add(active);
        response.Closed.AddRange(closed);
        return response;
    }
    static void Totals(TrafficLedger ledger, long up, long down) =>
        Check(ledger.Snapshot().Sum(r => r.Upload) == up && ledger.Snapshot().Sum(r => r.Download) == down, "Incorrect ledger totals");

    static void LedgerChecks()
    {
        var book = new TrafficLedger();
        book.Apply(R(C("a", 10, 100))); Totals(book, 10, 100);
        book.Apply(R(C("a", 20, 180))); Totals(book, 20, 180);
        book.Apply(R(C("a", 20, 180))); Totals(book, 20, 180);
        // a closes, b opens/closes entirely between polls; repeated closed ring is harmless.
        book.Apply(R(null, C("a", 25, 210), C("b", 3, 30, "Telegram.exe"))); Totals(book, 28, 240);
        book.Apply(R(null, C("a", 25, 210), C("b", 3, 30, "Telegram.exe"))); Totals(book, 28, 240);
        book.Apply(R(C("c", 4, 40), C("c", 6, 65))); Totals(book, 34, 305);
        book.BeginSession();
        book.Apply(R(C("a", 1, 10))); Totals(book, 35, 315);
        book.Reset(R(C("a", 5, 50), C("z", 90, 900)), DateTime.UtcNow);
        book.Apply(R(C("a", 7, 70), C("z", 90, 900))); Totals(book, 2, 20);
        book.Apply(R(null, C("a", 8, 80), C("z", 90, 900))); Totals(book, 3, 30);
        book.Apply(R(null, C("a", 8, 80))); Totals(book, 3, 30);
        var copies = book.Snapshot(); copies[0].Upload = 999;
        Totals(book, 3, 30);
        book.BeginSession();
        book.Apply(R(C("unknown", 2, 3, "")));
        Check(book.Snapshot().Any(r => r.Name == "نامشخص"), "Missing unknown-process bucket");
    }

    static void ConfigChecks()
    {
        var source = JObject.Parse("{ 'route': { 'final': 'proxy', 'rules': [{ 'outbound': 'direct' }] }, 'experimental': { 'cache_file': { 'enabled': true } } }");
        var enabled = JObject.Parse(TrafficConfiguration.Enable(source.ToString()));
        Check((bool)enabled["route"]["find_process"], "Process lookup not enabled");
        Check(JToken.DeepEquals(source["route"]["rules"], enabled["route"]["rules"]), "Routing changed");
        Check(enabled["experimental"]["clash_api"]["external_controller"] == null, "Unexpected network listener");
        Check(JToken.DeepEquals(enabled, JObject.Parse(TrafficConfiguration.Enable(enabled.ToString()))), "Config not idempotent");
    }

    static void StoreChecks(string temp)
    {
        string path = Path.Combine(temp, "history.json");
        var store = new TrafficStore(path);
        var book = new TrafficLedger(); book.Apply(R(C("a", 12, 123)));
        store.Save(book.Snapshot(), book.SinceUtc);
        var restored = new TrafficLedger(); store.Load(restored); Totals(restored, 12, 123);
        book.Apply(R(C("a", 15, 150))); store.Save(book.Snapshot(), book.SinceUtc);
        File.WriteAllText(path, "truncated");
        store.Load(restored); Totals(restored, 12, 123);
        store.Save(new TrafficUsage[0], DateTime.UtcNow, true);
        Check(!File.Exists(path + ".bak"), "Reset left a pre-reset recovery backup");
        store.Load(restored); Totals(restored, 0, 0);
        Check(!File.Exists(path + ".tmp"), "Uncommitted temporary file remains");
    }

    static void CollectorChecks(string temp)
    {
        var store = new TrafficStore(Path.Combine(temp, "collector.json"));
        long up = 10;
        bool fail = false;
        using (var sampled = new ManualResetEventSlim())
        using (var collector = new TrafficUsageService(store, Timeout.Infinite))
        {
            collector.StartSession(timeout =>
            {
                if (fail) throw new IOException("test unavailable");
                var result = R(C("live", Interlocked.Read(ref up), 100));
                sampled.Set(); return result;
            });
            Check(sampled.Wait(2000), "No off-view background sample");
            collector.StopSession(); // final sample + disk flush
            Check(collector.Snapshot.Rows.Single().Upload == 10, "Collector duplicated the final sample");
            sampled.Reset();
            collector.StartSession(timeout => { sampled.Set(); if (fail) throw new IOException(); return R(C("new", up, 100)); });
            Check(sampled.Wait(2000), "Reconnect did not sample");
            collector.ResetAsync().GetAwaiter().GetResult();
            Check(collector.Snapshot.Rows.Length == 0, "Reset failed");
            up = 14;
            collector.StopSession();
            Check(collector.Snapshot.Rows.Single().Upload == 4, "Pre-reset bytes returned");
            collector.StartSession(timeout => { if (fail) throw new IOException(); return R(C("third", 9, 90)); });
            collector.StopSession();
            var before = collector.Snapshot.Rows.Sum(r => r.Upload);
            fail = true;
            collector.StartSession(timeout => { throw new IOException("test unavailable"); });
            bool rejected = false;
            try { collector.ResetAsync().GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Check(rejected && collector.Snapshot.Rows.Sum(r => r.Upload) == before, "Failed reset destroyed history");
            collector.StopSession();
        }
        var restored = new TrafficLedger(); store.Load(restored);
        Check(restored.Snapshot().Sum(r => r.Upload) == 13, "Exit persistence failed");

        // Reset waits behind an in-flight sample. UI Snapshot access stays nonblocking.
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var collector = new TrafficUsageService(new TrafficStore(Path.Combine(temp, "race.json")), Timeout.Infinite))
        {
            int calls = 0;
            collector.StartSession(timeout =>
            {
                if (Interlocked.Increment(ref calls) == 1) { entered.Set(); Check(release.Wait(2000), "Blocked sample timed out"); }
                return R(C("race", 400, 800));
            });
            Check(entered.Wait(2000), "No race sample");
            var clock = Stopwatch.StartNew(); var snapshot = collector.Snapshot;
            Check(clock.ElapsedMilliseconds < 100, "Snapshot blocked the UI");
            var reset = collector.ResetAsync();
            release.Set();
            Check(reset.Wait(2000), "Reset deadlocked");
            collector.StopSession();
            Check(collector.Snapshot.Rows.Length == 0, "In-flight bytes came back after reset");
        }
    }

    static void Varint(Stream stream, ulong value)
    { while (value >= 128) { stream.WriteByte((byte)(value | 128)); value >>= 7; } stream.WriteByte((byte)value); }
    static void Bytes(Stream stream, int field, byte[] bytes)
    { Varint(stream, (ulong)(field * 8 + 2)); Varint(stream, (ulong)bytes.Length); stream.Write(bytes, 0, bytes.Length); }
    static byte[] EncodedRow()
    {
        using (var row = new MemoryStream())
        {
            Bytes(row, 1, Encoding.UTF8.GetBytes("wire-id"));
            Varint(row, 24); Varint(row, 5000000000UL);
            Varint(row, 32); Varint(row, 7000000000UL);
            Bytes(row, 10, Encoding.UTF8.GetBytes("firefox.exe"));
            Bytes(row, 99, Encoding.UTF8.GetBytes("future field"));
            return row.ToArray();
        }
    }
    static ulong ReadVarint(Stream stream)
    {
        ulong result = 0; int shift = 0;
        while (true) { int b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException(); result |= (ulong)(b & 127) << shift; if (b < 128) return result; shift += 7; }
    }
    static byte[] ReadFrame(Stream stream)
    {
        var bytes = new byte[(int)ReadVarint(stream)];
        int offset = 0;
        while (offset < bytes.Length) { int n = stream.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
        return bytes;
    }
    static void Frame(Stream stream, byte[] bytes) { Varint(stream, (ulong)bytes.Length); stream.Write(bytes, 0, bytes.Length); }
    static void RpcChecks()
    {
        byte[] payload;
        using (var stream = new MemoryStream()) { Bytes(stream, 1, EncodedRow()); Bytes(stream, 2, EncodedRow()); payload = stream.ToArray(); }
        var decoded = LibcoreProto.DecodeQueryConnections(payload);
        Check(decoded.Active.Single().Upload == 5000000000L && decoded.Closed.Single().Download == 7000000000L, "64-bit RPC decoding failed");
        bool malformed = false;
        try { LibcoreProto.DecodeQueryConnections(new byte[] { 10, 20, 1 }); } catch (EndOfStreamException) { malformed = true; }
        Check(malformed, "Truncated RPC message accepted");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(() =>
        {
            using (var peer = listener.AcceptTcpClient())
            using (var stream = peer.GetStream())
            {
                Check(Encoding.UTF8.GetString(ReadFrame(stream)).Contains("LibcoreService.QueryConnections"), "Wrong RPC method");
                ReadFrame(stream);
                using (var header = new MemoryStream())
                {
                    Varint(header, 8); Varint(header, 1); Varint(header, 24); Varint(header, (ulong)payload.Length);
                    Frame(stream, header.ToArray()); Frame(stream, payload);
                }
            }
        });
        var response = new LibcoreServiceClient("127.0.0.1", port).QueryConnections();
        Check(response.Active.Single().Process == "firefox.exe", "RPC query roundtrip failed");
        Check(server.Wait(2000), "RPC server hung"); listener.Stop();
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var stall = Task.Run(() => { using (var peer = listener.AcceptTcpClient()) { Thread.Sleep(600); } });
        var watch = Stopwatch.StartNew(); bool ended = false;
        try { new LibcoreServiceClient("127.0.0.1", port).QueryConnections(100); }
        catch (Exception) { ended = true; }
        Check(ended && watch.ElapsedMilliseconds < 550, "Stats RPC has no read deadline");
        stall.Wait(); listener.Stop();
    }
}

namespace IRSpeedyVPN.Common
{
    internal static class LogHelper { public static void WriteExLog(string text) { } }
}
