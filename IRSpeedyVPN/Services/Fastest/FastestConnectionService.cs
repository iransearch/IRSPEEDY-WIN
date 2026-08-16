using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services.Libcore;
using IRSpeedyVPN.WebServices;
using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Fastest
{
    internal sealed class FastestConnectionService : IVPNService
    {
        private readonly GlobalInfo gInfo;
        private readonly List<IVPNService> sourceServices;
        private readonly NewServiceController serviceController;
        private readonly object lifecycleLock = new object();

        private CancellationTokenSource activeCancellation;
        private RuntimeContext activeRuntime;
        private int generation;
        private bool isConnected;
        private bool isUsingProxifire;
        private int listenPort;
        private long lastLatency = -1;
        private string selectedProtocol;

        public FastestConnectionService(IEnumerable<IVPNService> services, GlobalInfo globalInfo, string protocol)
        {
            sourceServices = (services ?? Enumerable.Empty<IVPNService>())
                .Where(x => x != null)
                .Distinct()
                .ToList();
            if (sourceServices.Count == 0)
                throw new ArgumentException("At least one source service is required.", nameof(services));

            gInfo = globalInfo ?? throw new ArgumentNullException(nameof(globalInfo));
            selectedProtocol = protocol;
            Name = sourceServices[0].Name;
            serviceController = AppServices.NewServiceController;
        }

        public event OnConnectDisconnect onConnectDisconnect;
        public byte Order => 0;
        public int ID => 0;
        public string Name { get; set; }
        public string Country => "FASTEST CONNECTION";
        public string CountryCode => "*";
        public byte CountryIndex { get; set; }
        public string[] Protocols => string.IsNullOrWhiteSpace(selectedProtocol) ? new string[0] : new[] { selectedProtocol };
        public string SelectedProtocol => selectedProtocol;
        public bool IsUsingProxifire => isUsingProxifire;
        public bool ProxifierWithPassword => false;
        public ProxyType ProxyType => ProxyType.SOCKS;
        public Type SettingType => sourceServices.FirstOrDefault()?.SettingType;
        public bool ShowSpeedyShieldSetting => true;
        public bool IsShareActive { get; set; }
        public int? HttpPort => listenPort > 0 ? (int?)listenPort : null;
        public int? SocksPort => listenPort > 0 ? (int?)listenPort : null;
        public bool IsUrlTestSupported => false;
        public long UrlTestSpeed => lastLatency;
        public Url SelectedServerUrl { get; set; }

        public ProxifierType ProxifierRuleType
        {
            get
            {
                if (RegHelper.GetSettingValue("VGAURDGlobalProxy") != "0") return ProxifierType.Global;
                if (RegHelper.GetSettingValue("VGAURDSystemProxy") == "1") return ProxifierType.None;
                if (RegHelper.GetSettingValue("ProxifierSmartRoute") == "2") return ProxifierType.Telegram;
                return ProxifierType.None;
            }
        }

        public void Connect(string protocol)
        {
            if (!string.IsNullOrWhiteSpace(protocol))
                selectedProtocol = protocol;

            RuntimeContext oldRuntime;
            CancellationTokenSource cancellation;
            int currentGeneration;
            lock (lifecycleLock)
            {
                generation++;
                currentGeneration = generation;
                activeCancellation?.Cancel();
                activeCancellation = new CancellationTokenSource();
                cancellation = activeCancellation;
                oldRuntime = activeRuntime;
                activeRuntime = null;
                isConnected = false;
                listenPort = 0;
                isUsingProxifire = false;
            }

            CleanupRuntime(oldRuntime, true);
            Task.Run(() => RunConnection(currentGeneration, cancellation.Token));
        }

        public void Disconnect()
        {
            StopConnection(false);
        }

        public void DisconnectAll()
        {
            StopConnection(false);
        }

        public void ApplyShareSetting()
        {
            bool shouldRestart;
            lock (lifecycleLock)
                shouldRestart = isConnected;
            if (!shouldRestart)
                return;

            StopConnection(true);
            Connect(selectedProtocol);
        }

        public bool IsRequirementAvailable()
        {
            if (!File.Exists(ResolveCorePath()) || !File.Exists(ResolveThronePath()))
                return false;

            var urls = GetServerUrls().Where(x => x != null && !string.IsNullOrWhiteSpace(x.url)).ToList();
            var hasNonHysteria = urls.Any(x => !IsHysteria2Link(x.url));
            var hasHysteria = urls.Any(x => IsHysteria2Link(x.url));
            return hasNonHysteria || (hasHysteria && File.Exists(ResolveHysteriaCorePath()));
        }

        public long UrlTest() => lastLatency;

        public List<Url> GetServerUrls()
        {
            return sourceServices
                .SelectMany(x => x.GetServerUrls() ?? new List<Url>())
                .Where(x => x != null)
                .ToList();
        }

        private void RunConnection(int currentGeneration, CancellationToken token)
        {
            RuntimeContext runtime = null;
            var published = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
                    throw new InvalidOperationException("تعداد اتصالات بیش از حد مجاز است");

                runtime = new RuntimeContext(currentGeneration);
                var routes = CollectRoutes();
                if (routes.Count == 0)
                    throw new InvalidOperationException("سرور سازگار یافت نشد");

                PrepareRoutes(runtime, routes, token);
                if (runtime.Routes.Count == 0)
                    throw new InvalidOperationException("سرور سازگار یافت نشد");

                runtime.ListenPort = AllocatePort(runtime);
                runtime.VpnMode = RegHelper.GetSettingValue("VGAURDVPNMode") != "0";
                runtime.UseSystemProxy = !runtime.VpnMode && ProxifierRuleType == ProxifierType.None;
                runtime.UseProxifier = !runtime.VpnMode && ProxifierRuleType != ProxifierType.None;

                runtime.BuildResult = FastestConnectionConfigBuilder.Build(
                    runtime.Routes,
                    gInfo.Username,
                    runtime.ListenPort,
                    runtime.VpnMode,
                    IsShareActive,
                    GetShieldFiles(),
                    GetExcludedProcessPaths());
                SelectedServerUrl = runtime.BuildResult.FallbackRoute?.Url;

                token.ThrowIfCancellationRequested();
                StartCore(runtime, token);
                token.ThrowIfCancellationRequested();

                runtime.ReadinessLatency = ProbeThroughSocks(runtime.ListenPort, token);
                if (runtime.ReadinessLatency <= 0)
                    throw new InvalidOperationException("FASTEST CONNECTION readiness check failed.");

                var observed = ObserveWinner(runtime, token);
                if (observed.Route != null)
                {
                    SelectedServerUrl = observed.Route.Url;
                    lastLatency = observed.Latency;
                    FastestConnectionCache.RememberObserved(observed.Route, observed.Latency, gInfo.Username);
                }
                else
                {
                    lastLatency = runtime.ReadinessLatency;
                }

                if (runtime.UseSystemProxy)
                    WinINet.SetIEProxy(true, true, $"http://127.0.0.1:{runtime.ListenPort}", null);

                lock (lifecycleLock)
                {
                    token.ThrowIfCancellationRequested();
                    if (generation != currentGeneration)
                        throw new OperationCanceledException();

                    activeRuntime = runtime;
                    isConnected = true;
                    isUsingProxifire = runtime.UseProxifier;
                    listenPort = runtime.ListenPort;
                    published = true;
                }

                onConnectDisconnect?.Invoke(this, true, runtime.ListenPort, string.Empty);
            }
            catch (Exception ex)
            {
                var canceled = token.IsCancellationRequested || ex is OperationCanceledException;
                if (!published)
                    CleanupRuntime(runtime, true);

                bool notify;
                lock (lifecycleLock)
                    notify = generation == currentGeneration && !canceled;

                if (!canceled)
                    LogHelper.WriteLog(ex);
                if (notify)
                    onConnectDisconnect?.Invoke(this, false, 0, ex.Message);
            }
        }

        private List<FastestRoute> CollectRoutes()
        {
            var routes = new List<FastestRoute>();
            var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sequence = 0;

            foreach (var service in sourceServices)
            {
                if (!string.IsNullOrWhiteSpace(selectedProtocol)
                    && (service.Protocols == null || !service.Protocols.Contains(selectedProtocol)))
                    continue;

                foreach (var url in service.GetServerUrls() ?? new List<Url>())
                {
                    if (url == null || string.IsNullOrWhiteSpace(url.url) || url.chainproxy == 1 || !links.Add(url.url))
                        continue;

                    sequence++;
                    var suffix = url.id > 0
                        ? service.ID + "-" + url.id
                        : service.ID + "-x" + sequence;
                    var tag = FastestConnectionConfigBuilder.ProxyPrefix + suffix;
                    while (!tags.Add(tag))
                    {
                        sequence++;
                        tag = FastestConnectionConfigBuilder.ProxyPrefix + service.ID + "-x" + sequence;
                    }

                    routes.Add(new FastestRoute
                    {
                        Service = service,
                        Url = url,
                        Tag = tag,
                        RouteKey = FastestConnectionCache.BuildRouteKey(service.ID, url.id, url.url)
                    });
                }
            }

            return routes;
        }

        private void PrepareRoutes(RuntimeContext runtime, IEnumerable<FastestRoute> routes, CancellationToken token)
        {
            foreach (var route in routes)
            {
                token.ThrowIfCancellationRequested();
                if (IsHysteria2Link(route.Link))
                {
                    var hysteria = StartHysteria(runtime, route, token);
                    if (hysteria == null)
                        continue;
                    route.HysteriaSocksPort = hysteria.Port;
                }
                else if (IRSpeedyVPN.Services.Xray.ConfigGenerator.LinkNeedsXray(route.Link))
                {
                    route.XraySocksPort = AllocatePort(runtime);
                    route.XrayUser = Guid.NewGuid().ToString("N");
                    route.XrayPassword = Guid.NewGuid().ToString("N");
                }

                runtime.Routes.Add(route);
            }
        }

        private HysteriaRuntime StartHysteria(RuntimeContext runtime, FastestRoute route, CancellationToken token)
        {
            var path = ResolveHysteriaCorePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            var port = AllocatePort(runtime);
            string configPath = null;
            Process process = null;
            try
            {
                string message;
                string address;
                configPath = IRSpeedyVPN.Services.Hysteria.ConfigGenerator.WriteConfigFileFromLink(
                    route.Link,
                    port,
                    out message,
                    out address);
                if (string.IsNullOrWhiteSpace(configPath))
                    throw new InvalidOperationException(message ?? "Unable to create Hysteria config.");

                process = ShellExecute.ShellexecAndReturnProcess(path, $"client -c \"{configPath}\"");
                if (process == null || !WaitForPort("127.0.0.1", port, TimeSpan.FromSeconds(10), token))
                    throw new InvalidOperationException("Hysteria SOCKS endpoint did not become ready.");

                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupConfigFile(configPath);
                var result = new HysteriaRuntime { Route = route, Port = port, Process = process };
                runtime.HysteriaRuntimes.Add(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                ShellExecute.KillProcessTree(process);
                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupConfigFile(configPath);
                throw;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                ShellExecute.KillProcessTree(process);
                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupConfigFile(configPath);
                ReleasePort(runtime, port);
                return null;
            }
        }

        private void StartCore(RuntimeContext runtime, CancellationToken token)
        {
            EnsureCoreRunning(runtime, token);
            var request = new LoadConfigReq
            {
                CoreConfig = runtime.BuildResult.CoreConfig ?? string.Empty,
                DisableStats = false,
                NeedExtraProcess = false,
                ExtraProcessPath = string.Empty,
                ExtraProcessArgs = string.Empty,
                ExtraProcessConf = string.Empty,
                ExtraProcessConfDir = string.Empty,
                ExtraNoOut = false,
                NeedXray = runtime.BuildResult.NeedXray,
                XrayConfig = runtime.BuildResult.XrayConfig ?? string.Empty
            };

            var validation = ExecuteCoreCall(runtime, client => client.CheckConfig(request), true, token);
            if (!string.IsNullOrWhiteSpace(validation?.Error))
                throw new InvalidOperationException(validation.Error);

            var response = ExecuteCoreCall(runtime, client =>
            {
                try { client.Stop(); } catch { }
                return client.Start(request);
            }, true, token);
            if (!string.IsNullOrWhiteSpace(response?.Error))
                throw new InvalidOperationException(response.Error);
        }

        private void EnsureCoreRunning(RuntimeContext runtime, CancellationToken token)
        {
            if (runtime.ThroneClient != null && runtime.ThroneClient.IsConnected)
                return;

            ResetCore(runtime);
            token.ThrowIfCancellationRequested();

            var corePath = ResolveCorePath();
            var thronePath = ResolveThronePath();
            if (string.IsNullOrWhiteSpace(corePath) || !File.Exists(corePath))
                throw new FileNotFoundException("Core executable was not found.", corePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(thronePath) || !File.Exists(thronePath))
                throw new FileNotFoundException("Throne executable was not found.", thronePath ?? string.Empty);

            runtime.PipeName = "Throne_relay_" + Guid.NewGuid().ToString("N");
            runtime.CoreProcess = ShellExecute.ShellexecAndReturnProcess(
                thronePath,
                $"\"{corePath}\" {runtime.PipeName}",
                Path.GetDirectoryName(corePath));
            if (runtime.CoreProcess == null)
                throw new InvalidOperationException("Unable to start Throne.");

            runtime.CoreProcess.EnableRaisingEvents = true;
            runtime.CoreProcess.Exited += (sender, args) => CoreProcessExited(runtime);

            var stopwatch = Stopwatch.StartNew();
            Exception lastError = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(15))
            {
                token.ThrowIfCancellationRequested();
                if (runtime.CoreProcess != null && runtime.CoreProcess.HasExited)
                    throw new InvalidOperationException(
                        "Throne relay exited during startup with code " + runtime.CoreProcess.ExitCode + ".");
                try
                {
                    runtime.ThroneClient = new ThronePipeClient(runtime.PipeName);
                    runtime.ThroneClient.Connect(1000);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { runtime.ThroneClient?.Dispose(); } catch { }
                    runtime.ThroneClient = null;
                    if (token.WaitHandle.WaitOne(100))
                        token.ThrowIfCancellationRequested();
                }
            }

            throw new TimeoutException("Throne relay did not start in time."
                + (lastError != null ? " Last error: " + lastError.Message : string.Empty));
        }

        private T ExecuteCoreCall<T>(
            RuntimeContext runtime,
            Func<ThronePipeClient, T> call,
            bool reconnect,
            CancellationToken token)
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (runtime.ThroneClient == null || !runtime.ThroneClient.IsConnected)
                        EnsureCoreRunning(runtime, token);
                    return call(runtime.ThroneClient);
                }
                catch (Exception ex) when (
                    ex is IOException ||
                    ex is EndOfStreamException ||
                    ex is ObjectDisposedException ||
                    ex is TimeoutException)
                {
                    if (attempt >= (reconnect ? 1 : 0))
                        throw;
                    ResetCore(runtime);
                    if (token.WaitHandle.WaitOne(200))
                        token.ThrowIfCancellationRequested();
                }
            }
        }

        private ObservedRoute ObserveWinner(RuntimeContext runtime, CancellationToken token)
        {
            if (token.WaitHandle.WaitOne(1000))
                token.ThrowIfCancellationRequested();

            try
            {
                var response = ExecuteCoreCall(runtime, client => client.QueryURLTest(), false, token);
                if (response?.Results == null)
                    return default(ObservedRoute);

                FastestRoute bestRoute = null;
                long bestLatency = long.MaxValue;
                foreach (var result in response.Results)
                {
                    if (result == null || result.LatencyMs <= 0 || !string.IsNullOrWhiteSpace(result.Error))
                        continue;
                    if (!runtime.BuildResult.TagToRoute.TryGetValue(result.OutboundTag, out var route))
                        continue;

                    route.Url.latency = result.LatencyMs;
                    route.Url.latencychkTime = DateTime.Now;
                    if (result.LatencyMs < bestLatency)
                    {
                        bestLatency = result.LatencyMs;
                        bestRoute = route;
                    }
                }

                return bestRoute == null
                    ? default(ObservedRoute)
                    : new ObservedRoute(bestRoute, bestLatency);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is EndOfStreamException ||
                ex is ObjectDisposedException ||
                ex is TimeoutException ||
                ex is InvalidOperationException)
            {
                LogHelper.WriteLog(ex);
                return default(ObservedRoute);
            }
        }

        private long ProbeThroughSocks(int port, CancellationToken token)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                if (TrySocksHeadRequest("127.0.0.1", port, "connectivitycheck.gstatic.com", 443, "/generate_204", 7000))
                    return Math.Max(1L, stopwatch.ElapsedMilliseconds);
                if (token.WaitHandle.WaitOne(500))
                    token.ThrowIfCancellationRequested();
            }
            return -1;
        }

        private static bool TrySocksHeadRequest(
            string proxyHost,
            int proxyPort,
            string destinationHost,
            int destinationPort,
            string path,
            int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(proxyHost, proxyPort);
                    if (!connectTask.Wait(timeoutMs) || !client.Connected)
                        return false;
                    client.ReceiveTimeout = timeoutMs;
                    client.SendTimeout = timeoutMs;

                    using (var stream = client.GetStream())
                    {
                        stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
                        var greeting = ReadExact(stream, 2);
                        if (greeting[0] != 5 || greeting[1] != 0)
                            return false;

                        var hostBytes = Encoding.ASCII.GetBytes(destinationHost);
                        var request = new byte[7 + hostBytes.Length];
                        request[0] = 5;
                        request[1] = 1;
                        request[2] = 0;
                        request[3] = 3;
                        request[4] = (byte)hostBytes.Length;
                        Buffer.BlockCopy(hostBytes, 0, request, 5, hostBytes.Length);
                        request[5 + hostBytes.Length] = (byte)(destinationPort >> 8);
                        request[6 + hostBytes.Length] = (byte)destinationPort;
                        stream.Write(request, 0, request.Length);

                        var reply = ReadExact(stream, 4);
                        if (reply[0] != 5 || reply[1] != 0)
                            return false;
                        ConsumeSocksAddress(stream, reply[3]);

                        using (var ssl = new SslStream(stream, false))
                        {
                            ssl.ReadTimeout = timeoutMs;
                            ssl.WriteTimeout = timeoutMs;
                            ssl.AuthenticateAsClient(destinationHost, null, SslProtocols.Tls12, true);
                            var http = Encoding.ASCII.GetBytes(
                                $"HEAD {path} HTTP/1.1\r\nHost: {destinationHost}\r\nConnection: close\r\n\r\n");
                            ssl.Write(http, 0, http.Length);
                            ssl.Flush();
                            var statusLine = ReadAsciiLine(ssl, 256);
                            return statusLine.StartsWith("HTTP/1.1 2", StringComparison.Ordinal)
                                || statusLine.StartsWith("HTTP/2 2", StringComparison.Ordinal)
                                || statusLine.StartsWith("HTTP/1.1 3", StringComparison.Ordinal);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ReadAsciiLine(Stream stream, int maxLength)
        {
            var bytes = new List<byte>();
            while (bytes.Count < maxLength)
            {
                var value = stream.ReadByte();
                if (value < 0 || value == '\n')
                    break;
                if (value != '\r')
                    bytes.Add((byte)value);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }

        private static void ConsumeSocksAddress(Stream stream, byte addressType)
        {
            if (addressType == 1)
                ReadExact(stream, 4);
            else if (addressType == 3)
            {
                var length = stream.ReadByte();
                if (length < 0)
                    throw new EndOfStreamException();
                ReadExact(stream, length);
            }
            else if (addressType == 4)
                ReadExact(stream, 16);
            else
                throw new InvalidDataException("Invalid SOCKS address type.");
            ReadExact(stream, 2);
        }

        private void CoreProcessExited(RuntimeContext runtime)
        {
            bool notify = false;
            lock (lifecycleLock)
            {
                if (activeRuntime == runtime && isConnected && generation == runtime.Generation)
                {
                    activeRuntime = null;
                    isConnected = false;
                    listenPort = 0;
                    isUsingProxifire = false;
                    generation++;
                    activeCancellation?.Cancel();
                    notify = true;
                }
            }

            if (!notify)
                return;

            Task.Run(() =>
            {
                CleanupRuntime(runtime, true);
                onConnectDisconnect?.Invoke(this, false, 0, "FASTEST CONNECTION core stopped.");
            });
        }

        private void StopConnection(bool silent)
        {
            RuntimeContext runtime;
            bool wasActive;
            lock (lifecycleLock)
            {
                generation++;
                activeCancellation?.Cancel();
                activeCancellation = null;
                runtime = activeRuntime;
                activeRuntime = null;
                wasActive = isConnected || runtime != null;
                isConnected = false;
                isUsingProxifire = false;
                listenPort = 0;
            }

            CleanupRuntime(runtime, true);
            if (!silent && wasActive)
                onConnectDisconnect?.Invoke(this, false, 0, string.Empty);
        }

        private void CleanupRuntime(RuntimeContext runtime, bool disableProxy)
        {
            if (runtime == null || Interlocked.Exchange(ref runtime.CleanupStarted, 1) != 0)
                return;

            if (disableProxy && runtime.UseSystemProxy)
            {
                try { SystemProxy.Disable(); } catch { }
            }

            ResetCore(runtime);
            foreach (var hysteria in runtime.HysteriaRuntimes.ToList())
            {
                ShellExecute.KillProcessTree(hysteria.Process);
                ReleasePort(runtime, hysteria.Port);
            }
            runtime.HysteriaRuntimes.Clear();

            foreach (var port in runtime.OwnedPorts.ToList())
                ReleasePort(runtime, port);
            runtime.OwnedPorts.Clear();
        }

        private static void ResetCore(RuntimeContext runtime)
        {
            if (runtime == null)
                return;

            try { runtime.ThroneClient?.Stop(); } catch { }
            try { runtime.ThroneClient?.Dispose(); } catch { }
            runtime.ThroneClient = null;

            if (runtime.CoreProcess != null)
                ShellExecute.KillProcessTree(runtime.CoreProcess);
            runtime.CoreProcess = null;
        }

        private static int AllocatePort(RuntimeContext runtime)
        {
            var port = FreePortManager.Dequeue();
            if (port <= 0)
                throw new InvalidOperationException("Unable to allocate a local port.");
            runtime.OwnedPorts.Add(port);
            return port;
        }

        private static void ReleasePort(RuntimeContext runtime, int port)
        {
            if (runtime != null && port > 0 && runtime.OwnedPorts.Remove(port))
                FreePortManager.Enqueue(port);
        }

        private static bool WaitForPort(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var client = new TcpClient())
                    {
                        var task = client.ConnectAsync(host, port);
                        if (task.Wait(200) && client.Connected)
                            return true;
                    }
                }
                catch
                {
                }

                if (token.WaitHandle.WaitOne(100))
                    token.ThrowIfCancellationRequested();
            }
            return false;
        }

        private string[] GetShieldFiles()
        {
            try
            {
                return (RegHelper.GetSettingValue("ShieldFilterFiles") ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        private string[] GetExcludedProcessPaths()
        {
            return new[] { ResolveCorePath(), ResolveThronePath(), ResolveHysteriaCorePath() }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string ResolveCorePath()
        {
            var file = (Tools.IsWin7OrLower() ? "SGuard7" : "SGuard")
                + (Environment.Is64BitOperatingSystem ? "64.exe" : "32.exe");
            var path = Path.Combine(gInfo.TempPath, "V-Guard", file);
            return File.Exists(path) ? path : null;
        }

        private string ResolveThronePath()
        {
            var path = Path.Combine(gInfo.TempPath, "V-Guard", "Throne.exe");
            return File.Exists(path) ? path : null;
        }

        private string ResolveHysteriaCorePath()
        {
            var file = "hysteria-windows-"
                + (Environment.Is64BitOperatingSystem ? "amd64.exe" : "386.exe");
            var path = Path.Combine(gInfo.TempPath, "hysteria", file);
            return File.Exists(path) ? path : null;
        }

        private static bool IsHysteria2Link(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return false;
            return link.StartsWith(v2rayN.Global.Hysteria2ProtocolLite, StringComparison.OrdinalIgnoreCase)
                || link.StartsWith(v2rayN.Global.Hysteria2Protocol, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class RuntimeContext
        {
            public RuntimeContext(int generation)
            {
                Generation = generation;
            }

            public int Generation { get; }
            public List<FastestRoute> Routes { get; } = new List<FastestRoute>();
            public List<HysteriaRuntime> HysteriaRuntimes { get; } = new List<HysteriaRuntime>();
            public HashSet<int> OwnedPorts { get; } = new HashSet<int>();
            public ThronePipeClient ThroneClient { get; set; }
            public Process CoreProcess { get; set; }
            public string PipeName { get; set; }
            public FastestBuildResult BuildResult { get; set; }
            public int ListenPort { get; set; }
            public bool VpnMode { get; set; }
            public bool UseSystemProxy { get; set; }
            public bool UseProxifier { get; set; }
            public long ReadinessLatency { get; set; }
            public int CleanupStarted;
        }

        private sealed class HysteriaRuntime
        {
            public FastestRoute Route { get; set; }
            public int Port { get; set; }
            public Process Process { get; set; }
        }

        private struct ObservedRoute
        {
            public ObservedRoute(FastestRoute route, long latency)
            {
                Route = route;
                Latency = latency;
            }

            public FastestRoute Route { get; }
            public long Latency { get; }
        }
    }
}
