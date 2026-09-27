using IRSpeedyVPN.Services;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static async Task Main()
    {
        var state = new ConnectionHealthState();
        var beforeWifi = state.Revision;
        state.Complete(beforeWifi, "203.0.113.1", true);
        state.Invalidate("network changed");
        Assert(state.Address == null, "Network change must immediately clear old IP");
        state.Complete(beforeWifi, "203.0.113.1", true);
        Assert(state.Address == null, "Old Wi-Fi probe must not restore connected state");
        var current = state.Revision;
        for (int i = 0; i < 10; i++)
            Assert(!state.Complete(current, null, false), "Offline must wait, not restart Core repeatedly");
        Assert(!state.Complete(current, null, true), "A single failed endpoint round must not restart Core");
        Assert(state.Complete(current, null, true), "Persistent failure must trigger recovery");
        state.Complete(current, "203.0.113.2", true);
        Assert(state.Address == "203.0.113.2", "Reconnect must publish fresh IP");
        Assert(!state.Complete(current, null, true), "Success must reset failure streak");
        state.Invalidate("disconnected");
        state.Complete(current, "203.0.113.2", true);
        Assert(state.Address == null, "Late success after disconnect must be discarded");

        // A failing local proxy must receive BOTH endpoint attempts. No fallback
        // may go to an external endpoint directly, even if the OS has no proxy.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            foreach (var host in new[] { "api.ipify.org:443", "checkip.amazonaws.com:443" })
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                using (var reader = new StreamReader(stream))
                {
                    var first = await reader.ReadLineAsync();
                    Assert(first.StartsWith("CONNECT " + host + " "), "Health check bypassed explicit proxy");
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }
                    var bytes = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n");
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }
        });
        Assert(await TunnelPlusService.ProbePathAsync(port, CancellationToken.None) == null,
            "Proxy failure must not be reported as healthy");
        await server.WaitAsync(TimeSpan.FromSeconds(3));
        listener.Stop();

        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using (var cancel = new CancellationTokenSource())
        {
            var probe = TunnelPlusService.ProbePathAsync(port, cancel.Token);
            using (var socket = await listener.AcceptTcpClientAsync())
            {
                cancel.Cancel();
                try { await probe.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { }
            }
        }
        listener.Stop();
        Console.WriteLine("PASS: stale Wi-Fi/disconnect results, offline wait, failure threshold, fresh IP, explicit proxy and cancellation");
    }
}
