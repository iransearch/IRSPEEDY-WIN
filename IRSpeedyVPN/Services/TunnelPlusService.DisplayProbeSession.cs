using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        // One disposable Core process per display round, shared by up to eight
        // independent probe environments. No live Core, grpcLock, or StopTest.
        internal sealed class DisplayProbeSession : IDisposable
        {
            private readonly object gate = new object();
            private readonly GlobalInfo info;
            private readonly string chain;
            private Process process;
            private int port = -1;

            internal DisplayProbeSession(GlobalInfo info)
            {
                this.info = info;
                chain = selectedChain;
            }

            internal DisplayProbeResult Measure(IVPNService service, Url member, string target,
                CancellationToken cancellation, Action<long> measured)
            {
                var watch = Stopwatch.StartNew();
                if (service is CiscoService)
                {
                    var result = ProbeCiscoEndpoint(member.url, cancellation);
                    if (result.Success) measured?.Invoke(result.Latency);
                    return result;
                }
                member.url = UrlTestRetryPolicy.NormalizeDisplayTransport(member.url);
                var runner = new TunnelPlusService(new RefreshTestServer { urls = new List<Url> { member } }, info);
                return UrlTestRetryPolicy.RunDisplay(target, (url, budget) =>
                    runner.RunDisplayAttempt(this, member, url, budget, chain, cancellation),
                    () => watch.ElapsedMilliseconds, cancellation, measured);
            }

            internal int GetPort(string executable, CancellationToken cancellation)
            {
                lock (gate)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (process != null)
                    {
                        if (process.HasExited) throw new InvalidOperationException("Display probe Core exited.");
                        return port;
                    }
                    if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                        throw new InvalidOperationException("Display probe Core executable is unavailable.");
                    int assigned = FreePortManager.Dequeue();
                    try
                    {
                        process = Process.Start(new ProcessStartInfo
                        {
                            FileName = executable, Arguments = "-port " + assigned,
                            WorkingDirectory = Path.GetDirectoryName(executable),
                            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                        });
                        if (process == null) throw new InvalidOperationException("Display probe Core did not start.");
                        port = assigned;
                    }
                    catch
                    {
                        FreePortManager.Enqueue(assigned);
                        throw;
                    }
                    return port;
                }
            }

            public void Dispose()
            {
                lock (gate)
                {
                    if (process != null)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                if (!process.WaitForExit(1000)) port = -1;
                            }
                        }
                        catch (InvalidOperationException) { port = -1; }
                        catch (System.ComponentModel.Win32Exception) { port = -1; }
                        finally { process.Dispose(); }
                    }
                    if (port > 0) FreePortManager.Enqueue(port);
                }
            }
        }

        internal static DisplayProbeResult TestRefreshBatch(DisplayProbeSession session, IVPNService service,
            Url member, string target, CancellationToken cancellation, Action<long> measured)
            => session.Measure(service, member, target, cancellation, measured);

        private static readonly object displaySniStartup = new object();

        private DisplayProbeResult RunDisplayAttempt(DisplayProbeSession session, Url member, string target,
            int budget, string chain, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            int Remaining() => Math.Max(0, budget - (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds));
            var runtimes = new Dictionary<string, SniRuntime>(StringComparer.OrdinalIgnoreCase);
            int configPort = -1, sidecarPort = -1;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (Xray.ConfigGenerator.UrlTestRefusalReason(member.url) != null) return DisplayProbeResult.Failed;
                int rpcPort;
                try
                {
                    rpcPort = session.GetPort(corePath, cancellation);
                    configPort = FreePortManager.Dequeue();
                    if (Xray.ConfigGenerator.LinkNeedsXrayForUrlTest(member.url))
                        sidecarPort = FreePortManager.Dequeue();
                }
                catch (TimeoutException) { return DisplayProbeResult.Deferred; }

                var overrides = new Dictionary<string, SingBox.ConfigGenerator.EndpointOverride>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(GetSniLink(member.url)))
                {
                    // The legacy SNI helper allocates a local port per runner.
                    // Serialize only its listener startup to avoid duplicate binds.
                    SniRuntime runtime;
                    if (!Monitor.TryEnter(displaySniStartup, Remaining())) return DisplayProbeResult.Deferred;
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        runtime = GetSniRuntime(member.url, runtimes, false);
                    }
                    finally { Monitor.Exit(displaySniStartup); }
                    if (runtime != null)
                        overrides[member.url] = new SingBox.ConfigGenerator.EndpointOverride
                        { Server = runtime.ListenHost, ServerPort = runtime.ListenPort };
                }
                var links = new Dictionary<string, string[]> { [member.url] =
                    BuildChainLinks(GetChainLink(member.url), new[] { GetDefaultChainLink(), chain }) };
                var socks = new Dictionary<string, Tuple<int, string, string>>();
                string xrayConfig = "";
                if (sidecarPort > 0)
                {
                    var sidecar = new Xray.ConfigGenerator.XraySocksInfo
                    {
                        Link = member.url, Port = sidecarPort, Tag = "xray-0",
                        User = Guid.NewGuid().ToString("N"), Pass = Guid.NewGuid().ToString("N")
                    };
                    socks.Add(member.url, Tuple.Create(sidecar.Port, sidecar.User, sidecar.Pass));
                    xrayConfig = Xray.ConfigGenerator.GetUrlTestXrayConfig(
                        new List<Xray.ConfigGenerator.XraySocksInfo> { sidecar }, useCoreDns: true);
                }
                var config = SingBox.ConfigGenerator.GetUrlTestConfig(links, configPort, out var tags,
                    overrides, socks, "display-" + Guid.NewGuid().ToString("N") + "-", new Dictionary<string, string>());
                if (tags.Count != 1) return DisplayProbeResult.Failed;

                // Waiting for the probe Core to listen, local config preparation,
                // RPC startup and HTTP requests all debit this attempt's budget.
                while (Remaining() > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (ProtorpcClient.CanConnect("127.0.0.1", rpcPort, Math.Min(100, Remaining()))) break;
                    session.GetPort(corePath, cancellation); // Report an exited process instead of recording server failure.
                    cancellation.WaitHandle.WaitOne(Math.Min(20, Remaining()));
                }
                cancellation.ThrowIfCancellationRequested();
                if (Remaining() == 0) return DisplayProbeResult.Failed;

                var request = new TestReq
                {
                    Config = config, Url = target, OutboundTags = tags.Keys.ToList(), MaxConcurrency = 1,
                    NeedXray = sidecarPort > 0, XrayConfig = xrayConfig,
                    XrayOutboundDnsStrategy = sidecarPort > 0 ? Xray.ConfigGenerator.UrlTestOutboundDnsStrategy : ""
                };
                try
                {
                    var result = new LibcoreServiceClient("127.0.0.1", rpcPort)
                        .DisplayTest(request, Remaining(), cancellation)?.Results?.SingleOrDefault();
                    if (result == null || result.Error == "display-probe-deferred") return DisplayProbeResult.Deferred;
                    return UrlTestRetryPolicy.IsSuccess(result) ? new DisplayProbeResult(result.LatencyMs) : DisplayProbeResult.Failed;
                }
                catch (InvalidOperationException ex) when ((ex.Message ?? "").IndexOf("can't find method", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new NotSupportedException("Display probes require the matching Throne Core patch.");
                }
                catch (SocketException) { return DisplayProbeResult.Deferred; }
                catch (IOException) { return DisplayProbeResult.Deferred; }
            }
            finally
            {
                StopSniServers(runtimes);
                if (configPort >= 0) FreePortManager.Enqueue(configPort);
                if (sidecarPort >= 0) FreePortManager.Enqueue(sidecarPort);
            }
        }

        private static DisplayProbeResult ProbeCiscoEndpoint(string link, CancellationToken cancellation)
        {
            // Cisco was always an HTTPS reachability check, not a tunnel to
            // the API test URL. Keep that endpoint and isolate its HTTP state.
            string url = link.Replace("cisco://", "https://");
            var group = "display-" + Guid.NewGuid().ToString("N");
            ServicePoint servicePoint = null;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(UrlTestRetryPolicy.DisplayBudgetMs);
                try
                {
                    long latency = -1;
                    for (int i = 0; i < 2; i++)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        var request = (HttpWebRequest)WebRequest.Create(url);
                        request.ConnectionGroupName = group;
                        request.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
                        servicePoint = request.ServicePoint;
                        using (deadline.Token.Register(request.Abort))
                        {
                            var watch = Stopwatch.StartNew();
                            using (var response = request.GetResponse())
                            {
                                latency = Math.Max(1, watch.ElapsedMilliseconds);
                                if (i == 0) using (var body = response.GetResponseStream()) body?.CopyTo(Stream.Null);
                            }
                        }
                    }
                    return new DisplayProbeResult(latency);
                }
                catch (WebException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    return DisplayProbeResult.Failed;
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                { return DisplayProbeResult.Failed; }
                finally { servicePoint?.CloseConnectionGroup(group); }
            }
        }
    }
}
