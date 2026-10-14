using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private NetworkRecoverySession networkRecovery;
        private long networkRecoveryAfterMs;
        private readonly object poolAvailabilityGate = new object();
        private Task<bool> poolAvailabilityTask = Task.FromResult(true);
        private CancellationTokenSource recoveryLifetime;
        internal ConnectionRecoveryStatus RecoveryStatus => networkRecovery?.Status ?? ConnectionRecoveryStatus.None;

        internal async Task<bool> ConfirmInternetPathAsync()
        {
            long generation = Interlocked.Read(ref connectionGeneration);
            int port = lastListenPort;
            var lifetime = recoveryLifetime;
            if (lifetime == null || userCancelRequested || !IsConnected
                || !ReferenceEquals(gInfo?.CurrentService, this)) return false;
            try
            {
                var results = await Task.WhenAll(
                    ConnectionTlsTest.CheckAsync("www.microsoft.com", 1500, port, lifetime.Token),
                    ConnectionTlsTest.CheckAsync("www.cloudflare.com", 1500, port, lifetime.Token)).ConfigureAwait(false);
                return !lifetime.IsCancellationRequested && !userCancelRequested && IsConnected
                    && generation == Interlocked.Read(ref connectionGeneration)
                    && ReferenceEquals(gInfo?.CurrentService, this) && (results[0] || results[1]);
            }
            catch { return false; }
        }

        internal void ObserveInternetAvailability(bool online)
        {
            if (IsConnected && !userCancelRequested && ReferenceEquals(gInfo?.CurrentService, this))
            {
                if (!online && RecoveryStatus != ConnectionRecoveryStatus.WaitingForInternet)
                {
                    Interlocked.Exchange(ref networkRecoveryAfterMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    QueuePoolAvailability(false, CancellationToken.None);
                }
                networkRecovery?.Observe(online);
            }
        }

        private void BeginNetworkRecoverySession()
        {
            EndNetworkRecoverySession();
            long generation = Interlocked.Read(ref connectionGeneration);
            int port = lastListenPort;
            bool pooled = autoSelectorPlan != null;
            recoveryLifetime = new CancellationTokenSource();
            Interlocked.Exchange(ref networkRecoveryAfterMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var session = new NetworkRecoverySession(async token =>
            {
                if (userCancelRequested || !IsConnected || generation != Interlocked.Read(ref connectionGeneration)
                    || !ReferenceEquals(gInfo?.CurrentService, this)) throw new OperationCanceledException();
                // TLS must complete through the active local proxy. A successful
                // TCP/SOCKS handshake alone never clears the recovery status.
                var results = await Task.WhenAll(
                    // Keep these outside the AI domain list: an AI-only success
                    // must not claim that the main VPN path has recovered.
                    ConnectionTlsTest.CheckAsync("www.microsoft.com", 5000, port, token),
                    ConnectionTlsTest.CheckAsync("www.cloudflare.com", 5000, port, token),
                    Task.Run(() => !pooled || ConfirmMainPoolRecovery(token), token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (userCancelRequested || generation != Interlocked.Read(ref connectionGeneration)
                    || !ReferenceEquals(gInfo?.CurrentService, this)) throw new OperationCanceledException();
                bool usable = results[2] && (results[0] || results[1]);
                Diagnostic("network-return-path", "externalTlsConfirmed=" + (results[0] || results[1])
                    + " mainPoolConfirmed=" + results[2]);
                if (!usable) RequestConnectionState("network-return-failed");
                return usable;
            }, requestRecovery: token => pooled ? QueuePoolAvailability(true, token) : Task.FromResult(true));
            Interlocked.Exchange(ref networkRecovery, session)?.Dispose();
            if (userCancelRequested || generation != Interlocked.Read(ref connectionGeneration))
            {
                Interlocked.CompareExchange(ref networkRecovery, null, session);
                session.Dispose();
                return;
            }
            if (InternetConnectivityMonitor.LastKnownOnline == false)
                ObserveInternetAvailability(false);
        }

        private Task<bool> QueuePoolAvailability(bool online, CancellationToken cancellation)
        {
            long generation = Interlocked.Read(ref connectionGeneration);
            var lifetime = recoveryLifetime;
            if (lifetime == null || autoSelectorPlan == null) return Task.FromResult(true);
            CancellationToken connectionToken = lifetime.Token;
            lock (poolAvailabilityGate)
            {
                var previous = poolAvailabilityTask;
                return poolAvailabilityTask = Task.Run(async () =>
                {
                    // Order down/return messages; a late down must not suspend a
                    // pool that was already told that the internet came back.
                    try { await previous.ConfigureAwait(false); } catch { }
                    using (var request = CancellationTokenSource.CreateLinkedTokenSource(connectionToken, cancellation))
                    {
                        var token = request.Token;
                        if (token.IsCancellationRequested || userCancelRequested || !IsConnected
                            || generation != Interlocked.Read(ref connectionGeneration)
                            || !ReferenceEquals(gInfo?.CurrentService, this)) return false;
                        try
                        {
                            var result = new LibcoreServiceClient("127.0.0.1", CorePort, 750)
                                .SetAutoSelectorNetworkAvailability("proxy", online, 750, token);
                            token.ThrowIfCancellationRequested();
                            bool accepted = result != null && string.IsNullOrEmpty(result.Error);
                            Diagnostic("pool-network-availability", "online=" + online + " accepted=" + accepted);
                            return accepted;
                        }
                        catch (OperationCanceledException) { return false; }
                        catch (Exception ex)
                        {
                            Diagnostic("pool-network-availability-unavailable", "online=" + online
                                + " exception=" + ex.GetType().Name);
                            return false;
                        }
                    }
                });
            }
        }

        private bool ConfirmMainPoolRecovery(CancellationToken token)
        {
            try
            {
                var pools = new LibcoreServiceClient("127.0.0.1", CorePort, 750).QueryAutoSelectors(750, token);
                return MainPoolRecoveryProof.Confirmed(pools, Interlocked.Read(ref networkRecoveryAfterMs));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Diagnostic("network-return-pool-unavailable", "exception=" + ex.GetType().Name);
                return false;
            }
        }

        private void EndNetworkRecoverySession()
        {
            Interlocked.Exchange(ref networkRecovery, null)?.Dispose();
            // Cancellation wakes dedicated RPC sockets; it does not take the
            // connection lifecycle lock or wait for the worker on Disconnect.
            var lifetime = Interlocked.Exchange(ref recoveryLifetime, null);
            if (lifetime != null) lifetime.Cancel();
        }
    }
}
