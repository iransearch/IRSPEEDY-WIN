using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private readonly ConnectionSnapshotGate connectionSnapshotGate = new ConnectionSnapshotGate();

        internal void RequestConnectionState(string reason, string request = null, int delayMs = 0, string expectedIdentity = null)
        {
            // Callers use fixed causes. Core output can belong to an idle reader,
            // so that caller must dispatch this to gInfo.CurrentService instead.
            if (reason != "startup" && reason != "public-ip-first-failure" && reason != "public-ip-failed"
                && reason != "connection-test-failed" && reason != "core-dial-error") return;
            if (expectedIdentity != null && expectedIdentity != DiagnosticIdentity) return;
            long generation = Interlocked.Read(ref connectionGeneration);
            string connection = diagnosticConnectionId;
            Guid related;
            string relatedRequest = request == null ? "none" : Guid.TryParse(request, out related)
                ? related.ToString("N") : "opaque-" + ConnectionDiagnostics.Fingerprint(request);
            string context = DiagnosticIdentity + " cause=" + reason
                + " relatedRequest=" + relatedRequest
                + " snapshot=" + Guid.NewGuid().ToString("N");
            Func<bool> current = () => generation == Interlocked.Read(ref connectionGeneration)
                && connection == diagnosticConnectionId && IsConnected && !userCancelRequested
                && ReferenceEquals(gInfo?.CurrentService, this);
            Task.Run(async () =>
            {
                bool entered = false;
                try
                {
                    if (delayMs > 0) await Task.Delay(delayMs).ConfigureAwait(false);
                    if (!current() || !connectionSnapshotGate.TryEnter(generation,
                        Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000), reason == "core-dial-error")) return;
                    entered = true;
                    // No lifecycle lock: these dedicated, read-only RPC sockets
                    // must never delay Disconnect or the UI. Bound the whole read.
                    using (var budget = new CancellationTokenSource(2000))
                    {
                        Action<string, string> write = (stage, fields) =>
                        {
                            if (current()) ConnectionDiagnostics.Write(stage, context + " " + fields);
                        };
                        write("connection-state-begin", "readOnly=True healthProbes=False controlPort=" + CorePort
                            + " configuredMainMembers=" + (autoSelectorPlan?.MainMembers ?? 0)
                            + " configuredAiMembers=" + (autoSelectorPlan?.AiMembers ?? 0));
                        var client = new LibcoreServiceClient("127.0.0.1", CorePort, 750);
                        var clock = Stopwatch.StartNew();
                        try
                        {
                            if (!current()) return;
                            var pools = client.QueryAutoSelectors(750, budget.Token);
                            if (current()) ConnectionStateDiagnostic.Pools(pools, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), write);
                        }
                        catch (Exception ex)
                        {
                            // An older Core or unavailable API is not an empty
                            // unhealthy pool and must not affect the connection.
                            write("pool-state-unavailable", "rpcElapsedMs=" + clock.ElapsedMilliseconds
                                + " budgetCanceled=" + budget.IsCancellationRequested + " " + NetworkFailureDiagnostic.ExceptionFields(ex));
                        }
                        clock.Restart();
                        try
                        {
                            if (!current() || budget.IsCancellationRequested) return;
                            var flows = client.QueryConnections(750, budget.Token);
                            if (current()) ConnectionStateDiagnostic.Flows(flows, write);
                        }
                        catch (Exception ex) { write("flow-state-unavailable", "rpcElapsedMs=" + clock.ElapsedMilliseconds
                            + " budgetCanceled=" + budget.IsCancellationRequested + " " + NetworkFailureDiagnostic.ExceptionFields(ex)); }
                        if (current()) ConnectionDiagnostics.RequestSnapshot();
                        write("connection-state-end", "readOnly=True");
                    }
                }
                catch { /* Optional diagnostics must not affect the connection. */ }
                finally { if (entered) connectionSnapshotGate.Exit(); }
            });
        }
    }
}
