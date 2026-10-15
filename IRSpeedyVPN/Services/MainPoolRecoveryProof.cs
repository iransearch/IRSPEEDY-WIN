using IRSpeedyVPN.Services.Libcore;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    internal static class MainPoolRecoveryProof
    {
        // Called only after trusted TLS succeeds, while that socket is still open.
        // Pool qualification is a ranking policy, not proof of live reachability:
        // a working fallback can serve traffic while its URL-test history is poor.
        internal static bool Confirmed(QueryConnectionsResponse connections, int sourcePort)
        {
            return sourcePort > 0 && connections?.Active.Any(connection =>
                connection.LoopbackSourcePort == sourcePort && connection.IsRecoveryTarget
                && connection.Network == "tcp" && connection.Chain.Contains("proxy")
                && !connection.Chain.Any(tag => tag == "direct" || tag == "ai-proxy"
                    || tag.StartsWith("ai-proxy-", System.StringComparison.Ordinal))) == true;
        }
    }
}
