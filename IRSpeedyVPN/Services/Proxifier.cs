using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    internal class Proxifier:IProxifier
    {
        string profileNoraml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<ProxifierProfile version=\"102\" platform=\"Windows\" product_id=\"1\" product_minver=\"400\">\r\n\t<Options>\r\n\t\t<Resolve>\r\n\t\t\t<AutoModeDetection enabled=\"false\" />\r\n\t\t\t<ViaProxy enabled=\"true\" />\r\n\t\t\t<ExclusionList OnlyFromListMode=\"false\">%ComputerName%; localhost; *.local</ExclusionList>\r\n\t\t\t<DnsUdpMode>0</DnsUdpMode>\r\n\t\t</Resolve>\r\n\t\t<Encryption mode=\"disabled\" />\r\n\t\t<ConnectionLoopDetection enabled=\"true\" resolve=\"true\" />\r\n\t\t<ProcessOtherUsers enabled=\"false\" />\r\n\t\t<ProcessServices enabled=\"false\" />\r\n\t\t<HandleDirectConnections enabled=\"false\" />\r\n\t\t<HttpProxiesSupport enabled=\"false\" />\r\n\t\t<ProxificationPortableEngine subsystem=\"32\">\r\n\t\t\t<Type hotpatch=\"true\">Prologue</Type>\r\n\t\t\t<Location>BaseProvider</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t\t<ProxificationPortableEngine subsystem=\"64\">\r\n\t\t\t<Type hotpatch=\"false\">Modcopy</Type>\r\n\t\t\t<Location>Winsock</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t</Options>\r\n\t<ProxyList>\r\n\t\t<Proxy id=\"100\" type=\"{0}\">\r\n\t\t\t<Authentication enabled=\"{3}\">\r\n\t\t\t\t<Password>{5}</Password>\r\n\t\t\t\t<Username>{4}</Username>\r\n\t\t\t</Authentication>\r\n\t\t\t<Options>0</Options>\r\n\t\t\t<Port>{2}</Port>\r\n\t\t\t<Address>{1}</Address>\r\n\t\t</Proxy>\r\n\t</ProxyList>\r\n\t<ChainList />\r\n\t<RuleList>\r\n <Rule enabled=\"true\">\r\n<Name>Localhost</Name>\r\n<Targets>localhost;127.0.0.1-127.255.255.255;%ComputerName%;::1;192.168.1.1-192.168.255.255</Targets>\r\n<Action type=\"Direct\" />\r\n</Rule>\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>zebedee.exe; tstunnel.exe; overproxy-fte.exe; overproxy-obfs.exe; powervpn.exe; vpnplus.exe;</Applications>\r\n\t\t\t<Name>Direct access</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>\"IRSpeedyVPN.exe\"</Applications>\r\n\t\t\t<Name>IRSpeedyVPN</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Name>Default</Name>\r\n\t\t</Rule>\r\n\t</RuleList>\r\n</ProxifierProfile>\r\n";
        string profileTelegram = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<ProxifierProfile version=\"102\" platform=\"Windows\" product_id=\"1\" product_minver=\"400\">\r\n\t<Options>\r\n\t\t<Resolve>\r\n\t\t\t<AutoModeDetection enabled=\"false\" />\r\n\t\t\t<ViaProxy enabled=\"true\" />\r\n\t\t\t<ExclusionList OnlyFromListMode=\"false\">%ComputerName%; localhost; *.local</ExclusionList>\r\n\t\t\t<DnsUdpMode>0</DnsUdpMode>\r\n\t\t</Resolve>\r\n\t\t<Encryption mode=\"disabled\" />\r\n\t\t<ConnectionLoopDetection enabled=\"true\" resolve=\"true\" />\r\n\t\t<ProcessOtherUsers enabled=\"false\" />\r\n\t\t<ProcessServices enabled=\"false\" />\r\n\t\t<HandleDirectConnections enabled=\"false\" />\r\n\t\t<HttpProxiesSupport enabled=\"false\" />\r\n\t\t<ProxificationPortableEngine subsystem=\"32\">\r\n\t\t\t<Type hotpatch=\"true\">Prologue</Type>\r\n\t\t\t<Location>BaseProvider</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t\t<ProxificationPortableEngine subsystem=\"64\">\r\n\t\t\t<Type hotpatch=\"false\">Modcopy</Type>\r\n\t\t\t<Location>Winsock</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t</Options>\r\n\t<ProxyList>\r\n\t\t<Proxy id=\"100\" type=\"{0}\">\r\n\t\t\t<Authentication enabled=\"{3}\">\r\n\t\t\t\t<Password>{5}</Password>\r\n\t\t\t\t<Username>{4}</Username>\r\n\t\t\t</Authentication>\r\n\t\t\t<Options>0</Options>\r\n\t\t\t<Port>{2}</Port>\r\n\t\t\t<Address>{1}</Address>\r\n\t\t</Proxy>\r\n\t</ProxyList>\r\n\t<ChainList />\r\n\t<RuleList>\r\n <Rule enabled=\"true\">\r\n<Name>Localhost</Name>\r\n<Targets>localhost;127.0.0.1-127.255.255.255;%ComputerName%;::1;192.168.1.1-192.168.255.255</Targets>\r\n<Action type=\"Direct\" />\r\n</Rule>\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>zebedee.exe; tstunnel.exe; overproxy-fte.exe; overproxy-obfs.exe; powervpn.exe; vpnplus.exe;</Applications>\r\n\t\t\t<Name>Direct access</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>Telegram.exe;WhatsApp.exe</Applications>\r\n\t\t\t<Name>New</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>\"IRSpeedyVPN.exe\"</Applications>\r\n\t\t\t<Name>IRSpeedyVPN</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Name>Default</Name>\r\n\t\t</Rule>\r\n\t</RuleList>\r\n</ProxifierProfile>\r\n";
        private readonly object lifecycleGate = new object();
        private long generation;
        private volatile bool isAttached;
        private ProxifierType routeType;
        private Process pr;
        private ProxifierBrowserQuic browserQuic;
        public GlobalInfo globalInfo => AppServices.GlobalInfo;
        public ProxifierType ProxyType => routeType;
        public event OnResult onResult;

        public void Attach(string ip, int port, string username, string password,
            ProxyType proxyType, ProxifierType route)
        {
            long version;
            var context = SynchronizationContext.Current;
            lock (lifecycleGate) version = ++generation;
            ThreadPool.QueueUserWorkItem(_ => RunProxifier(ip, port, username, password, proxyType, route, version, context));
        }

        private void RunProxifier(string ip, int port, string username, string password,
            ProxyType proxyType, ProxifierType route, long version, SynchronizationContext context)
        {
            string error = null;
            lock (lifecycleGate)
            {
                // Detach invalidates queued starts, so a cancelled connection cannot add filters later.
                if (version != generation) return;
                StopLocked();
                routeType = route;
                try
                {
                    string profile = route == ProxifierType.Telegram ? profileTelegram : profileNoraml;
                    string current = string.Format(profile, proxyType == Common.ProxyType.SOCKS ? "SOCKS5" : "HTTPS",
                        ip, port, (!string.IsNullOrEmpty(username)).ToString().ToLowerInvariant(), username, password);
                    File.WriteAllText(Path.Combine(globalInfo.TempPath, "Proxifier/Profiles/Default.ppx"), current);

                    if (ProxifierBrowserQuic.AppliesTo(route))
                    {
                        browserQuic = new ProxifierBrowserQuic();
                        try { browserQuic.Start(); }
                        catch (Exception ex)
                        {
                            // BFE/service/permission failures must not prevent ordinary TCP proxying.
                            browserQuic.Dispose();
                            browserQuic = null;
                            LogHelper.WriteLog("[Proxifier QUIC] Temporary browser filter unavailable: " + ex.Message);
                        }
                    }

                    pr = ShellExecute.ShellexecAndReturnProcess(Path.Combine(globalInfo.TempPath, "Proxifier/Proxifier.exe"), "");
                    var launched = pr;
                    launched.Exited += (sender, args) => ProxifierExited(launched, version, context);
                    launched.EnableRaisingEvents = true;
                    if (launched.HasExited) throw new InvalidOperationException("Proxifier exited during startup.");
                    isAttached = true;
                }
                catch (Exception ex)
                {
                    StopLocked();
                    error = ex.Message;
                }
            }
            Notify(version, error == null, error, context);
        }

        private void ProxifierExited(Process process, long version, SynchronizationContext context)
        {
            lock (lifecycleGate)
            {
                if (version != generation || !ReferenceEquals(pr, process)) return;
                StopLocked();
            }
            Notify(version, false, "Proxifier stopped unexpectedly.", context);
        }

        private void Notify(long version, bool connected, string message, SynchronizationContext context)
        {
            SendOrPostCallback callback = _ =>
            {
                lock (lifecycleGate)
                    if (version != generation || connected != isAttached) return;
                // Never invoke a WPF callback under lifecycleGate: Detach may be on the UI thread.
                onResult?.Invoke(connected, message);
            };
            if (context != null) context.Post(callback, null);
            else callback(null);
        }

        private void StopLocked()
        {
            isAttached = false;
            var process = pr;
            pr = null;
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch { }
            finally
            {
                process?.Dispose();
                browserQuic?.Dispose();
                browserQuic = null;
            }
        }

        public void Detach()
        {
            lock (lifecycleGate)
            {
                ++generation;
                StopLocked();
                ShellExecute.KillProccess("proxifier");
            }
        }

        public bool IsAttached() => isAttached;
    }
}
