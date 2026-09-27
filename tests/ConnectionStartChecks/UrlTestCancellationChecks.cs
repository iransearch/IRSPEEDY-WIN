using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services.Libcore;

internal static class UrlTestCancellationChecks
{
    public static async Task Run()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try
            {
                new LibcoreServiceClient("127.0.0.1", 1).TestWithProgress(
                    new TestReq(), null, () => false, null, cancellation.Token);
                throw new Exception("A pre-cancelled test was started");
            }
            catch (OperationCanceledException) { }
        }
        await CheckCancellation(false, false);
        await CheckCancellation(true, false);
        await CheckCancellation(false, true);
        Console.WriteLine("PASS URL test cancellation, preparation race, drain, unsupported query and StopTest deadline");
    }

    private static async Task CheckCancellation(bool unsupportedQuery, bool stalledStop)
    {
        using (var core = new FakeCore(stalledStop))
        using (var cancellation = new CancellationTokenSource())
        {
            var client = new LibcoreServiceClient("127.0.0.1", core.Port);
            Action<TestResp> report = unsupportedQuery ? (Action<TestResp>)(_ => { }) : null;
            var call = Task.Run(() => client.TestWithProgress(new TestReq(), report,
                () => false, null, cancellation.Token));
            await Within(core.TestStarted.Task, "Test request");
            if (unsupportedQuery) await Within(core.QuerySeen.Task, "unsupported QueryURLTest");
            var clock = Stopwatch.StartNew();
            cancellation.Cancel();
            await Within(core.FirstStop.Task, "StopTest before Test completes");
            if (clock.ElapsedMilliseconds > 1500) throw new Exception("Cancellation was delayed");
            if (!stalledStop)
            {
                // First stop is ignored, as by an older core still preparing its box.
                await Within(core.SecondStop.Task, "StopTest retry during preparation");
            }
            if (call.IsCompleted) throw new Exception("Test resources released before core cleanup");
            core.ReleaseTest.TrySetResult(true);
            await Within(call, "Test drain including pending StopTest");
            var stops = core.StopCount;
            // A subsequent test must not inherit a late StopTest from the old worker.
            var next = Task.Run(() => client.TestWithProgress(new TestReq(), null, () => false, null));
            await Within(next, "next test after cancellation");
            if (core.StopCount != stops) throw new Exception("Old cancellation reached the next test");
        }
    }

    private static async Task Within(Task task, string stage)
    {
        if (await Task.WhenAny(task, Task.Delay(4000)) != task)
            throw new Exception("Timeout waiting for " + stage);
        await task;
    }

    private sealed class FakeCore : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly List<TcpClient> peers = new List<TcpClient>();
        private readonly bool stalledStop;
        private int stopCount;
        public int StopCount => Volatile.Read(ref stopCount);
        public int Port { get; }
        public readonly TaskCompletionSource<bool> TestStarted = Signal();
        public readonly TaskCompletionSource<bool> QuerySeen = Signal();
        public readonly TaskCompletionSource<bool> FirstStop = Signal();
        public readonly TaskCompletionSource<bool> SecondStop = Signal();
        public readonly TaskCompletionSource<bool> ReleaseTest = Signal();

        private static TaskCompletionSource<bool> Signal() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeCore(bool stalledStop)
        {
            this.stalledStop = stalledStop;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Accept();
        }

        private async Task Accept()
        {
            try
            {
                while (true)
                {
                    var peer = await listener.AcceptTcpClientAsync();
                    lock (peers) peers.Add(peer);
                    _ = Task.Run(() => Handle(peer));
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        private async Task Handle(TcpClient peer)
        {
            using (peer)
            {
                try
                {
                    var stream = peer.GetStream();
                    var header = Encoding.UTF8.GetString(ReadFrame(stream));
                    ReadFrame(stream);
                    if (header.Contains("LibcoreService.StopTest"))
                    {
                        int count = Interlocked.Increment(ref stopCount);
                        FirstStop.TrySetResult(true);
                        if (count >= 2) SecondStop.TrySetResult(true);
                        if (stalledStop)
                        {
                            // Leave the RPC unanswered until the client's deadline closes it.
                            await Task.Run(() => stream.ReadByte());
                            return;
                        }
                    }
                    else if (header.Contains("LibcoreService.QueryURLTest"))
                    {
                        QuerySeen.TrySetResult(true);
                        return; // Older core: query unavailable, cancellation must still work.
                    }
                    else if (header.Contains("LibcoreService.Test"))
                    {
                        TestStarted.TrySetResult(true);
                        await ReleaseTest.Task;
                    }
                    else throw new Exception("Unexpected RPC");
                    await stream.WriteAsync(new byte[] { 0, 0 }, 0, 2);
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private static byte[] ReadFrame(Stream stream)
        {
            int size = 0, shift = 0, value;
            do
            {
                value = stream.ReadByte();
                if (value < 0 || shift > 28) throw new IOException("Invalid frame");
                size |= (value & 127) << shift;
                shift += 7;
            } while ((value & 128) != 0);
            var bytes = new byte[size];
            for (int offset = 0; offset < size;)
            {
                int read = stream.Read(bytes, offset, size - offset);
                if (read == 0) throw new IOException("Incomplete frame");
                offset += read;
            }
            return bytes;
        }

        public void Dispose()
        {
            listener.Stop();
            ReleaseTest.TrySetResult(true);
            lock (peers) foreach (var peer in peers) peer.Close();
        }
    }
}
