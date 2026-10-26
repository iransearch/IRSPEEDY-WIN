using System;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.WebServices
{
    internal interface ILoginConnectTransport : ILoginTransport
    {
        LoginCurlResult CurlObserved(string url, string method, string headers, string body,
            int timeoutMs, CancellationToken cancellation, LoginConnectProgress progress);
    }
    internal interface ILoginPrefetchResolver : ILoginResolver
    {
        LoginAddresses Prefetch(string host, int timeoutMs, CancellationToken cancellation);
        void Accept(string host, LoginAddresses answer);
    }
    internal sealed class LoginConnectProgress
    {
        private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object responseGate = new object();
        private Action responseObserved;
        private int phase; // unknown, connecting, past-connect, finished
        internal Task<bool> StartedTask => started.Task;
        internal bool IsConnecting => Volatile.Read(ref phase) == 1;
        internal void Start() { Interlocked.CompareExchange(ref phase, 1, 0); started.TrySetResult(true); }
        internal void Complete() { Interlocked.Exchange(ref phase, 3); started.TrySetResult(false); }
        internal void OnResponse(Action callback) { lock (responseGate) responseObserved = callback; }
        internal void Observe(string line)
        {
            // Consume only fixed curl verbose milestones. Never retain or log raw
            // stderr, headers or credential data. Unknown state never starts prefetch.
            if (line == null) return;
            if (line.StartsWith("< HTTP/", StringComparison.Ordinal))
            {
                Interlocked.CompareExchange(ref phase, 2, 1);
                lock (responseGate) responseObserved?.Invoke();
            }
            if (line.StartsWith(">", StringComparison.Ordinal)
                || line.StartsWith("* Connected to ", StringComparison.Ordinal)
                || line.StartsWith("* Established connection to ", StringComparison.Ordinal)
                || line.StartsWith("* TLSv", StringComparison.Ordinal)
                || line.StartsWith("* SSL connection ", StringComparison.Ordinal)
                || line.StartsWith("* ALPN:", StringComparison.Ordinal)
                || line.IndexOf("TLS handshake", StringComparison.Ordinal) >= 0)
                Interlocked.CompareExchange(ref phase, 2, 1);
        }
    }
    internal sealed class LoginPrefetch : IDisposable
    {
        private readonly ILoginPrefetchResolver resolver;
        private readonly string host;
        private readonly LoginDeadline deadline;
        private readonly LoginNetworkContext context;
        private readonly LoginConnectProgress progress;
        private readonly CancellationTokenSource stop;
        private readonly object gate = new object();
        private readonly Task<LoginAddresses> task;
        private bool requestedStop, attempted;
        internal LoginPrefetch(ILoginPrefetchResolver resolver, string host, LoginDeadline deadline,
            LoginNetworkContext context, LoginConnectProgress progress)
        {
            this.resolver = resolver; this.host = host; this.deadline = deadline;
            this.context = context; this.progress = progress;
            stop = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation);
            progress.OnResponse(Cancel);
            task = Monitor();
        }
        private void Cancel()
        {
            lock (gate) requestedStop = true;
            stop.Cancel();
        }
        private async Task<LoginAddresses> Monitor()
        {
            if (resolver == null) return null;
            try
            {
                if (!await progress.StartedTask.ConfigureAwait(false)) return null;
                await Task.Delay(1000, stop.Token).ConfigureAwait(false);
                int budget;
                lock (gate)
                {
                    if (requestedStop || !progress.IsConnecting || !context.RecoveryAvailable(host)) return null;
                    deadline.Check();
                    budget = Math.Min(LoginDohProviders.LookupBudgetMs, deadline.RemainingMs - 1000);
                    if (budget <= 100) return null;
                    attempted = true;
                }
                return resolver.Prefetch(host, budget, stop.Token);
            }
            catch (OperationCanceledException) { return null; }
            catch (TimeoutException) { return null; }
            catch { return null; } // A speculative DNS failure never changes the POST result.
        }
        internal LoginAddresses Take(out bool wasAttempted)
        {
            lock (gate) { wasAttempted = attempted; if (!attempted) requestedStop = true; }
            if (!wasAttempted) stop.Cancel();
            return task.GetAwaiter().GetResult();
        }
        internal void Accept(LoginAddresses answer)
        { if (answer != null) { deadline.Check(); resolver.Accept(host, answer); } }
        public void Dispose()
        {
            lock (gate) requestedStop = true;
            // Clear under the observer's lock before disposing its CTS, including
            // an HTTP-header callback already running on curl's stderr reader.
            progress.OnResponse(null);
            progress.Complete(); stop.Cancel();
            // Providers join their canceled I/O before Monitor completes. No
            // background DNS task or child curl is abandoned after a POST result.
            task.GetAwaiter().GetResult(); stop.Dispose();
        }
    }
}
