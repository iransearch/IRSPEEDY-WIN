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
                    CheckRecoveryTlsAsync("www.microsoft.com", 1500, port, autoSelectorPlan != null, lifetime.Token),
                    CheckRecoveryTlsAsync("www.cloudflare.com", 1500, port, autoSelectorPlan != null, lifetime.Token)).ConfigureAwait(false);
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
            var session = new NetworkRecoverySession(async token =>
            {
                if (userCancelRequested || !IsConnected || generation != Interlocked.Read(ref connectionGeneration)
                    || !ReferenceEquals(gInfo?.CurrentService, this)) throw new OperationCanceledException();
                // TLS must complete through the active local proxy. A successful
                // TCP/SOCKS handshake alone never clears the recovery status.
                var results = await Task.WhenAll(
                    // Keep these outside the AI domain list: an AI-only success
                    // must not claim that the main VPN path has recovered.
                    CheckRecoveryTlsAsync("www.microsoft.com", 5000, port, pooled, token),
                    CheckRecoveryTlsAsync("www.cloudflare.com", 5000, port, pooled, token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (userCancelRequested || generation != Interlocked.Read(ref connectionGeneration)
                    || !ReferenceEquals(gInfo?.CurrentService, this)) throw new OperationCanceledException();
                bool usable = results[0] || results[1];
                Diagnostic("network-return-path", "externalTlsConfirmed=" + (results[0] || results[1])
                    + " mainRouteRequired=" + pooled + " recoveryConfirmed=" + usable);
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
            if (this is Residential.ResidentialService && pooled)
            {
                var startupToken = recoveryLifetime.Token;
                _ = Task.Run(() => RecheckResidentialStartupPoolAsync(generation, startupToken));
            }
        }

        // A transient first probe can leave a one-member Residential pool dead until
        // its 15-minute sweep. Retry that completed, entirely failed round once.
        // Never restart Core, select another country, or interrupt an in-flight round.
        private async Task RecheckResidentialStartupPoolAsync(long generation, CancellationToken cancellation)
        {
            var plan = autoSelectorPlan;
            if (!(this is Residential.ResidentialService) || plan == null) return;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(20000);
                var token = budget.Token;
                Func<bool> current = () => !token.IsCancellationRequested && !userCancelRequested && IsConnected
                    && generation == Interlocked.Read(ref connectionGeneration)
                    && ReferenceEquals(plan, autoSelectorPlan) && ReferenceEquals(gInfo?.CurrentService, this);
                try
                {
                    while (current())
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                        if (!current()) return;
                        var client = new LibcoreServiceClient("127.0.0.1", CorePort, 750);
                        var status = client.QueryAutoSelectors(750, token)?.Groups.FirstOrDefault(g => g.Tag == "proxy");
                        if (status == null || status.Suspended) return;
                        if (status.RoundsCompleted == 0 || status.ProbesInFlight > 0) continue;
                        if (status.MembersAlive > 0 || status.MembersQualified > 0) return;
                        if (status.MembersTotal <= 0 || status.MembersProbed < status.MembersTotal) return;
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        // Serialize with config replacement. A stale worker must never
                        // recheck the next connection's pool. Each RPC is bounded to 750ms.
                        if (!Monitor.TryEnter(connectionLifecycleGate)) return;
                        try
                        {
                            if (!current()) return;
                            status = client.QueryAutoSelectors(750, token)?.Groups.FirstOrDefault(g => g.Tag == "proxy");
                            if (status == null || status.Suspended || status.RoundsCompleted == 0
                                || status.ProbesInFlight > 0 || status.MembersTotal <= 0
                                || status.MembersProbed < status.MembersTotal
                                || status.MembersAlive > 0 || status.MembersQualified > 0 || !current()) return;
                            var result = client.RecheckAutoSelector("proxy", 750);
                            Diagnostic("residential-startup-pool-recheck", "accepted=" + (result != null && string.IsNullOrEmpty(result.Error))
                                + " members=" + status.MembersTotal + " completedRounds=" + status.RoundsCompleted
                                + " reason=initial-round-no-healthy-members attempts=1");
                        }
                        finally { Monitor.Exit(connectionLifecycleGate); }
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    Diagnostic("residential-startup-pool-recheck-stopped", "reason="
                        + (cancellation.IsCancellationRequested ? "connection-canceled" : "budget-expired"));
                }
                catch (Exception ex)
                {
                    // Older/unsupported Core RPCs remain optional, with no retry loop.
                    Diagnostic("residential-startup-pool-recheck-unavailable", "exception=" + ex.GetType().Name);
                }
            }
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

        private Task<bool> CheckRecoveryTlsAsync(string host, int timeoutMs, int port, bool pooled,
            CancellationToken token)
        {
            return ConnectionTlsTest.CheckAsync(host, timeoutMs, port, token,
                pooled ? (Func<int, CancellationToken, Task<bool>>)ConfirmMainPoolRouteAsync : null);
        }

        private Task<bool> ConfirmMainPoolRouteAsync(int sourcePort, CancellationToken token)
        {
            return Task.Run(() =>
            {
                try
                {
                    var connections = new LibcoreServiceClient("127.0.0.1", CorePort, 750).QueryConnections(750, token);
                    return MainPoolRecoveryProof.Confirmed(connections, sourcePort);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Diagnostic("network-return-route-unavailable", "exception=" + ex.GetType().Name);
                    return false;
                }
            }, token);
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
