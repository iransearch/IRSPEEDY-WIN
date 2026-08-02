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
        private readonly object runtimeLock = new object();
        private readonly object coreLock = new object();
        private readonly List<HysteriaRuntime> hysteriaRuntimes = new List<HysteriaRuntime>();
        private readonly HashSet<int> ownedPorts = new HashSet<int>();

        private ThronePipeClient throneClient;
        private Process coreProcess;
        private bool coreOwned;
        private bool stopping;
        private bool userCancelRequested;
        private bool useSystemProxy;
        private bool isUsingProxifire;
        private bool isConnected;
        private int listenPort;
        private int xraySocksPort;
        private long lastLatency = -1;
        private string selectedProtocol;
        private FastestBuildResult buildResult;

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
            serviceController = (NewServiceController)Program.container.GetInstance(typeof(NewServiceController));
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
            if (!string.IsNullOrWhiteSpace(protocol)) selectedProtocol = protocol;
            userCancelRequested = false;
            Task.Run((Action)RunConnection);
        }

        public void Disconnect()
        {
            userCancelRequested = true;
            DisconnectInternal(false);
        }

        public void DisconnectAll()
        {
            userCancelRequested = true;
            DisconnectInternal(false);
        }

        public void ApplyShareSetting()
        {
            if (!isConnected) return;
            Task.Run(() =>
            {
                DisconnectInternal(true);
                if (!userCancelRequested) RunConnection();
            });
        }

        public bool IsRequirementAvailable()
        {
            if (!File.Exists(ResolveCorePath()) || !File.Exists(ResolveThronePath())) return false;
            if (GetServerUrls().Any(x => IsHysteria2Link(x?.url)) && !File.Exists(ResolveHysteriaCorePath())) return false;
            return true;
        }

        public long UrlTest() => lastLatency;

        public List<Url> GetServerUrls()
        {
            return sourceServices.SelectMany(x => x.GetServerUrls() ?? new List<Url>()).Where(x => x != null).ToList();
        }

        private void RunConnection()
        {
            try
            {
                stopping = false;
                ResetRuntime(true);
                ThrowIfCanceled();

                if (!serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
                    throw new InvalidOperationException("تعداد اتصالات بیش از حد مجاز است");

                var routes = CollectRoutes();
                if (routes.Count == 0) throw new InvalidOperationException("سرور سازگار یافت نشد");

                var usableRoutes = PrepareHysteriaRoutes(routes);
                if (usableRoutes.Count == 0) throw new InvalidOperationException("سرور سازگار یافت نشد");

                xraySocksPort = AllocatePort();
                listenPort = AllocatePort();
                buildResult = FastestConnectionConfigBuilder.Build(usableRoutes, "127.0.0.1", xraySocksPort, gInfo.Username);
                SelectedServerUrl = buildResult.FallbackRoute?.Url;

                var vpnMode = RegHelper.GetSettingValue("VGAURDVPNMode") != "0";
                useSystemProxy = ProxifierRuleType == ProxifierType.None;
                isUsingProxifire = !vpnMode && ProxifierRuleType != ProxifierType.None;

                var wrapperLink = $"socks://127.0.0.1:{xraySocksPort}?host=127.0.0.1";
                var wrapperConfig = IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetConfig(
                    wrapperLink,
                    listenPort,
                    vpnMode,
                    IsShareActive,
                    GetShieldFiles(),
                    null,
                    new string[0],
                    null,
                    false,
                    null,
                    null,
                    GetExcludedProcessPaths());

                if (string.IsNullOrWhiteSpace(wrapperConfig))
                    throw new InvalidOperationException("Unable to create FASTEST CONNECTION wrapper config.");

                ThrowIfCanceled();
                StartCore(wrapperConfig, buildResult.XrayConfig);
                ThrowIfCanceled();

                lastLatency = ProbeThroughSocks(listenPort);
                if (lastLatency <= 0) throw new InvalidOperationException("FASTEST CONNECTION readiness check failed.");

                FastestConnectionCache.Remember(buildResult.FallbackRoute, lastLatency, gInfo.Username);

                if (vpnMode) useSystemProxy = false;
                else if (useSystemProxy) WinINet.SetIEProxy(true, true, $"http://127.0.0.1:{listenPort}", null);

                isConnected = true;
                onConnectDisconnect?.Invoke(this, true, listenPort, string.Empty);
            }
            catch (Exception ex)
            {
                var canceled = userCancelRequested || ex is OperationCanceledException;
                if (!canceled) LogHelper.WriteLog(ex);
                ResetRuntime(true);
                if (!canceled) onConnectDisconnect?.Invoke(this, false, 0, ex.Message);
            }
        }

        private void ThrowIfCanceled()
        {
            if (userCancelRequested) throw new OperationCanceledException();
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
                    && (service.Protocols == null || !service.Protocols.Contains(selectedProtocol))) continue;

                foreach (var url in service.GetServerUrls() ?? new List<Url>())
                {
                    if (url == null || string.IsNullOrWhiteSpace(url.url) || url.chainproxy == 1 || !links.Add(url.url)) continue;
                    sequence++;
                    var suffix = url.id > 0 ? service.ID + "-" + url.id : service.ID + "-x" + sequence;
                    var tag = FastestConnectionConfigBuilder.ProxyPrefix + suffix;
                    while (!tags.Add(tag))
                    {
                        sequence++;
                        tag = FastestConnectionConfigBuilder.ProxyPrefix + service.ID + "-x" + sequence;
                    }
                    routes.Add(new FastestRoute { Service = service, Url = url, Tag = tag });
                }
            }
            return routes;
        }

        private List<FastestRoute> PrepareHysteriaRoutes(IEnumerable<FastestRoute> routes)
        {
            var usable = new List<FastestRoute>();
            foreach (var route in routes)
            {
                ThrowIfCanceled();
                if (!IsHysteria2Link(route.Link))
                {
                    usable.Add(route);
                    continue;
                }

                var runtime = StartHysteria(route);
                if (runtime == null) continue;
                route.HysteriaSocksPort = runtime.Port;
                hysteriaRuntimes.Add(runtime);
                usable.Add(route);
            }
            return usable;
        }

        private HysteriaRuntime StartHysteria(FastestRoute route)
        {
            var path = ResolveHysteriaCorePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            var port = AllocatePort();
            string configPath = null;
            Process process = null;
            try
            {
                string message;
                string address;
                configPath = IRSpeedyVPN.Services.Hysteria.ConfigGenerator.WriteConfigFileFromLink(route.Link, port, out message, out address);
                if (string.IsNullOrWhiteSpace(configPath)) throw new InvalidOperationException(message ?? "Unable to create Hysteria config.");

                process = ShellExecute.ShellexecAndReturnProcess(path, $"client -c \"{configPath}\"");
                if (process == null || !WaitForPort("127.0.0.1", port, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Hysteria SOCKS endpoint did not become ready.");

                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupConfigFile(configPath);
                return new HysteriaRuntime { Route = route, Port = port, Process = process };
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                TryKillProcess(process);
                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupConfigFile(configPath);
                ReleasePort(port);
                return null;
            }
        }

        private void StartCore(string wrapperConfig, string xrayConfig)
        {
            EnsureCoreRunning();
            var response = ExecuteCoreCall(client =>
            {
                try { client.Stop(); } catch { }
                return client.Start(new LoadConfigReq
                {
                    CoreConfig = wrapperConfig ?? string.Empty,
                    DisableStats = false,
                    NeedExtraProcess = false,
                    ExtraProcessPath = string.Empty,
                    ExtraProcessArgs = string.Empty,
                    ExtraProcessConf = string.Empty,
                    ExtraProcessConfDir = string.Empty,
                    ExtraNoOut = false,
                    NeedXray = true,
                    XrayConfig = xrayConfig ?? string.Empty
                });
            }, true);

            if (!string.IsNullOrWhiteSpace(response?.Error)) throw new InvalidOperationException(response.Error);
        }

        private void EnsureCoreRunning()
        {
            lock (coreLock)
            {
                if (throneClient != null && throneClient.IsConnected) return;
                ResetCore();

                var corePath = ResolveCorePath();
                var thronePath = ResolveThronePath();
                if (!File.Exists(corePath)) throw new FileNotFoundException("Core executable was not found.", corePath ?? string.Empty);
                if (!File.Exists(thronePath)) throw new FileNotFoundException("Throne executable was not found.", thronePath ?? string.Empty);

                ShellExecute.KillProccess("Throne");
                ShellExecute.KillProccess("SGUARD64");
                ShellExecute.KillProccess("SGUARD32");
                coreProcess = ShellExecute.ShellexecAndReturnProcess(thronePath, $"\"{corePath}\"", Path.GetDirectoryName(corePath));
                if (coreProcess == null) throw new InvalidOperationException("Unable to start Throne.");
                coreOwned = true;
                coreProcess.EnableRaisingEvents = true;
                coreProcess.Exited += CoreProcessExited;
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                try
                {
                    throneClient = new ThronePipeClient();
                    throneClient.Connect(1000);
                    return;
                }
                catch
                {
                    try { throneClient?.Dispose(); } catch { }
                    throneClient = null;
                    Thread.Sleep(100);
                }
            }
            throw new TimeoutException("Throne relay did not start in time.");
        }

        private T ExecuteCoreCall<T>(Func<ThronePipeClient, T> call, bool reconnect)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (throneClient == null || !throneClient.IsConnected) EnsureCoreRunning();
                    return call(throneClient);
                }
                catch (Exception ex) when (ex is IOException || ex is EndOfStreamException || ex is ObjectDisposedException || ex is TimeoutException || ex is InvalidOperationException)
                {
                    if (attempt >= (reconnect ? 1 : 0)) throw;
                    try { throneClient?.Dispose(); } catch { }
                    throneClient = null;
                    Thread.Sleep(200);
                    EnsureCoreRunning();
                }
            }
        }

        private long ProbeThroughSocks(int port)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                if (TrySocksHeadRequest("127.0.0.1", port, "connectivitycheck.gstatic.com", 443, "/generate_204", 7000))
                    return Math.Max(1L, stopwatch.ElapsedMilliseconds);
                Thread.Sleep(500);
            }
            return -1;
        }

        private static bool TrySocksHeadRequest(string proxyHost, int proxyPort, string destinationHost, int destinationPort, string path, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(proxyHost, proxyPort);
                    if (!connectTask.Wait(timeoutMs) || !client.Connected) return false;
                    client.ReceiveTimeout = timeoutMs;
                    client.SendTimeout = timeoutMs;

                    using (var stream = client.GetStream())
                    {
                        stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
                        var greeting = ReadExact(stream, 2);
                        if (greeting[0] != 5 || greeting[1] != 0) return false;

                        var hostBytes = Encoding.ASCII.GetBytes(destinationHost);
                        var request = new byte[7 + hostBytes.Length];
                        request[0] = 5; request[1] = 1; request[2] = 0; request[3] = 3; request[4] = (byte)hostBytes.Length;
                        Buffer.BlockCopy(hostBytes, 0, request, 5, hostBytes.Length);
                        request[5 + hostBytes.Length] = (byte)(destinationPort >> 8);
                        request[6 + hostBytes.Length] = (byte)destinationPort;
                        stream.Write(request, 0, request.Length);

                        var reply = ReadExact(stream, 4);
                        if (reply[0] != 5 || reply[1] != 0) return false;
                        ConsumeSocksAddress(stream, reply[3]);

                        using (var ssl = new SslStream(stream, false, (sender, certificate, chain, errors) => true))
                        {
                            ssl.ReadTimeout = timeoutMs;
                            ssl.WriteTimeout = timeoutMs;
                            ssl.AuthenticateAsClient(destinationHost, null, SslProtocols.Tls12, true);
                            var http = Encoding.ASCII.GetBytes($"HEAD {path} HTTP/1.1\r\nHost: {destinationHost}\r\nConnection: close\r\n\r\n");
                            ssl.Write(http, 0, http.Length);
                            ssl.Flush();
                            return ssl.ReadByte() >= 0;
                        }
                    }
                }
            }
            catch { return false; }
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }

        private static void ConsumeSocksAddress(Stream stream, byte addressType)
        {
            if (addressType == 1) ReadExact(stream, 4);
            else if (addressType == 3)
            {
                var length = stream.ReadByte();
                if (length < 0) throw new EndOfStreamException();
                ReadExact(stream, length);
            }
            else if (addressType == 4) ReadExact(stream, 16);
            else throw new InvalidDataException("Invalid SOCKS address type.");
            ReadExact(stream, 2);
        }

        private void CoreProcessExited(object sender, EventArgs e)
        {
            if (stopping || userCancelRequested || !isConnected) return;
            isConnected = false;
            ResetRuntime(false);
            onConnectDisconnect?.Invoke(this, false, 0, "FASTEST CONNECTION core stopped.");
        }

        private void DisconnectInternal(bool silent)
        {
            var wasConnected = isConnected;
            ResetRuntime(true);
            if (!silent && (wasConnected || userCancelRequested)) onConnectDisconnect?.Invoke(this, false, 0, string.Empty);
        }

        private void ResetRuntime(bool disableProxy)
        {
            lock (runtimeLock)
            {
                stopping = true;
                isConnected = false;
                if (disableProxy && useSystemProxy) try { SystemProxy.Disable(); } catch { }

                ResetCore();
                foreach (var runtime in hysteriaRuntimes.ToList())
                {
                    TryKillProcess(runtime.Process);
                    ReleasePort(runtime.Port);
                }
                hysteriaRuntimes.Clear();
                ShellExecute.KillProccess("hysteria-windows-amd64");
                ShellExecute.KillProccess("hysteria-windows-386");
                IRSpeedyVPN.Services.Hysteria.ConfigGenerator.CleanupAllConfigFile();

                foreach (var port in ownedPorts.ToList()) ReleasePort(port);
                ownedPorts.Clear();
                listenPort = 0;
                xraySocksPort = 0;
                buildResult = null;
                stopping = false;
            }
        }

        private void ResetCore()
        {
            try { throneClient?.Stop(); } catch { }
            try { throneClient?.Dispose(); } catch { }
            throneClient = null;

            if (coreProcess != null)
            {
                try { coreProcess.Exited -= CoreProcessExited; } catch { }
                if (coreOwned) TryKillProcess(coreProcess);
            }
            coreProcess = null;
            coreOwned = false;
            ShellExecute.KillProccess("Throne");
            ShellExecute.KillProccess("SGUARD64");
            ShellExecute.KillProccess("SGUARD32");
        }

        private int AllocatePort()
        {
            var port = FreePortManager.Dequeue();
            if (port <= 0) throw new InvalidOperationException("Unable to allocate a local port.");
            ownedPorts.Add(port);
            return port;
        }

        private void ReleasePort(int port)
        {
            if (port > 0 && ownedPorts.Remove(port)) FreePortManager.Enqueue(port);
        }

        private static bool WaitForPort(string host, int port, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        var task = client.ConnectAsync(host, port);
                        if (task.Wait(200) && client.Connected) return true;
                    }
                }
                catch { }
                Thread.Sleep(100);
            }
            return false;
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch { }
            try { process.Dispose(); } catch { }
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
            catch { return new string[0]; }
        }

        private string[] GetExcludedProcessPaths()
        {
            return new[] { ResolveCorePath(), ResolveHysteriaCorePath() }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string ResolveCorePath()
        {
            var file = (Tools.IsWin7OrLower() ? "SGuard7" : "SGuard") + (Environment.Is64BitOperatingSystem ? "64.exe" : "32.exe");
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
            var file = "hysteria-windows-" + (Environment.Is64BitOperatingSystem ? "amd64.exe" : "386.exe");
            var path = Path.Combine(gInfo.TempPath, "hysteria", file);
            return File.Exists(path) ? path : null;
        }

        private static bool IsHysteria2Link(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return false;
            return link.StartsWith(v2rayN.Global.Hysteria2ProtocolLite, StringComparison.OrdinalIgnoreCase)
                || link.StartsWith(v2rayN.Global.Hysteria2Protocol, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class HysteriaRuntime
        {
            public FastestRoute Route { get; set; }
            public int Port { get; set; }
            public Process Process { get; set; }
        }
    }
}
