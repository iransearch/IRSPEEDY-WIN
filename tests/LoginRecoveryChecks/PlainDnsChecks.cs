using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.WebServices;
static class PlainDnsChecks
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static byte[] Reply(byte[] query, string address)
    {
        var b = new List<byte>(query); b[2] = 0x81; b[3] = 0x80; b[7] = 1;
        var ip = IPAddress.Parse(address).GetAddressBytes();
        b.AddRange(new byte[] { 0xc0, 12, 0, (byte)(ip.Length == 4 ? 1 : 28), 0, 1, 0, 0, 0, 60, 0, (byte)ip.Length }); b.AddRange(ip); return b.ToArray();
    }
    static bool Reject(Action action) { try { action(); return false; } catch (FormatException) { return true; } }
    static byte[] Read(System.IO.Stream stream, int size)
    {
        var bytes = new byte[size]; int offset = 0;
        while (offset < size) { int n = stream.Read(bytes, offset, size - offset); if (n == 0) throw new Exception("EOF"); offset += n; }
        return bytes;
    }
    internal static void Run()
    {
        byte[] q = LoginPlainDns.BuildQuery("login.test", 1, 77), good = Reply(q, "93.184.216.34");
        Check(LoginPlainDns.Parse(good, "login.test", 1, 77).Single() == "93.184.216.34", "classic DNS validates compressed public A answer");
        Check(Reject(() => LoginPlainDns.Parse(good, "login.test", 1, 78)) && Reject(() => LoginPlainDns.Parse(good, "other.test", 1, 77))
            && Reject(() => LoginPlainDns.Parse(good.Take(good.Length - 1).ToArray(), "login.test", 1, 77)), "classic DNS rejects wrong transaction, question and truncated RDATA");
        Check(LoginPlainDns.Parse(Reply(q, "127.0.0.1"), "login.test", 1, 77).Length == 0, "classic DNS private-only answer cannot win");
        var q6 = LoginPlainDns.BuildQuery("login.test", 28, 90);
        Check(LoginPlainDns.Parse(Reply(q6, "2606:4700:4700::1111"), "login.test", 28, 90).Length == 1
            && !LoginDohCache.IsPublicAddress("::1") && !LoginDohCache.IsPublicAddress("fd00::1")
            && !LoginDohCache.IsPublicAddress("fe80::1") && !LoginDohCache.IsPublicAddress("2001:db8::1"), "classic DNS accepts public AAAA and rejects local/documentation IPv6");
        string pinned = null;
        var transport = new StubTransport { Handler = resolve => { pinned = resolve; return new LoginCurlResult { HttpCode = 200 }; } };
        new LoginRecovery(transport, new LoginDohCache((h, ms, t) => new[] { "2606:4700:4700::1111" }), null, dohFirst: true)
            .Send("https://login.test/", "POST", "", "fixture", 20000, new LoginNetworkContext(CancellationToken.None));
        Check(pinned == "login.test:443:[2606:4700:4700::1111]" && transport.Calls == 1,
            "classic DNS IPv6 cache reaches one credential attempt with bracketed curl resolve address");
        using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var worker = Task.Run(() => { IPEndPoint peer = null; byte[] request = udp.Receive(ref peer); byte[] answer = Reply(request, "93.184.216.34"); udp.Send(answer, answer.Length, peer); });
            var result = LoginPlainDns.Query("login.test", (IPEndPoint)udp.Client.LocalEndPoint, 1, 1000, CancellationToken.None);
            worker.Wait(); Check(result.Length == 1, "real loopback UDP DNS query validates server response without internet");
        }
        using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var tcp = new TcpListener((IPEndPoint)udp.Client.LocalEndPoint); tcp.Start();
            try
            {
                var worker = Task.Run(() =>
                {
                    IPEndPoint peer = null; var request = udp.Receive(ref peer);
                    var truncated = (byte[])request.Clone(); truncated[2] = 0x83; truncated[3] = 0x80;
                    udp.Send(truncated, truncated.Length, peer);
                    using (var client = tcp.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        var prefix = Read(stream, 2); var question = Read(stream, (prefix[0] << 8) | prefix[1]);
                        var answer = Reply(question, "93.184.216.34");
                        stream.WriteByte((byte)(answer.Length >> 8)); stream.WriteByte((byte)answer.Length);
                        stream.Write(answer, 0, answer.Length);
                    }
                });
                var result = LoginPlainDns.Query("login.test", (IPEndPoint)udp.Client.LocalEndPoint, 1, 2000, CancellationToken.None);
                worker.Wait(); Check(result.Length == 1, "truncated classic UDP reply completes over real loopback TCP within the original budget");
            }
            finally { tcp.Stop(); }
        }
        using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var cancel = new CancellationTokenSource(100))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { LoginPlainDns.Query("login.test", (IPEndPoint)udp.Client.LocalEndPoint, 1, 5000, cancel.Token); } catch { }
            Check(watch.ElapsedMilliseconds < 1500, "cancel closes blocked classic DNS socket promptly");
        }
        int live = 0; var logs = new List<string>();
        var winner = LoginDohProviders.Resolve("login.test", 5000, CancellationToken.None, (p, ms, token) =>
        {
            Interlocked.Increment(ref live);
            try { if (p == "bad") return new[] { "127.0.0.1" }; if (p == "good") { Thread.Sleep(30); return new[] { "93.184.216.34" }; }
                token.WaitHandle.WaitOne(ms); token.ThrowIfCancellationRequested(); return new string[0]; }
            finally { Interlocked.Decrement(ref live); }
        }, logs.Add, new[] { "bad", "slow", "good" }, new[] { "bad", "slow", "good" }, true);
        Check(winner.Length == 1 && live == 0 && logs.Any(s => s.Contains("provider=good outcome=winner")), "first valid classic DNS answer wins and cancels/joins slow loser without waiting for its answer");
    }
}
