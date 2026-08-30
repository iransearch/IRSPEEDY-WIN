using IRSpeedyVPN.Common;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    /// <summary>
    /// Exposes the core's loopback proxy to the local network without touching the core's
    /// configuration. The core keeps listening on 127.0.0.1 for its whole lifetime, so
    /// turning sharing on or off never reloads the config and never drops live connections.
    ///
    /// Binding a specific LAN address does not collide with the core's 127.0.0.1 listener on
    /// the same port (unlike 0.0.0.0, which does), so the shared endpoint keeps the very same
    /// port number the app already shows.
    /// </summary>
    internal sealed class ShareRelay : IDisposable
    {
        private const int BufferSize = 32 * 1024;

        private readonly object sync = new object();
        private TcpListener listener;
        private CancellationTokenSource cancellation;

        public IPAddress ListenAddress { get; private set; }
        public int Port { get; private set; }

        public bool IsRunning
        {
            get { lock (sync) return listener != null; }
        }

        /// <summary>
        /// Starts (or re-points) the relay. Returns false if the address could not be bound,
        /// in which case sharing is simply unavailable and the tunnel keeps running.
        /// </summary>
        public bool Start(IPAddress listenAddress, int port)
        {
            if (listenAddress == null || port <= 0)
                return false;

            lock (sync)
            {
                if (listener != null)
                {
                    if (Equals(ListenAddress, listenAddress) && Port == port)
                        return true;
                    StopLocked();
                }

                try
                {
                    var started = new TcpListener(listenAddress, port);
                    started.Start();

                    listener = started;
                    ListenAddress = listenAddress;
                    Port = port;
                    cancellation = new CancellationTokenSource();

                    var token = cancellation.Token;
                    ObserveFaults(Task.Run(() => AcceptLoopAsync(started, port, token)));
                    return true;
                }
                catch (Exception ex)
                {
                    listener = null;
                    LogHelper.WriteExLog(
                        "Share relay could not listen on " + listenAddress + ":" + port
                        + " - " + ex.Message);
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (sync)
                StopLocked();
        }

        private void StopLocked()
        {
            try { cancellation?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            try { cancellation?.Dispose(); } catch { }
            cancellation = null;
            listener = null;
        }

        private static async Task AcceptLoopAsync(TcpListener started, int targetPort, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient inbound;
                try
                {
                    inbound = await started.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The listener was stopped (sharing turned off) or the socket faulted.
                    return;
                }

                ObserveFaults(Task.Run(() => PumpAsync(inbound, targetPort, token)));
            }
        }

        private static async Task PumpAsync(TcpClient inbound, int targetPort, CancellationToken token)
        {
            TcpClient outbound = null;
            try
            {
                inbound.NoDelay = true;
                outbound = new TcpClient { NoDelay = true };
                await outbound.ConnectAsync(IPAddress.Loopback, targetPort).ConfigureAwait(false);

                using (var inboundStream = inbound.GetStream())
                using (var outboundStream = outbound.GetStream())
                {
                    var upstream = inboundStream.CopyToAsync(outboundStream, BufferSize, token);
                    var downstream = outboundStream.CopyToAsync(inboundStream, BufferSize, token);
                    ObserveFaults(upstream);
                    ObserveFaults(downstream);

                    // Once either direction ends the connection is over; closing the sockets
                    // in the finally block unblocks the other copy.
                    await Task.WhenAny(upstream, downstream).ConfigureAwait(false);
                }
            }
            catch
            {
                // A client going away, or the core restarting, only ends this one connection.
            }
            finally
            {
                try { inbound.Close(); } catch { }
                try { outbound?.Close(); } catch { }
            }
        }

        private static void ObserveFaults(Task task)
        {
            if (task == null)
                return;
            task.ContinueWith(
                t => { var ignored = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
