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
        internal ConnectionRecoveryStatus RecoveryStatus => networkRecovery?.Status ?? ConnectionRecoveryStatus.None;

        internal void ObserveInternetAvailability(bool online)
        {
            if (IsConnected && !userCancelRequested && ReferenceEquals(gInfo?.CurrentService, this))
            {
                if (!online && RecoveryStatus != ConnectionRecoveryStatus.WaitingForInternet)
                    Interlocked.Exchange(ref networkRecoveryAfterMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                networkRecovery?.Observe(online);
            }
        }

        private void BeginNetworkRecoverySession()
        {
            EndNetworkRecoverySession();
            long generation = Interlocked.Read(ref connectionGeneration);
            int port = lastListenPort;
            bool pooled = autoSelectorPlan != null;
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
            });
            Interlocked.Exchange(ref networkRecovery, session)?.Dispose();
            if (userCancelRequested || generation != Interlocked.Read(ref connectionGeneration))
            {
                Interlocked.CompareExchange(ref networkRecovery, null, session);
                session.Dispose();
                return;
            }
            if (InternetConnectivityMonitor.LastKnownOnline == false)
                session.Observe(false);
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
        }
    }
}
