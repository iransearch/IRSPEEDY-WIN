using IRSpeedyVPN.Common;
using System;
using System.IO;
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

        // The relay used to swallow every per-connection failure, so a second device that
        // could not browse produced an empty log. These counters make the path observable
        // without flooding log.txt when a browser opens dozens of sockets at once.
        private static int acceptedCount;
        private static long lastProblemLogTicks;
        private static int suppressedProblems;

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
                    Interlocked.Exchange(ref acceptedCount, 0);
                    LogHelper.WriteExLog("Share relay listening on " + listenAddress + ":" + port);
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

                var accepted = Interlocked.Increment(ref acceptedCount);
                if (accepted == 1 || accepted % 25 == 0)
                {
                    LogHelper.WriteExLog(
                        "Share relay accepted connection #" + accepted
                        + " from " + DescribeRemote(inbound));
                }

                ObserveFaults(Task.Run(() => PumpAsync(inbound, targetPort, token)));
            }
        }

        private static async Task PumpAsync(TcpClient inbound, int targetPort, CancellationToken token)
        {
            TcpClient outbound = null;
            var remote = DescribeRemote(inbound);
            try
            {
                inbound.NoDelay = true;
                outbound = new TcpClient { NoDelay = true };

                try
                {
                    await outbound.ConnectAsync(IPAddress.Loopback, targetPort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogProblem(
                        "Share relay could not reach the core at 127.0.0.1:" + targetPort
                        + " for " + remote + " - " + ex.Message);
                    return;
                }

                var inboundStream = inbound.GetStream();
                var outboundStream = outbound.GetStream();

                // A TCP connection has two independent halves. Each direction runs to its
                // own EOF and then half-closes the peer, and the sockets are closed (in the
                // finally below) only once BOTH halves are done. Ending the whole
                // connection on the first half to finish would cut off a response still
                // being delivered - which is why web pages failed while Telegram, whose
                // connection stays open in both directions, kept working.
                var upstream = CopyThenHalfCloseAsync(
                    inboundStream, outboundStream, outbound.Client, token);
                var downstream = CopyThenHalfCloseAsync(
                    outboundStream, inboundStream, inbound.Client, token);

                await Task.WhenAll(upstream, downstream).ConfigureAwait(false);

                var sent = upstream.Result;
                var received = downstream.Result;

                // A connection that carried nothing in a direction never really worked;
                // that is the difference between "blocked before the core" and "data
                // flowed, then something broke".
                if (sent == 0 || received == 0)
                {
                    LogProblem(
                        "Share relay connection carried no data for " + remote
                        + " (toCore=" + sent + " bytes, fromCore=" + received + " bytes)");
                }
            }
            catch (Exception ex)
            {
                // A client going away, or the core restarting, only ends this one connection.
                LogProblem("Share relay connection failed for " + remote + " - " + ex.Message);
            }
            finally
            {
                try { inbound.Close(); } catch { }
                try { outbound?.Close(); } catch { }
            }
        }

        private static string DescribeRemote(TcpClient client)
        {
            try { return client?.Client?.RemoteEndPoint?.ToString() ?? "?"; }
            catch { return "?"; }
        }

        /// <summary>
        /// Reports an abnormal connection at most once every few seconds. A failing page
        /// load opens dozens of sockets at once, and one line each would bury the log.
        /// </summary>
        private static void LogProblem(string message)
        {
            var now = DateTime.UtcNow.Ticks;
            var previous = Interlocked.Read(ref lastProblemLogTicks);
            if (now - previous < TimeSpan.FromSeconds(3).Ticks
                || Interlocked.CompareExchange(ref lastProblemLogTicks, now, previous) != previous)
            {
                Interlocked.Increment(ref suppressedProblems);
                return;
            }

            var alsoSuppressed = Interlocked.Exchange(ref suppressedProblems, 0);
            LogHelper.WriteExLog(
                alsoSuppressed > 0
                    ? message + " (+" + alsoSuppressed + " more like this)"
                    : message);
        }

        /// <summary>
        /// Copies one direction to EOF, then shuts down only the peer's send side so it
        /// sees a clean end-of-stream while the opposite direction keeps draining. Never
        /// throws, so the Task.WhenAll above always completes.
        /// </summary>
        private static async Task<long> CopyThenHalfCloseAsync(
            Stream from, Stream to, Socket toSocket, CancellationToken token)
        {
            long copied = 0;
            try
            {
                // Copied by hand rather than with Stream.CopyToAsync so the byte count is
                // available for the diagnostics above.
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await from.ReadAsync(buffer, 0, buffer.Length, token)
                        .ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await to.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                    copied += read;
                }
            }
            catch
            {
                // The peer went away mid-transfer; the half-close below still ends the
                // other direction.
            }

            try { toSocket.Shutdown(SocketShutdown.Send); } catch { }
            return copied;
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
