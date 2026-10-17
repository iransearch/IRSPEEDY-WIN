using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal enum ConnectionRecoveryStatus { None, WaitingForInternet, Recovering, Impaired }

    // Observe a network return only; this is not a permanent health-check loop
    // and never changes Core, TUN, proxy settings or the active service.
    internal sealed class NetworkRecoverySession : IDisposable
    {
        internal const int RetryIntervalMs = 15000;
        private readonly object gate = new object();
        private readonly Func<CancellationToken, Task<bool>> confirm;
        private readonly Func<CancellationToken, Task<bool>> requestRecovery;
        private readonly Func<int, CancellationToken, Task> delay;
        private readonly Func<long> elapsedMilliseconds;
        private CancellationTokenSource worker;
        private bool disposed;
        private volatile ConnectionRecoveryStatus status;
        internal ConnectionRecoveryStatus Status => status;

        internal NetworkRecoverySession(Func<CancellationToken, Task<bool>> confirm,
            Func<int, CancellationToken, Task> delay = null,
            Func<CancellationToken, Task<bool>> requestRecovery = null,
            Func<long> elapsedMilliseconds = null)
        {
            this.confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            this.delay = delay ?? ((ms, token) => Task.Delay(ms, token));
            this.requestRecovery = requestRecovery;
            var clock = Stopwatch.StartNew();
            this.elapsedMilliseconds = elapsedMilliseconds ?? (() => clock.ElapsedMilliseconds);
        }

        internal void Observe(bool online)
        {
            CancellationTokenSource cancel = null;
            lock (gate)
            {
                if (disposed) return;
                if (!online)
                {
                    status = ConnectionRecoveryStatus.WaitingForInternet;
                    cancel = worker;
                    worker = null;
                }
                else if (status != ConnectionRecoveryStatus.None && worker == null)
                {
                    status = ConnectionRecoveryStatus.Recovering;
                    var request = worker = new CancellationTokenSource();
                    Task.Run(() => RunAsync(request));
                }
            }
            CancelWithoutWaiting(cancel);
        }

        private async Task RunAsync(CancellationTokenSource request)
        {
            try
            {
                while (true)
                {
                    if (!IsCurrent(request)) return;
                    request.Token.ThrowIfCancellationRequested();
                    long started = elapsedMilliseconds();
                    bool usable;
                    try
                    {
                        // An RPC acknowledgement is not a recovered VPN. Keep
                        // requesting the main Pool sweep until TLS proves its path.
                        // Core coalesces requests while a sweep is still in flight.
                        bool requested = requestRecovery == null
                            || await requestRecovery(request.Token).ConfigureAwait(false);
                        if (!IsCurrent(request)) return;
                        request.Token.ThrowIfCancellationRequested();
                        usable = requested && await confirm(request.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { usable = false; }
                    lock (gate)
                    {
                        // An older check must not overwrite a new outage/connection.
                        if (disposed || worker != request || request.IsCancellationRequested) return;
                        status = usable ? ConnectionRecoveryStatus.None : ConnectionRecoveryStatus.Impaired;
                    }
                    if (usable) return;
                    // Start-to-start cadence includes RPC/TLS time. Slow work
                    // delays the next cycle without queuing overlapping workers.
                    long spent = Math.Max(0, elapsedMilliseconds() - started);
                    int remaining = (int)Math.Max(0, RetryIntervalMs - Math.Min(RetryIntervalMs, spent));
                    await delay(remaining, request.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (gate) { if (worker == request) worker = null; }
                request.Dispose();
            }
        }

        private bool IsCurrent(CancellationTokenSource request)
        {
            lock (gate)
                return !disposed && worker == request && !request.IsCancellationRequested;
        }

        private static void CancelWithoutWaiting(CancellationTokenSource request)
        {
            if (request == null) return;
            Task.Run(() => { try { request.Cancel(); } catch (ObjectDisposedException) { } });
        }

        public void Dispose()
        {
            CancellationTokenSource cancel;
            lock (gate)
            {
                disposed = true;
                cancel = worker;
                worker = null;
                status = ConnectionRecoveryStatus.None;
            }
            CancelWithoutWaiting(cancel);
        }
    }
}
