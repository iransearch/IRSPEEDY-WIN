using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using IRSpeedyVPN.Services.Libcore;
class Program
{
    static async Task Main()
    {
        await CheckStop(false);
        await CheckStop(true);
    }
    static async Task CheckStop(bool respond)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var watch = Stopwatch.StartNew();
        var call = Task.Run(() => { try { new LibcoreServiceClient("127.0.0.1", port).Stop(); return true; } catch { return false; } });
        using (var peer = await accepted)
        {
            if (respond)
            {
                // Empty protobuf response header (no error, zero body length) + body.
                await peer.GetStream().WriteAsync(new byte[] { 0, 0 }, 0, 2);
            }
            if (await Task.WhenAny(call, Task.Delay(4000)) != call) throw new Exception("Stop exceeded its bound");
            bool result = await call;
            if (result != respond) throw new Exception("Unexpected Stop result");
            if (!respond && watch.ElapsedMilliseconds < 1700) throw new Exception("Deadline fired too early");
            Console.WriteLine("PASS " + (respond ? "responsive Core stops normally" : "silent Core cannot block Stop indefinitely") + " elapsedMs=" + watch.ElapsedMilliseconds);
        }
        listener.Stop();
    }
}
