using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services.Libcore;

class Program
{
    static async Task Main()
    {
        await CheckUnresponsiveStart(false);
        await CheckUnresponsiveStart(true);
        await CheckResponsiveStart();
    }

    static async Task CheckUnresponsiveStart(bool cancel)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        using (var cancellation = new CancellationTokenSource())
        {
            var clock = Stopwatch.StartNew();
            var call = Task.Run(() =>
            {
                try
                {
                    new LibcoreServiceClient("127.0.0.1", port)
                        .StartWithDeadline(new LoadConfigReq(), 650, cancellation.Token);
                    throw new Exception("Unresponsive Start succeeded");
                }
                catch (OperationCanceledException) when (cancel) { }
                catch (IOException) when (!cancel) { }
            });
            using (var peer = await accepted)
            {
                if (cancel) cancellation.Cancel();
                if (await Task.WhenAny(call, Task.Delay(3000)) != call)
                    throw new Exception("Start did not release its RPC socket");
                await call;
                if (clock.ElapsedMilliseconds > (cancel ? 550 : 2000))
                    throw new Exception("Start exceeded cancellation/deadline bound");
            }
        }
        listener.Stop();
        Console.WriteLine("PASS " + (cancel ? "user cancellation" : "Start deadline"));
    }

    static async Task CheckResponsiveStart()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var call = Task.Run(() => new LibcoreServiceClient("127.0.0.1", port)
            .StartWithDeadline(new LoadConfigReq(), 2000, CancellationToken.None));
        using (var peer = await accepted)
        {
            // Empty protobuf response header and body, as in ExitChecks.
            await peer.GetStream().WriteAsync(new byte[] { 0, 0 }, 0, 2);
            if (await Task.WhenAny(call, Task.Delay(3000)) != call)
                throw new Exception("Responsive Start did not finish");
            var result = await call;
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new Exception("Responsive Start returned an error");
        }
        listener.Stop();
        Console.WriteLine("PASS responsive Start");
    }
}
