using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services.Hysteria;
using IRSpeedyVPN.Services.Libcore;
using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.Windows;
using Newtonsoft.Json.Linq;
using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Security;
using System.Web.UI.WebControls.WebParts;
using v2rayN;
using v2rayN.Handler;
using v2rayN.Mode;

namespace IRSpeedyVPN.Services
{
    class TunnelPlusService : IVPNService, ISmartFastConnection
    {
        NewServiceController serviceController { get; set; }
        GlobalInfo gInfo;
        private string name;
        bool cancelUrlTest;
        long urlTestSpeed;
        DateTime lastUrlTest;
        int _xraySocksPort;
        string _singboxLinkOverride;
        public static string selectedChain;
        public static object grpcLock=new object();
        public string Name { get => name ?? (server.urls.FirstOrDefault().url.StartsWith("trojan:") ? "VPN+" : "V-Guard".ToUpper());
            set => name = value;
        } 
        public int ID => server.ID;
        public string[] Protocols => server.Protocol == null ? new string[0] : server.Protocol.Split(' ');

        public bool isUsingProxifire = true;
        public bool IsUsingProxifire => isUsingProxifire;
        public string Country => (server.urls.Count() > 0 && server.urls.FirstOrDefault().url.StartsWith("ssr:") ? "*" : "") + server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");
        public string CountryCode => server.Country;
        public string SelectedProtocol => null;

        public byte Order => (byte)((server.urls==null||server.urls.Count==0 || server.urls.FirstOrDefault().url.StartsWith("trojan:")) ? 0 : 1);
        public byte CountryIndex { get; set; }
        public Type SettingType => typeof(VGAURDServiceSetting);
        string selectedUrl;
        public string SelectedUrl => selectedUrl ?? server.urls.FirstOrDefault()?.url;
        string[] _smartFastUrls;
        bool IsConnected=false;
        bool userCancelRequested;
        int reconnecting;


        public ProxifierType ProxifierRuleType {
            get
            {
                if (RegHelper.GetSettingValue("VGAURDGlobalProxy") != "0")
                    return ProxifierType.Global;
               else if (RegHelper.GetSettingValue("VGAURDSystemProxy") == "1")
                    return ProxifierType.None;
                else if (RegHelper.GetSettingValue("ProxifierSmartRoute") == "2")
                    return ProxifierType.Telegram;
                return ProxifierType.None;

            }
        }

        public bool ProxifierWithPassword =>false;

        public ProxyType ProxyType => ProxyType.SOCKS;
        public bool ShowSpeedyShieldSetting => true;
        public bool IsShareActive { get; set; }
        public int? HttpPort => lastListenPort;
        public int? SocksPort => lastListenPort;

        public bool IsUrlTestSupported => true;

        public long UrlTestSpeed => urlTestSpeed;

        public Url SelectedServerUrl { get; set; }

        IServer server;

        public event OnConnectDisconnect onConnectDisconnect;
        bool useSystemProxy = false;
        Process coreProcess;
        Process vpnCoreProcess;
        bool coreOwned;
        bool vpnCoreOwned;
        bool suppressCoreExit;
        string corePath;
        string sniCorePath;
        string lastLink;
        string lastVodLink;
        int lastListenPort = 1080;
        bool lastVpnMode;
        DateTime lastCoreStartUtc;
        readonly Queue<DateTime> recentCoreExitsUtc = new Queue<DateTime>();
        readonly object reconnectLock = new object();
        const int MaxCoreExitsInWindow = 10;
        const int CoreExitWindowSeconds = 30;
        const int ImmediateExitSeconds = 2;
        static readonly object coreLock = new object();
        readonly object sniLock = new object();
        readonly Dictionary<string, SniRuntime> serviceSniServers = new Dictionary<string, SniRuntime>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<int> activeSniPorts = new HashSet<int>();
        const int CorePort = 19810;
        const int VpnCorePort = 19811;
        const int CoreConnectTimeoutMs = 8000;
        const int CoreConnectRetryDelayMs = 200;        
        const string SniScheme = "sni://";
        int nextSniListenPort = 40443;
        public TunnelPlusService(IServer server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            corePath = ResolveCorePath();
            serviceController = AppServices.NewServiceController;
        }

        public void Connect(string protocol)
        {
            userCancelRequested = false;
            useSystemProxy = (ProxifierRuleType == ProxifierType.None);
            IsConnected = false;
            ((Action)(() => RunV2ray(/*SelectedUrl*/))).BeginInvoke(null, null);
        }
        public bool IsSmartFast => _smartFastUrls != null && _smartFastUrls.Length > 0;

        public void SetSmartFastUrls(string[] successUrls)
        {
            _smartFastUrls = (successUrls ?? new string[0])
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        void RunV2ray(string goUrl=null,int port=1080)
        {
            try
            {
                bool isVodEnabled = RegHelper.GetSettingValue("VGAURDVodService") == "1";
                if (serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
                // if (ServiceHelper.CheckAvailabilty(gInfo.Username,gInfo.Password))
                {
                    StopAndDrainUrlTests();
                    if (goUrl == null)
                    {
                        KillAll();
                        // smart fast already ran the url test and collected the success urls
                        if (_smartFastUrls == null || _smartFastUrls.Length == 0)
                        {
                            UrlTest(SelectedServerUrl != null ? new[] { SelectedServerUrl } : null, true);

                            if (urlTestSpeed < 0)
                            {
                                TryStopCore();
                                
                                if (onConnectDisconnect != null)
                                    onConnectDisconnect.Invoke(this, false, 0, "سروری یافت نشد");
                                return;

                            }
                            else if (isVodEnabled)
                            {
                                VodUrlTest();
                            }
                            //LogHelper.WriteExLog($"ss\t{selectedUrl}\n");
                        }
                        else if (isVodEnabled)
                        {
                            // A smart or country pool runs its own url test, so the
                            // branch above is skipped and VOD would keep a stale link
                            // from an earlier connection, or none at all.
                            VodUrlTest();
                        }

                    }                    
                    /* var configData = new V2Ray.V2RayHandler().GetV2RayConfig(server.Address, port);
                     File.WriteAllText(this.configPath, configData);
                     File.SetAttributes(this.configPath, FileAttributes.Hidden);
                      //vprocess = ShellExecute.ShellexecAndReturnProcessRedirectOutput(v2rayPath, "-config stdin:");
                     vprocess = ShellExecute.ShellexecAndReturnProcess(v2rayPath, "-c " + configPath); */
                    //vprocess.StandardInput.Write(configData);
                    //vprocess.StandardInput.Close();
                    bool vpnmode =  (RegHelper.GetSettingValue("VGAURDVPNMode") != "0");
                    lastLink = goUrl ?? SelectedUrl;
                    lastListenPort = port;
                    lastVpnMode = vpnmode;
                    var shieldFiles = GetShieldFiles();
                    var isSmartFast = _smartFastUrls != null && _smartFastUrls.Length > 0;
                    var sniRuntime = isSmartFast ? null : GetSniRuntime(lastLink, serviceSniServers, true);
                    var chainLink = isSmartFast ? null : GetChainLink(lastLink);
                    var defaultChainLink = GetDefaultChainLink();
                    bool needXray = false;
                    string xrayConfig = null;
                    string singboxLink = lastLink; // Default to lastLink

                    if (isSmartFast)
                    {
                        // smart fast: force every success url into an xray outbound
                        // (hysteria2 urls included) and let the balancer pick the fastest
                        var smartUrls = _smartFastUrls
                            .Where(u => !string.IsNullOrWhiteSpace(u))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        if (smartUrls.Count == 0)
                        {
                            TryStopCore();
                            if (onConnectDisconnect != null)
                                onConnectDisconnect.Invoke(this, false, 0, "سروری یافت نشد");
                            return;
                        }

                        needXray = true;
                        _xraySocksPort = FreePortManager.Dequeue();
                        var authUser = Guid.NewGuid().ToString("N");
                        var authPass = Guid.NewGuid().ToString("N");

                        xrayConfig = Xray.ConfigGenerator.GetSmartBalancerConfig(
                            smartUrls, _xraySocksPort, authUser, authPass);
                        if (string.IsNullOrWhiteSpace(xrayConfig))
                        {
                            if (_xraySocksPort > 0) FreePortManager.Enqueue(_xraySocksPort);
                            _xraySocksPort = 0;
                            TryStopCore();
                            if (onConnectDisconnect != null)
                                onConnectDisconnect.Invoke(this, false, 0, "سروری یافت نشد");
                            return;
                        }

                        // sing-box relays the local listener to the xray SOCKS inbound
                        singboxLink = $"socks://{authUser}:{authPass}@127.0.0.1:{_xraySocksPort}";
                        _singboxLinkOverride = null;
                    }
                    else if (Xray.ConfigGenerator.LinkNeedsXray(lastLink))
                    {
                        needXray = true;
                        _xraySocksPort = FreePortManager.Dequeue();
                        var authUser = Guid.NewGuid().ToString("N");
                        var authPass = Guid.NewGuid().ToString("N");

                        // Generate Xray config
                        xrayConfig = Xray.ConfigGenerator.GetConfig(lastLink, _xraySocksPort, authUser, authPass);

                        string address = JObject.Parse(xrayConfig)?["outbounds"]?[0]?["settings"]?["address"]?.ToString();

                        string downloadAddress = JObject.Parse(xrayConfig)?["outbounds"]?[0]?["streamSettings"]?["xhttpSettings"]?["extra"]?["downloadSettings"]?["address"]?.ToString();
                        // Create SOCKS URL for Sing-box to connect to Xray
                        singboxLink = $"socks://{authUser}:{authPass}@127.0.0.1:{_xraySocksPort}?host={address},{downloadAddress}";
                        _singboxLinkOverride = null;
                    }
                    /*
                    else if (IsHysteria2Link(lastLink))
                    {
                        _hysteriaSocksPort = FreePortManager.Dequeue();
                        string msg;
                        var configPath = ConfigGenerator.WriteConfigFileFromLink(lastLink, _hysteriaSocksPort, out msg, out var address);
                        if (configPath == null || !StartHysteriaProcess(configPath, _hysteriaSocksPort))
                        {
                            StopSniServers(serviceSniServers);
                            if (_hysteriaSocksPort > 0) FreePortManager.Enqueue(_hysteriaSocksPort);
                            _hysteriaSocksPort = 0;
                            onConnectDisconnect?.Invoke(this, false, 0, "Failed to start Hysteria process");
                            return;
                        }
                        singboxLink = $"socks://127.0.0.1:{_hysteriaSocksPort}?host={address}";
                        _singboxLinkOverride = singboxLink;
                    }
                    */
                    // Now generate Sing-box config with the appropriate parameter
                    var configData = SingBox.ConfigGenerator.GetConfig(
                        singboxLink,  // Either lastLink or SOCKS URL
                        port,
                        vpnmode,
                        IsShareActive,
                        shieldFiles,
                        chainLink,
                        new string[] { defaultChainLink, selectedChain },
                        lastVodLink,
                        !string.IsNullOrEmpty(defaultChainLink),
                        sniRuntime?.ListenHost,
                        sniRuntime?.ListenPort,
                        new string[] { ResolveCorePath() }
                    );

                    if (!TryStartCoreWithConfig(configData, out var startError, needXray, xrayConfig))
                    {
                        StopSniServers(serviceSniServers);
                        if (needXray && _xraySocksPort > 0) FreePortManager.Enqueue(_xraySocksPort);
                        _xraySocksPort = 0;
                        onConnectDisconnect?.Invoke(this, false, 0, startError);
                        return;
                    }

                    IsConnected = true;
                    if (vpnmode)
                    {                      
                        useSystemProxy = false;
                    }
                    else if (useSystemProxy)
                    {
                        WinINet.SetIEProxy(true, true, $"http://127.0.0.1:{port}", null);
                    }
                    isUsingProxifire = !vpnmode && ProxifierRuleType != ProxifierType.None;
                    if (onConnectDisconnect != null)
                    {
                        onConnectDisconnect.Invoke(this, true, port, "");

                    }
                }
                else
                {
                    onConnectDisconnect.Invoke(this, false, 0, "تعداد اتصالات بیش از حد مجاز است");
                }
            }
            catch (Exception ex)
            {
                StopSniServers(serviceSniServers);
                TryStopCore();
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, ex.Message);
            }
        }

        public void ApplyShareSetting()
        {
            if (!IsConnected)
                return;

            try
            {/*
                var shieldFiles = GetShieldFiles();
                var sniRuntime = GetSniRuntime(lastLink ?? SelectedUrl, serviceSniServers, true);
                var chainLink = GetChainLink(lastLink ?? SelectedUrl);
                var defaultChainLink = GetDefaultChainLink();
                var effectiveLink = _singboxLinkOverride ?? lastLink ?? SelectedUrl;
                var configData = SingBox.ConfigGenerator.GetConfig(effectiveLink, lastListenPort, lastVpnMode, IsShareActive, shieldFiles, chainLink, new string[] { defaultChainLink, selectedChain  }, lastVodLink, !string.IsNullOrEmpty(defaultChainLink), sniRuntime?.ListenHost, sniRuntime?.ListenPort);
                if (!TryStartCoreWithConfig(configData, out var startError))
                {
                    onConnectDisconnect?.Invoke(this, false, 0, startError);
                }*/
                RunV2ray(lastLink ?? SelectedUrl);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        private bool TryStartCoreWithConfig(string configData, out string error, bool needXray = false, string xrayConfig = null)
        {
            error = null;
            EnsureCoreRunning(CorePort, ref coreProcess, ref coreOwned);
            ErrorResp startResp;
            try
            {
                startResp = ExecuteCoreCall(client =>
                {
                    SafeStopCore(client);
                    return client.Start(new LoadConfigReq
                    {
                        CoreConfig = configData ?? "",
                        DisableStats = false,
                        NeedExtraProcess = false,
                        ExtraProcessPath = "",
                        ExtraProcessArgs = "",
                        ExtraProcessConf = "",
                        ExtraProcessConfDir = "",
                        ExtraNoOut = false,
                        NeedXray = needXray,
                        XrayConfig = xrayConfig ?? ""
                    });
                });
            }
            catch (TimeoutException ex)
            {
                LogHelper.WriteLog(ex);
                error = "Timeout connecting to core service.";
                TryStopCore();
                return false;
            }
            if (!string.IsNullOrEmpty(startResp.Error))
            {
                error = startResp.Error;
                TryStopCore();
                return false;
            }
            return true;
        }

        private string[] GetShieldFiles()
        {
            try
            {
                var raw = RegHelper.GetSettingValue("ShieldFilterFiles") ?? string.Empty;
                return raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        private string GetChainLink(string primaryLink)
        {
            if (string.IsNullOrWhiteSpace(primaryLink) || server?.urls == null)
                return null;
            var url = server.urls.FirstOrDefault(x =>
                x != null && string.Equals(x.url, primaryLink, StringComparison.OrdinalIgnoreCase));
            if (url != null && url.chainproxy == 1 && !string.IsNullOrWhiteSpace(url.extra_field_1))
            {
                if (!string.IsNullOrWhiteSpace(ExtractSniLink(url.extra_field_1)))
                    return null;
                return url.extra_field_1;
            }
            return null;
        }

        private string GetSniLink(string primaryLink)
        {
            if (string.IsNullOrWhiteSpace(primaryLink) || server?.urls == null)
                return null;
            var url = server.urls.FirstOrDefault(x =>
                x != null && string.Equals(x.url, primaryLink, StringComparison.OrdinalIgnoreCase));
            return ExtractSniLink(url?.extra_field_1);
        }

        private string ExtractSniLink(string extraField)
        {
            if (string.IsNullOrWhiteSpace(extraField))
                return null;

            var tokens = extraField
                .Split(new[] { ' ', '\r', '\n', '\t', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var trimmed = token.Trim();
                if (trimmed.StartsWith(SniScheme, StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
            return null;
        }

        private SniRuntime GetSniRuntime(string primaryLink, Dictionary<string, SniRuntime> runtimes, bool persistent)
        {
            var sniLink = GetSniLink(primaryLink);
            if (string.IsNullOrWhiteSpace(sniLink))
                return null;

            return EnsureSniRuntime(sniLink, runtimes, persistent);
        }

        private string GetDefaultChainLink()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chain.txt");
                if (!File.Exists(path))
                    return null;

                var content = File.ReadAllText(path).Trim();
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                string msg;
                var item = ShareHandler.ImportFromConfigLink(content, out msg);
                if (item == null)
                    return null;

                return content;
            }
            catch
            {
                return null;
            }
        }

        private string[] BuildChainLinks(string chainLink, string[] defaultChainLink)
        {
            defaultChainLink = defaultChainLink.Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (string.IsNullOrWhiteSpace(chainLink) && (defaultChainLink == null || defaultChainLink.Length == 0))
                return null;

            if (!string.IsNullOrWhiteSpace(chainLink) && defaultChainLink != null && defaultChainLink.Length > 0
                && string.Equals(chainLink.Trim(), defaultChainLink[0].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return defaultChainLink;
            }

            if (!string.IsNullOrWhiteSpace(chainLink) &&  defaultChainLink != null && defaultChainLink.Length > 0)
            {
                List<string> chainLinks = new List<string>();
                chainLinks.AddRange(defaultChainLink);
                chainLinks.Add(chainLink);            
                return chainLinks.ToArray();
            }

            if (!string.IsNullOrWhiteSpace(chainLink))
                return new[] { chainLink };

            return defaultChainLink;
        }

      
     
      

        public void Disconnect()
        {
            DisconnectInternal(true, false, true);
        }
        public void Disconnect(bool chkprocess, bool silent = false)
        {
            DisconnectInternal(chkprocess, silent, true);
        }
        private void DisconnectInternal(bool chkprocess, bool silent, bool userCanceled)
        {
            if (userCanceled)
                userCancelRequested = true;
            if (useSystemProxy)
                SystemProxy.Disable();
            lock (this)
            {
                suppressCoreExit = true;
                if (chkprocess)
                {
                    StopSniServers(serviceSniServers);
                    TryStopCore();
              
                    if (vpnCoreOwned && vpnCoreProcess != null)
                    {
                        TryKillProcess(vpnCoreProcess);
                    }
                }
                else
                {
                    KillAll();
                }
                if (_xraySocksPort > 0)
                {
                    FreePortManager.Enqueue(_xraySocksPort);
                    _xraySocksPort = 0;
                }
               
                _singboxLinkOverride = null;
                suppressCoreExit = false;                
                IsConnected = false;

                if (onConnectDisconnect != null && !silent)
                    onConnectDisconnect.Invoke(this, false, 0, "");

            }
        }
        void KillAll()
        {
            if (IsConnected)
            {

            }
            TryStopCore();
            StopSniServers(serviceSniServers);
            ShellExecute.KillProccess("sni");
            ShellExecute.KillProccess("hysteria");
        }
        public void DisconnectAll()
        {
            DisconnectInternal(false, false, true);
        }

        public bool IsRequirementAvailable()
        {
            if (string.IsNullOrEmpty(corePath) || !File.Exists(corePath))
            {
                corePath = ResolveCorePath();
            }
            return !string.IsNullOrEmpty(corePath) && File.Exists(corePath);
        }        

        public long UrlTest()
        {
            return UrlTest(null, false);
        }

        public long UrlTest(Url[] urls)
        {
            return UrlTest(urls, false);
        }

        public long UrlTest(Url[] urls, bool force)
        {
            if (!force && UrlTestCoordinator.AbortRequested && urls == null)
                return urlTestSpeed;

            bool shouldRun;
            if (urls != null)
                shouldRun = true;
            else if (urlTestSpeed < 0 || lastUrlTest == null || (DateTime.Now - lastUrlTest) > TimeSpan.FromSeconds(60))
                shouldRun = true;
            else
                shouldRun = false;

            if (shouldRun)
            {
                var task = Task.Factory.StartNew(() => UrlTestFull(urls, force));
                while (!task.Wait(200))
                {
                    if (!force && UrlTestCoordinator.AbortRequested)
                    {
                        cancelUrlTest = true;
                        break;
                    }
                }
                cancelUrlTest = true;
                lastUrlTest = DateTime.Now;
            }
            return urlTestSpeed;
        }
        public void UrlTestFull(Url[] urls = null, bool force = false)
        {
            cancelUrlTest = false;
            if (!force && UrlTestCoordinator.AbortRequested)
                return;
            urlTestSpeed = -1;
            selectedUrl = null;
            try
            {
                var sourceUrls = (urls != null ? (IEnumerable<Url>)urls : server.urls)
                    .Where(u => u != null && !string.IsNullOrWhiteSpace(u.url))
                    .ToList();

                var urlObjects = new Dictionary<string, Url>(StringComparer.Ordinal);
                foreach (var u in sourceUrls)
                    if (!urlObjects.ContainsKey(u.url))
                        urlObjects[u.url] = u;

                Dictionary<string, string[]> allUrls = new Dictionary<string, string[]>();
                var urlTestSniServers = new Dictionary<string, SniRuntime>(StringComparer.OrdinalIgnoreCase);
                var urlTestOverrides = new Dictionary<string, SingBox.ConfigGenerator.EndpointOverride>(StringComparer.OrdinalIgnoreCase);
                var defaultChainLink = GetDefaultChainLink();
                sourceUrls.Randomize()
                    .Select(u => u.url)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .ToList()
                    .ForEach(u =>
                    {
                        if (!allUrls.ContainsKey(u))
                        {
                            var sniRuntime = GetSniRuntime(u, urlTestSniServers, false);
                            allUrls.Add(u, BuildChainLinks(GetChainLink(u), new string[] { defaultChainLink, selectedChain }));
                            if (sniRuntime != null)
                            {
                                urlTestOverrides[u] = new SingBox.ConfigGenerator.EndpointOverride
                                {
                                    Server = sniRuntime.ListenHost,
                                    ServerPort = sniRuntime.ListenPort
                                };
                            }
                        }
                    });
                
                if (allUrls.Count == 0)
                {
                    return;
                }

                // Separate xhttp links for Xray processing
                var xrayInfos = new List<Xray.ConfigGenerator.XraySocksInfo>();
                var socksOverrides = new Dictionary<string, Tuple<int, string, string>>();
                var urlTestHysteriaProcs = new List<Process>();
                var urlTestConfigPaths = new List<string>();
                var urlTestHysteriaPorts = new List<int>();
                int port = -1;
                try
                {
                    foreach (var kvp in allUrls)
                    {
                        if (Xray.ConfigGenerator.LinkNeedsXray(kvp.Key))
                        {
                            var socksPort = FreePortManager.Dequeue();
                            var authUser = Guid.NewGuid().ToString("N");
                            var authPass = Guid.NewGuid().ToString("N");
                            var tag = $"xray-{xrayInfos.Count}";
                            xrayInfos.Add(new Xray.ConfigGenerator.XraySocksInfo
                            {
                                Link = kvp.Key,
                                Tag = tag,
                                Port = socksPort,
                                User = authUser,
                                Pass = authPass
});
                            socksOverrides[kvp.Key] = Tuple.Create(socksPort, authUser, authPass);
                        }
                        /*
                        else if (IsHysteria2Link(kvp.Key))
                        {
                            var socksPort = FreePortManager.Dequeue();
                            var configPath = ConfigGenerator.WriteConfigFileFromLink(kvp.Key, socksPort, out _, out _);
                            if (configPath != null)
                                urlTestConfigPaths.Add(configPath);
                            if (configPath != null)
                            {
                                var hyPath = ResolveHysteriaCorePath();
                                if (File.Exists(hyPath))
                                {
                                    var proc = ShellExecute.ShellexecAndReturnProcess(hyPath, $"client -c \"{configPath}\"");
                                    if (proc != null)
                                        urlTestHysteriaProcs.Add(proc);
                                    if (proc != null && WaitForPort("127.0.0.1", socksPort, TimeSpan.FromSeconds(5)))
                                    {
                                        socksOverrides[kvp.Key] = Tuple.Create(socksPort, "", "");
                                        urlTestHysteriaPorts.Add(socksPort);
                                    }
                                }
                            }
                            if (!socksOverrides.ContainsKey(kvp.Key))
                                FreePortManager.Enqueue(socksPort);
                        }*/
                    }

                    port = FreePortManager.Dequeue();

                    if (!force && (cancelUrlTest || UrlTestCoordinator.AbortRequested))
                        return;

                    TestResp resp;
                    Dictionary<string, string> tagToUrl;
                    lock (grpcLock)
                    {
                        if (!force && (cancelUrlTest || UrlTestCoordinator.AbortRequested))
                            return;

                        EnsureCoreRunning(CorePort, ref coreProcess, ref coreOwned);

                        var configData = SingBox.ConfigGenerator.GetUrlTestConfig(allUrls, port, out tagToUrl, urlTestOverrides, socksOverrides);

                        if (tagToUrl.Count == 0)
                        {
                            return;
                        }

                        bool needXray = xrayInfos.Count > 0;
                        string xrayConfig = needXray ? Xray.ConfigGenerator.GetUrlTestXrayConfig(xrayInfos) : "";

                        resp = ExecuteCoreCall(client => client.Test(new TestReq
                        {
                            Config = configData ?? "",
                            OutboundTags = tagToUrl.Keys.ToList(),
                            //UseDefaultOutbound = false,
                            Url = gInfo?.settings?.setting?.url_test ?? "https://www.google.com/generate_204",
                            //TestCurrent = false,
                            MaxConcurrency = 10,
                            TestTimeoutMs = 5000,
                            NeedXray = needXray,
                            XrayConfig = xrayConfig
                        }));
                    }
                    if (resp?.Results != null && !(cancelUrlTest || (!force && UrlTestCoordinator.AbortRequested)))
                    {
                        var testedUrls = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var result in resp.Results)
                        {
                            if (result == null || result.LatencyMs <= 0)
                                continue;
                            if (!tagToUrl.TryGetValue(result.OutboundTag, out var url))
                                continue;
                            if (urlTestSpeed < 0 || result.LatencyMs < urlTestSpeed)
                            {
                                urlTestSpeed = result.LatencyMs;
                                selectedUrl = url;
                            }
                            testedUrls.Add(url);
                            if (urlObjects.TryGetValue(url, out var urlObject))
                            {
                                urlObject.latency = result.LatencyMs;
                                urlObject.latencychkTime = DateTime.Now;
                            }
                        }

                        foreach (var kvp in urlObjects)
                        {
                            if (!testedUrls.Contains(kvp.Key))
                            {
                                kvp.Value.latency = -1;
                                kvp.Value.latencychkTime = DateTime.Now;
                            }
                        }
                    }

                }
                finally
                {
                    foreach (var p in urlTestHysteriaProcs)
                        TryKillProcess(p);
                    foreach (var cfg in urlTestConfigPaths)
                        ConfigGenerator.CleanupConfigFile(cfg);
                    foreach (var hyPort in urlTestHysteriaPorts)
                        FreePortManager.Enqueue(hyPort);
                    StopSniServers(urlTestSniServers);
                    if (port >= 0)
                        FreePortManager.Enqueue(port);
                    foreach (var info in xrayInfos)
                        FreePortManager.Enqueue(info.Port);
                }
            }
            catch (FileNotFoundException ex)
            {
                if (!force && UrlTestCoordinator.AbortRequested)
                    return;
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, "1 خطا در بررسی سرورها");
                return;
            }
            catch(Exception ex)
            {               
                if (!force && (cancelUrlTest || UrlTestCoordinator.AbortRequested))
                    return;
                LogHelper.WriteLog(ex);
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, "خطا در بررسی سرورها");
            }/*
            if (!IsConnected)
            {
                TryStopCore(CorePort);
                if (coreOwned && coreProcess != null)
                {
                    TryKillProcess(coreProcess);
                }
            }*/
        }

        private void VodUrlTest()
        {
            lastVodLink = null;
            if (!IsSmartFast && urlTestSpeed <= 0)
                return;

            try
            {
                Dictionary<string, string[]> vodUrls = new Dictionary<string, string[]>();
                gInfo.Vods
                   .Select(u => u.url)
                   .Distinct()
                   .ToList()
                   .ForEach(u => vodUrls.Add(u, new string[] { GetDefaultChainLink() }));
                if (vodUrls.Count == 0)
                    return;

                // With a single VOD link the latency probe only re-confirms that one
                // link, adding seconds to the connect for no selection benefit. Use it
                // directly; ApplyVodOutbound still validates it when the config is
                // built. The probe only earns its cost when it picks among several.
                if (vodUrls.Count == 1)
                {
                    lastVodLink = vodUrls.Keys.First();
                    return;
                }

                // Separate xhttp links for Xray processing in VOD test
                var vodXhttpInfos = new List<Xray.ConfigGenerator.XraySocksInfo>();
                var vodSocksOverrides = new Dictionary<string, Tuple<int, string, string>>();

                foreach (var kvp in vodUrls)
                {
                    if (Xray.ConfigGenerator.LinkNeedsXray(kvp.Key))
                    {
                        var socksPort = FreePortManager.Dequeue();
                        var authUser = Guid.NewGuid().ToString("N");
                        var authPass = Guid.NewGuid().ToString("N");
                        var tag = $"vod-xray-{vodXhttpInfos.Count}";
                        vodXhttpInfos.Add(new Xray.ConfigGenerator.XraySocksInfo
                        {
                            Link = kvp.Key,
                            Tag = tag,
                            Port = socksPort,
                            User = authUser,
                            Pass = authPass
                        });
                        vodSocksOverrides[kvp.Key] = Tuple.Create(socksPort, authUser, authPass);
                    }
                }

                int port = FreePortManager.Dequeue();
                try
                {
                    EnsureCoreRunning(CorePort, ref coreProcess, ref coreOwned);
                    var vodConfig = SingBox.ConfigGenerator.GetUrlTestConfig(vodUrls, port, out var vodTagToUrl, null, vodSocksOverrides);

                    if (vodTagToUrl.Count == 0)
                        return;

                    bool needVodXray = vodXhttpInfos.Count > 0;
                    string vodXrayConfig = needVodXray ? Xray.ConfigGenerator.GetUrlTestXrayConfig(vodXhttpInfos) : "";

                    var vodResp = ExecuteCoreCall(client => client.Test(new TestReq
                    {
                        Config = vodConfig ?? "",
                        OutboundTags = vodTagToUrl.Keys.ToList(),
                        Url = "https://www.filimo.com/",
                        MaxConcurrency = 10,
                        TestTimeoutMs = 3000,
                        NeedXray = needVodXray,
                        XrayConfig = vodXrayConfig
                    }));

                    if (vodResp?.Results != null)
                    {
                        long best = -1;
                        foreach (var result in vodResp.Results)
                        {
                            if (result == null || result.LatencyMs <= 0)
                                continue;
                            if (!vodTagToUrl.TryGetValue(result.OutboundTag, out var url))
                                continue;
                            if (best < 0 || result.LatencyMs < best)
                            {
                                best = result.LatencyMs;
                                lastVodLink = url;
                            }
                        }
                    }
                }
                finally
                {
                    FreePortManager.Enqueue(port);
                    foreach (var info in vodXhttpInfos)
                        FreePortManager.Enqueue(info.Port);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        private void EnsureCoreRunning(int port, ref Process process, ref bool owned)
        {
            lock (coreLock)
            {
                if (ProtorpcClient.CanConnect("127.0.0.1", port, 200))
                {
                    return;
                }

                if (process != null)
                {
                    var staleProcess = process;
                    var hasExited = true;
                    try
                    {
                        hasExited = staleProcess.HasExited;
                    }
                    catch
                    {
                        hasExited = true;
                    }

                    if (!hasExited)
                    {
                        var probe = Stopwatch.StartNew();
                        while (probe.Elapsed < TimeSpan.FromSeconds(2))
                        {
                            if (ProtorpcClient.CanConnect("127.0.0.1", port, 250))
                                return;
                            Thread.Sleep(100);
                        }
                    }

                    try
                    {
                        staleProcess.Exited -= CoreProcess_Exited;
                        if (!hasExited && !staleProcess.HasExited)
                        {
                            LogHelper.WriteLog(
                                $"Owned Core process is alive but did not accept connections on 127.0.0.1:{port} after repeated checks; restarting it.");
                            TryKillProcess(staleProcess);
                        }
                    }
                    catch { }
                    finally { staleProcess.Dispose(); }
                    process = null;
                    owned = false;
                }

                if (string.IsNullOrEmpty(corePath) || !File.Exists(corePath))
                {
                    corePath = ResolveCorePath();
                }
                if (string.IsNullOrEmpty(corePath) || !File.Exists(corePath))
                {
                    throw new FileNotFoundException("Core executable not found in temp folder.", corePath ?? "");
                }

                var coreDir = Path.GetDirectoryName(corePath);
                var diagnostics = new StringBuilder();
                var diagnosticsLock = new object();
                var startedProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = corePath,
                        Arguments = $"-port {port}",
                        WorkingDirectory = coreDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                startedProcess.OutputDataReceived += (sender, args) =>
                    AppendCoreDiagnostic(diagnostics, diagnosticsLock, "stdout", args.Data);
                startedProcess.ErrorDataReceived += (sender, args) =>
                    AppendCoreDiagnostic(diagnostics, diagnosticsLock, "stderr", args.Data);
                startedProcess.Exited += CoreProcess_Exited;

                try
                {
                    if (!startedProcess.Start())
                        throw new InvalidOperationException("Core process could not be started.");
                    startedProcess.BeginOutputReadLine();
                    startedProcess.BeginErrorReadLine();
                }
                catch
                {
                    startedProcess.Exited -= CoreProcess_Exited;
                    startedProcess.Dispose();
                    throw;
                }

                process = startedProcess;
                owned = true;
                lastCoreStartUtc = DateTime.UtcNow;

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < CoreConnectTimeoutMs)
                {
                    if (ProtorpcClient.CanConnect("127.0.0.1", port, 200))
                        return;

                    if (startedProcess.HasExited)
                    {
                        startedProcess.WaitForExit();
                        var exitCode = startedProcess.ExitCode;
                        var message = BuildCoreStartupError(
                            $"Core exited before listening on 127.0.0.1:{port}. Exit code: {exitCode}.",
                            corePath,
                            diagnostics,
                            diagnosticsLock);
                        startedProcess.Exited -= CoreProcess_Exited;
                        startedProcess.Dispose();
                        process = null;
                        owned = false;
                        LogHelper.WriteLog(message);
                        throw new InvalidOperationException(message);
                    }

                    Thread.Sleep(100);
                }

                var timeoutMessage = BuildCoreStartupError(
                    $"Core did not start listening on 127.0.0.1:{port} within {CoreConnectTimeoutMs} ms.",
                    corePath,
                    diagnostics,
                    diagnosticsLock);
                startedProcess.Exited -= CoreProcess_Exited;
                TryKillProcess(startedProcess);
                startedProcess.Dispose();
                process = null;
                owned = false;
                LogHelper.WriteLog(timeoutMessage);
                throw new TimeoutException(timeoutMessage);
            }
        }

        private static void AppendCoreDiagnostic(StringBuilder buffer, object sync, string source, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            lock (sync)
            {
                if (buffer.Length < 16 * 1024)
                    buffer.Append('[').Append(source).Append("] ").AppendLine(line);
            }
        }

        private static string BuildCoreStartupError(
            string summary,
            string executable,
            StringBuilder diagnostics,
            object diagnosticsLock)
        {
            string output;
            lock (diagnosticsLock)
                output = diagnostics.ToString().Trim();
            if (string.IsNullOrEmpty(output))
                output = "Core produced no stdout/stderr output.";
            return $"{summary} Executable: {executable}{Environment.NewLine}{output}";
        }


        private void TryStopCore()
        {
            try
            {
                if (!ProtorpcClient.CanConnect("127.0.0.1", CorePort, 200))
                {
                    return;
                }
                var client = new LibcoreServiceClient("127.0.0.1", CorePort, CoreConnectTimeoutMs);                
                SafeStopCore(client);
            }
            catch { }
        }

        private void StopAndDrainUrlTests()
        {
            UrlTestCoordinator.CancelAll();
            try
            {
                if (ProtorpcClient.CanConnect("127.0.0.1", CorePort, 200))
                {
                    var client = new LibcoreServiceClient("127.0.0.1", CorePort, CoreConnectTimeoutMs);
                    client.StopTest();
                }
            }
            catch
            {
                // The barrier below still waits for the in-flight call to unwind.
            }

            lock (grpcLock)
            {
                // Wait until any in-flight URL Test RPC has released the shared Core.
            }
        }

        private void SafeStopCore(LibcoreServiceClient client)
        {
            try
            {
                client.Stop();
            }
            catch { }
        }

        private T ExecuteCoreCall<T>(Func<LibcoreServiceClient, T> call)
        {
            try
            {
                return call(new LibcoreServiceClient("127.0.0.1", CorePort, CoreConnectTimeoutMs));
            }
            catch (TimeoutException ex)
            {
                LogHelper.WriteLog(ex);
                EnsureCoreRunning(CorePort, ref coreProcess, ref coreOwned);
                Thread.Sleep(CoreConnectRetryDelayMs);
                return call(new LibcoreServiceClient("127.0.0.1", CorePort, CoreConnectTimeoutMs));
            }
        }

        private void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch { }
        }

        private SniRuntime EnsureSniRuntime(string sniLink, Dictionary<string, SniRuntime> runtimes, bool persistent)
        {
            if (string.IsNullOrWhiteSpace(sniLink))
                throw new ArgumentNullException(nameof(sniLink));
            if (runtimes == null)
                throw new ArgumentNullException(nameof(runtimes));

            lock (sniLock)
            {
                if (persistent)
                {
                    var toStop = serviceSniServers
                        .Where(x => !string.Equals(x.Key, sniLink, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Key)
                        .ToList();
                    foreach (var key in toStop)
                    {
                        StopSniRuntime(serviceSniServers[key], true);
                        serviceSniServers.Remove(key);
                    }
                }

                if (runtimes.TryGetValue(sniLink, out var current) && current != null && current.Process != null)
                {
                    try
                    {
                        if (!current.Process.HasExited)
                            return current;
                    }
                    catch { }

                    StopSniRuntime(current, true);
                }

                var config = ParseSniLink(sniLink);
                var runtime = StartSniRuntime(config, persistent);
                Thread.Sleep(1000);
                runtimes[sniLink] = runtime;
                return runtime;
            }
        }

        private SniRuntime StartSniRuntime(SniConfig config, bool persistent)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var sniPath = ResolveSniCorePath();
            if (string.IsNullOrEmpty(sniPath) || !File.Exists(sniPath))
                throw new FileNotFoundException("SNI executable not found in temp folder.", sniPath ?? "");

            var listenPort = AllocateSniListenPort();            

            var configPath = Path.Combine(Path.GetDirectoryName(sniPath), "config.json");
            var serializer = new JavaScriptSerializer();
            var configJson = serializer.Serialize(new Dictionary<string, object>
            {
                { "LISTEN_HOST", "127.0.0.1" },
                { "LISTEN_PORT", listenPort },
                { "CONNECT_IP", config.ConnectIp },
                { "CONNECT_PORT", config.ConnectPort },
                { "FAKE_SNI", config.FakeSni },
                { "QUEUE_NUM", config.QueueNum },
                { "HANDSHAKE_TIMEOUT_MS", config.HandshakeTimeoutMs },
            });
            File.WriteAllText(configPath, configJson, Encoding.ASCII);

            var process = ShellExecute.ShellexecAndReturnProcess(sniPath, configPath);
            if (!WaitForSniPort("127.0.0.1", listenPort, TimeSpan.FromSeconds(10)))
            {
                TryKillProcess(process);
                ReleaseSniListenPort(listenPort);
                throw new TimeoutException($"sni.exe did not start listening on port {listenPort} within 10 seconds.");
            }
            return new SniRuntime
            {
                RawLink = config.RawLink,
                ListenHost = "127.0.0.1",
                ListenPort = listenPort,
                WorkingDirectory = Path.GetDirectoryName(sniPath),
                ConfigPath = configPath,
                Process = process,
                Persistent = persistent,
            };
        }

        private void StopSniServers(Dictionary<string, SniRuntime> runtimes)
        {
            if (runtimes == null)
                return;

            lock (sniLock)
            {
                foreach (var runtime in runtimes.Values.ToList())
                {
                    StopSniRuntime(runtime, true);
                }
                runtimes.Clear();
            }
        }

        private void StopSniRuntime(SniRuntime runtime, bool releasePort)
        {
            if (runtime == null)
                return;

            TryKillProcess(runtime.Process);
            if (releasePort && runtime.ListenPort > 0)
                ReleaseSniListenPort(runtime.ListenPort);
            ShellExecute.KillProccess("sni");
        }

        private int AllocateSniListenPort()
        {
            lock (sniLock)
            {
                var candidate = Math.Max(nextSniListenPort, 50443);
                while (candidate < 65535)
                {
                    if (!activeSniPorts.Contains(candidate) && IsPortAvailable(candidate))
                    {
                        activeSniPorts.Add(candidate);
                        nextSniListenPort = candidate + 1;
                        return candidate;
                    }
                    candidate++;
                }
            }

            throw new InvalidOperationException("No free SNI listen port found.");
        }

        private void ReleaseSniListenPort(int port)
        {
            lock (sniLock)
            {
                activeSniPorts.Remove(port);
                if (port < nextSniListenPort)
                    nextSniListenPort = port;
            }
        }

        private bool IsPortAvailable(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    listener?.Stop();
                }
                catch { }
            }
        }

        private bool WaitForSniPort(string host, int port, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(host, port);
                        if (connectTask.Wait(200) && client.Connected)
                            return true;
                    }
                }
                catch
                {
                }

                Thread.Sleep(100);
            }

            return false;
        }

        private SniConfig ParseSniLink(string sniLink)
        {
            Uri uri;
            try
            {
                uri = new Uri(sniLink);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid SNI link: {sniLink}", ex);
            }

            if (!string.Equals(uri.Scheme, "sni", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Invalid SNI scheme: {sniLink}");

            var query = HttpUtility.ParseQueryString((uri.Query ?? "").Replace("&amp;", "&"));
            var connectIp = query["ip"];
            if (string.IsNullOrWhiteSpace(connectIp))
                throw new InvalidOperationException($"SNI link is missing ip: {sniLink}");

            return new SniConfig
            {
                RawLink = sniLink,
                FakeSni = uri.Host,
                ConnectIp = connectIp,
                ConnectPort = ParseIntOrDefault(query["port"], 443),
                QueueNum = ParseIntOrDefault(query["qnum"], 100),
                HandshakeTimeoutMs = ParseIntOrDefault(query["timeout"], 2000),
            };
        }

        private int ParseIntOrDefault(string value, int defaultValue)
        {
            if (int.TryParse(value, out var parsed) && parsed > 0)
                return parsed;
            return defaultValue;
        }

        private void CoreProcess_Exited(object sender, EventArgs e)
        {
            if (suppressCoreExit)
                return;
            if (!IsConnected)
            {
                try
                {
                    var exitedProcess = sender as Process;
                    if (exitedProcess != null)
                        LogHelper.WriteLog($"Core exited while idle or URL testing. Exit code: {exitedProcess.ExitCode}.");
                }
                catch { }
                return;
            }
            if (userCancelRequested)
            {
                DisconnectInternal(true, false, false);
                return;
            }
            if (!ShouldRetryCoreExit())
            {
                DisconnectInternal(true, false, false);
                return;
            }
            TryReconnect();
        }
        private bool ShouldRetryCoreExit()
        {
            var now = DateTime.UtcNow;
            if ((now - lastCoreStartUtc) <= TimeSpan.FromSeconds(ImmediateExitSeconds))
                return false;
            lock (reconnectLock)
            {
                while (recentCoreExitsUtc.Count > 0 &&
                       (now - recentCoreExitsUtc.Peek()) > TimeSpan.FromSeconds(CoreExitWindowSeconds))
                {
                    recentCoreExitsUtc.Dequeue();
                }
                recentCoreExitsUtc.Enqueue(now);
                if (recentCoreExitsUtc.Count >= MaxCoreExitsInWindow)
                    return false;
            }
            return true;
        }
        private void TryReconnect()
        {
            if (Interlocked.CompareExchange(ref reconnecting, 1, 0) != 0)
                return;
            Task.Run(() =>
            {
                try
                {
                    DisconnectInternal(true, true, false);
                    Thread.Sleep(1000);
                    if (userCancelRequested)
                        return;
                    var link = lastLink ?? SelectedUrl;
                    RunV2ray(link, lastListenPort);
                }
                finally
                {
                    Interlocked.Exchange(ref reconnecting, 0);
                }
            });
        }

        private string ResolveCorePath()
        {

            var filepath = Path.Combine(gInfo.TempPath, "V-Guard", (Tools.IsWin7OrLower() ? "SGuard7" : "SGuard") + (Environment.Is64BitOperatingSystem ? "64.exe" : "32.exe"));
            if (!File.Exists(filepath))
            {
                return null;
            }
            return filepath;
        }


        private string ResolveSniCorePath()
        {
            if (!string.IsNullOrWhiteSpace(sniCorePath) && File.Exists(sniCorePath))
                return sniCorePath;

            var directPath = Path.Combine(gInfo.TempPath, "sni.exe");
            if (File.Exists(directPath))
                return sniCorePath = directPath;

            try
            {
                sniCorePath = Directory.GetFiles(gInfo.TempPath, "sni.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                sniCorePath = null;
            }

            return sniCorePath;
        }

        class SniConfig
        {
            public string RawLink { get; set; }
            public string FakeSni { get; set; }
            public string ConnectIp { get; set; }
            public int ConnectPort { get; set; }
            public int QueueNum { get; set; }
            public int HandshakeTimeoutMs { get; set; }
        }

        class SniRuntime
        {
            public string RawLink { get; set; }
            public string ListenHost { get; set; }
            public int ListenPort { get; set; }
            public string WorkingDirectory { get; set; }
            public string ConfigPath { get; set; }
            public Process Process { get; set; }
            public bool Persistent { get; set; }
        }


        private bool WaitForPort(string host, int port, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(host, port);
                        if (connectTask.Wait(200) && client.Connected)
                            return true;
                    }
                }
                catch { }
                Thread.Sleep(100);
            }
            return false;
        }

        public List<Url> GetServerUrls()
        {
            return server.urls;
        }
    }
}
