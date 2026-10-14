using System;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    internal enum ConnectionRecoveryStatus { None, WaitingForInternet, Recovering, Impaired }

    // Observe a network return only; this is not a permanent health-check loop
    // and never changes Core, TUN, proxy settings or the active service.
    internal sealed class NetworkRecoverySession : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<CancellationToken, Task<bool>> confirm;
        private readonly Func<int, CancellationToken, Task> delay;
        private CancellationTokenSource worker;
        private bool disposed;
        private volatile ConnectionRecoveryStatus status;
        internal ConnectionRecoveryStatus Status => status;

        internal NetworkRecoverySession(Func<CancellationToken, Task<bool>> confirm,
            Func<int, CancellationToken, Task> delay = null)
        {
            this.confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            this.delay = delay ?? ((ms, token) => Task.Delay(ms, token));
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
                for (int attempt = 0; ; attempt++)
                {
                    request.Token.ThrowIfCancellationRequested();
                    bool usable;
                    try { usable = await confirm(request.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch { usable = false; }
                    lock (gate)
                    {
                        // An older check must not overwrite a new outage/connection.
                        if (disposed || worker != request || request.IsCancellationRequested) return;
                        status = usable ? ConnectionRecoveryStatus.None : ConnectionRecoveryStatus.Impaired;
                    }
                    if (usable) return;
                    await delay(attempt < 12 ? 5000 : 30000, request.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (gate) { if (worker == request) worker = null; }
                request.Dispose();
            }
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
