using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using IRSpeedyVPN.Services.Libcore;

internal static class AutoSelectorRecheckChecks
{
    public static async Task Run()
    {
        try
        {
            new LibcoreServiceClient("127.0.0.1", 1).RecheckAutoSelector(" ", 100);
            throw new Exception("Blank tag allowed a global recheck");
        }
        catch (ArgumentException) { }
        Console.WriteLine("PASS recheck rejects blank group before connecting");
        await CheckCall(false);
        await CheckCall(true);
        await CheckCall(false, true);
    }

    private static async Task CheckCall(bool stall, bool unsupported = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            var clock = Stopwatch.StartNew();
            var call = Task.Run(() => new LibcoreServiceClient("127.0.0.1", port)
                .RecheckAutoSelector("ai-proxy", 650));
            using (var peer = await accepted)
            {
                var stream = peer.GetStream();
                var header = await ReadFrame(stream);
                var body = await ReadFrame(stream);
                if (!Encoding.UTF8.GetString(header).Contains("LibcoreService.AutoSelectorAction"))
                    throw new Exception("Wrong recheck RPC method");
                var expected = new byte[] { 10, 8 }.Concat(Encoding.UTF8.GetBytes("ai-proxy"))
                    .Concat(new byte[] { 18, 7 }).Concat(Encoding.UTF8.GetBytes("recheck")).ToArray();
                if (!body.SequenceEqual(expected))
                    throw new Exception("Recheck is not scoped exclusively to AI with the Core protobuf schema");
                if (unsupported)
                {
                    var error = Encoding.UTF8.GetBytes("unknown method");
                    var response = new byte[] { (byte)(error.Length + 2), 18, (byte)error.Length }
                        .Concat(error).ToArray();
                    await stream.WriteAsync(response, 0, response.Length);
                }
                else if (!stall) await stream.WriteAsync(new byte[] { 0, 0 }, 0, 2);
                if (await Task.WhenAny(call, Task.Delay(2500)) != call)
                    throw new Exception("Recheck RPC did not release its socket");
                if (unsupported)
                {
                    try { await call; throw new Exception("Unsupported RPC was reported successful"); }
                    catch (InvalidOperationException ex) when (ex.Message == "unknown method") { }
                }
                else if (stall)
                {
                    try { await call; throw new Exception("Unresponsive recheck succeeded"); }
                    catch (IOException) { }
                    if (clock.ElapsedMilliseconds > 2000)
                        throw new Exception("Recheck exceeded deadline");
                }
                else
                {
                    var result = await call;
                    if (result == null || !string.IsNullOrEmpty(result.Error))
                        throw new Exception("Successful recheck returned error");
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine(unsupported ? "PASS unsupported recheck surfaces error" :
            stall ? "PASS recheck deadline" : "PASS AI-only recheck wire request and response");
    }

    private static async Task<byte[]> ReadFrame(NetworkStream stream)
    {
        int length = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            var one = new byte[1];
            if (await stream.ReadAsync(one, 0, 1) != 1) throw new EndOfStreamException();
            length |= (one[0] & 127) << shift;
            if ((one[0] & 128) != 0) continue;
            if (length < 0 || length > 4096) throw new IOException("Unexpected test frame size");
            var result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(result, offset, length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return result;
        }
        throw new IOException("Invalid frame size");
    }
}
