using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private sealed class RefreshTestServer : IServer
        {
            public int ID => 0;
            public string Country { get; set; } = "";
            public string Protocol { get; set; }
            public List<Url> urls { get; set; }
        }
        internal static ServerProbeMeasurement TestRefreshBatch(Url url, GlobalInfo info, string testUrl,
            CancellationToken cancellation)
        {
            var clock = Stopwatch.StartNew();
            int Remaining() => (int)Math.Max(0, UrlTestRetryPolicy.DisplayBudgetMs - clock.ElapsedMilliseconds);
            var runner = new TunnelPlusService(new RefreshTestServer { urls = new List<Url> { url } }, info)
                { Name = "server-refresh" };
            Process process = null;
            SniRuntime sni = null;
            int corePort = 0, socksPort = 0;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (Xray.ConfigGenerator.UrlTestRefusalReason(url.url) != null) return ServerProbeMeasurement.Failed;
                try
                {
                    corePort = FreePortManager.Dequeue();
                    if (Xray.ConfigGenerator.LinkNeedsXrayForUrlTest(url.url)) socksPort = FreePortManager.Dequeue();
                }
                catch (TimeoutException) { return default; } // No local port, no measurement.

                string sniLink = runner.GetSniLink(url.url);
                if (!string.IsNullOrEmpty(sniLink))
                    sni = runner.StartSniRuntime(runner.ParseSniLink(sniLink), false, Remaining, cancellation);
                var links = new Dictionary<string, string[]>
                {
                    [url.url] = runner.BuildChainLinks(runner.GetChainLink(url.url), new[] { runner.GetDefaultChainLink(), selectedChain })
                };
                var overrides = sni == null ? null : new Dictionary<string, SingBox.ConfigGenerator.EndpointOverride>
                {
                    [url.url] = new SingBox.ConfigGenerator.EndpointOverride { Server = sni.ListenHost, ServerPort = sni.ListenPort }
                };
                var bridge = socksPort == 0 ? null : new Xray.ConfigGenerator.XraySocksInfo
                {
                    Link = url.url, Tag = "xray-0", Port = socksPort,
                    User = Guid.NewGuid().ToString("N"), Pass = Guid.NewGuid().ToString("N")
                };
                var socks = bridge == null ? null : new Dictionary<string, Tuple<int, string, string>>
                {
                    [url.url] = Tuple.Create(bridge.Port, bridge.User, bridge.Pass)
                };
                string config = SingBox.ConfigGenerator.GetUrlTestConfig(links, corePort, out var tags, overrides, socks);
                if (tags.Count != 1) return ServerProbeMeasurement.Failed;
                string tag = tags.Keys.Single();
                string xrayConfig = bridge == null ? "" :
                    Xray.ConfigGenerator.GetUrlTestXrayConfig(new List<Xray.ConfigGenerator.XraySocksInfo> { bridge }, useCoreDns: true);

                // StopTest is process-wide. A private worker lets one deadline
                // end its Test RPC without touching other probes or the live VPN.
                process = runner.StartDisplayCore(corePort);
                WaitForDisplayListener(process, corePort, Remaining, cancellation);
                var client = new LibcoreServiceClient("127.0.0.1", corePort);
                return UrlTestRetryPolicy.RunDisplay(testUrl, (target, timeout) =>
                {
                    long deadline = clock.ElapsedMilliseconds + timeout;
                    int AttemptRemaining() => (int)Math.Max(0, deadline - clock.ElapsedMilliseconds);
                    try
                    {
                        if (process == null)
                        {
                            process = runner.StartDisplayCore(corePort);
                            WaitForDisplayListener(process, corePort, AttemptRemaining, cancellation);
                        }
                        int remaining = AttemptRemaining();
                        if (remaining <= 0) return ServerProbeMeasurement.Failed;
                        var response = client.TestWithDeadline(new TestReq
                        {
                            Config = config,
                            OutboundTags = new List<string> { tag },
                            Url = target,
                            MaxConcurrency = 1,
                            NeedXray = bridge != null,
                            XrayConfig = xrayConfig,
                            XrayOutboundDnsStrategy = bridge != null ? Xray.ConfigGenerator.UrlTestOutboundDnsStrategy : ""
                        }, remaining, cancellation);
                        var result = response?.Results?.FirstOrDefault(item => item.OutboundTag == tag);
                        return UrlTestRetryPolicy.IsSuccess(result)
                            ? new ServerProbeMeasurement(result.LatencyMs) : ServerProbeMeasurement.Failed;
                    }
                    catch (TimeoutException)
                    {
                        runner.StopDisplayProcess(process);
                        process = null;
                        return ServerProbeMeasurement.Failed;
                    }
                    catch (Exception ex) when (ex is SocketException || ex is IOException)
                    {
                        return default; // The local worker stopped before reporting a measurement.
                    }
                    catch (InvalidOperationException)
                    {
                        return ServerProbeMeasurement.Failed; // Core rejected this server's config.
                    }
                }, () => clock.ElapsedMilliseconds, cancellation);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return default; }
            catch (Win32Exception) { return default; } // Local process admission failed.
            catch (TimeoutException) { return ServerProbeMeasurement.Failed; }
            catch (FormatException) { return ServerProbeMeasurement.Failed; }
            catch (InvalidOperationException) { return ServerProbeMeasurement.Failed; }
            finally
            {
                runner.StopDisplayProcess(process);
                if (sni != null)
                {
                    runner.StopDisplayProcess(sni.Process);
                    runner.ReleaseSniListenPort(sni.ListenPort);
                    DeleteDisplaySniConfig(sni.ConfigPath);
                }
                if (corePort > 0) FreePortManager.Enqueue(corePort);
                if (socksPort > 0) FreePortManager.Enqueue(socksPort);
            }
        }

        private Process StartDisplayCore(int port)
        {
            string path = ResolveCorePath();
            if (path == null) throw new FileNotFoundException("Display probe core is unavailable.");
            return StartDisplayProcess(path, "-port " + port);
        }

        private static Process StartDisplayProcess(string path, string arguments)
        {
            lock (ownedExitGate)
            {
                if (applicationExiting) throw new OperationCanceledException();
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (process == null) throw new Win32Exception("Display probe core could not be started.");
                ownedExitProcesses.Add(process);
                // Drain worker output without copying configs or endpoint data into app logs.
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
        }

        private static void WaitForDisplayListener(Process process, int port, Func<int> remaining, CancellationToken cancellation)
        {
            while (remaining() > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (process.HasExited) throw new Win32Exception("Display probe worker exited before listening.");
                // Never send a subscription config to an unrelated listener.
                if (CoreDiagnosticMetadata.ListenerPid(port) == process.Id) return;
                cancellation.WaitHandle.WaitOne(Math.Min(25, remaining()));
            }
            throw new TimeoutException("Display probe worker startup deadline exceeded.");
        }

        private void StopDisplayProcess(Process process)
        {
            if (process == null) return;
            if (!TryKillProcess(process)) process.WaitForExit();
            lock (ownedExitGate) ownedExitProcesses.Remove(process);
            process.Dispose();
        }

        private static void DeleteDisplaySniConfig(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal void UpdateListTestResult()
        {
            var best = server.urls.Where(u => u != null && u.latency > 0).OrderBy(u => u.latency).FirstOrDefault();
            urlTestSpeed = best?.latency ?? -1; selectedUrl = best?.url; lastUrlTest = DateTime.Now;
        }
    }
}
