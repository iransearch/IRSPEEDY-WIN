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

namespace IRSpeedyVPN.Services
{
    internal class Proxifier:IProxifier
    {
        string profileNoraml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<ProxifierProfile version=\"102\" platform=\"Windows\" product_id=\"1\" product_minver=\"400\">\r\n\t<Options>\r\n\t\t<Resolve>\r\n\t\t\t<AutoModeDetection enabled=\"false\" />\r\n\t\t\t<ViaProxy enabled=\"true\" />\r\n\t\t\t<ExclusionList OnlyFromListMode=\"false\">%ComputerName%; localhost; *.local</ExclusionList>\r\n\t\t\t<DnsUdpMode>0</DnsUdpMode>\r\n\t\t</Resolve>\r\n\t\t<Encryption mode=\"disabled\" />\r\n\t\t<ConnectionLoopDetection enabled=\"true\" resolve=\"true\" />\r\n\t\t<ProcessOtherUsers enabled=\"false\" />\r\n\t\t<ProcessServices enabled=\"false\" />\r\n\t\t<HandleDirectConnections enabled=\"false\" />\r\n\t\t<HttpProxiesSupport enabled=\"false\" />\r\n\t\t<ProxificationPortableEngine subsystem=\"32\">\r\n\t\t\t<Type hotpatch=\"true\">Prologue</Type>\r\n\t\t\t<Location>BaseProvider</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t\t<ProxificationPortableEngine subsystem=\"64\">\r\n\t\t\t<Type hotpatch=\"false\">Modcopy</Type>\r\n\t\t\t<Location>Winsock</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t</Options>\r\n\t<ProxyList>\r\n\t\t<Proxy id=\"100\" type=\"{0}\">\r\n\t\t\t<Authentication enabled=\"{3}\">\r\n\t\t\t\t<Password>{5}</Password>\r\n\t\t\t\t<Username>{4}</Username>\r\n\t\t\t</Authentication>\r\n\t\t\t<Options>0</Options>\r\n\t\t\t<Port>{2}</Port>\r\n\t\t\t<Address>{1}</Address>\r\n\t\t</Proxy>\r\n\t</ProxyList>\r\n\t<ChainList />\r\n\t<RuleList>\r\n <Rule enabled=\"true\">\r\n<Name>Localhost</Name>\r\n<Targets>localhost;127.0.0.1-127.255.255.255;%ComputerName%;::1;192.168.1.1-192.168.255.255</Targets>\r\n<Action type=\"Direct\" />\r\n</Rule>\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>zebedee.exe; tstunnel.exe; overproxy-fte.exe; overproxy-obfs.exe; powervpn.exe; vpnplus.exe;</Applications>\r\n\t\t\t<Name>Direct access</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>\"IRSpeedyVPN.exe\"</Applications>\r\n\t\t\t<Name>IRSpeedyVPN</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Name>Default</Name>\r\n\t\t</Rule>\r\n\t</RuleList>\r\n</ProxifierProfile>\r\n";
        string profileSmart = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<ProxifierProfile version=\"102\" platform=\"Windows\" product_id=\"1\" product_minver=\"400\">\r\n\t<Options>\r\n\t\t<Resolve>\r\n\t\t\t<AutoModeDetection enabled=\"false\" />\r\n\t\t\t<ViaProxy enabled=\"true\" />\r\n\t\t\t<ExclusionList OnlyFromListMode=\"false\">%ComputerName%; localhost; *.local</ExclusionList>\r\n\t\t\t<DnsUdpMode>0</DnsUdpMode>\r\n\t\t</Resolve>\r\n\t\t<Encryption mode=\"disabled\" />\r\n\t\t<ConnectionLoopDetection enabled=\"true\" resolve=\"true\" />\r\n\t\t<ProcessOtherUsers enabled=\"false\" />\r\n\t\t<ProcessServices enabled=\"false\" />\r\n\t\t<HandleDirectConnections enabled=\"false\" />\r\n\t\t<HttpProxiesSupport enabled=\"false\" />\r\n\t\t<ProxificationPortableEngine subsystem=\"32\">\r\n\t\t\t<Type hotpatch=\"true\">Prologue</Type>\r\n\t\t\t<Location>BaseProvider</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t\t<ProxificationPortableEngine subsystem=\"64\">\r\n\t\t\t<Type hotpatch=\"false\">Modcopy</Type>\r\n\t\t\t<Location>Winsock</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t</Options>\r\n\t<ProxyList>\r\n\t\t<Proxy id=\"100\" type=\"{0}\">\r\n\t\t\t<Authentication enabled=\"{3}\">\r\n\t\t\t\t<Password>{5}</Password>\r\n\t\t\t\t<Username>{4}</Username>\r\n\t\t\t</Authentication>\r\n\t\t\t<Options>0</Options>\r\n\t\t\t<Port>{2}</Port>\r\n\t\t\t<Address>{1}</Address>\r\n\t\t</Proxy>\r\n\t</ProxyList>\r\n\t<ChainList />\r\n\t<RuleList>\r\n <Rule enabled=\"true\">\r\n<Name>Localhost</Name>\r\n<Targets>localhost;127.0.0.1-127.255.255.255;%ComputerName%;::1;192.168.1.1-192.168.255.255</Targets>\r\n<Action type=\"Direct\" />\r\n</Rule>\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>zebedee.exe; tstunnel.exe; overproxy-fte.exe; overproxy-obfs.exe; powervpn.exe; vpnplus.exe;</Applications>\r\n\t\t\t<Name>Direct access</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>\"IRSpeedyVPN.exe\"</Applications>\r\n\t\t\t<Name>IRSpeedyVPN</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Block\" />\r\n\t\t\t<Targets>ads.*.com\r\n*.sabavision.com\r\n*.mediaad.org\r\n*.raykaad.com\r\n*.g-ads.org\r\n*.tapsell.ir\r\n*.clickyab.com\r\n*.dgad.ir\r\n*.kaprila.com\r\n*.internet.ir\r\n*.cyberpolice.ir\r\n*.gerdab.ir</Targets>\r\n\t\t\t<Name>ads</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Targets>*.ir\r\n*.sanjesh.org\r\n*.co.ir\r\n*.iau.ir\r\n*.cafebazaar.org\r\n*.sheypoor.com\r\n*.divarcdn.com\r\n</Targets>\r\n\t\t\t<Name>ir</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Targets>*.aparat.com; *.filimo.com; *.snapp.ir; *.digikala.com</Targets>\r\n\t\t\t<Name>vod-iran</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Targets>*.shaparak.ir</Targets>\r\n\t\t\t<Name>bank</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Targets>5.78.0.0-5.78.255.255\r\n5.160.0.0-5.160.255.255\r\n31.14.112.0-31.14.127.255\r\n37.128.240.0-37.128.255.255\r\n37.221.0.0-37.221.63.255\r\n46.41.192.0-46.41.255.255\r\n81.28.32.0-81.28.47.255</Targets>\r\n\t\t\t<Name>IRAN IP</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Targets>*.p30world.com\r\n*.upera.shop\r\n*.doostihaa.com</Targets>\r\n\t\t\t<Name>software</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Ports>2087</Ports>\r\n\t\t\t<Name>New</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Ports>2083</Ports>\r\n\t\t\t<Name>New</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>MediaMonkey.exe</Applications>\r\n\t\t\t<Name>New</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Name>Default</Name>\r\n\t\t</Rule>\r\n\t</RuleList>\r\n</ProxifierProfile>\r\n";
        string profileTelegram = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<ProxifierProfile version=\"102\" platform=\"Windows\" product_id=\"1\" product_minver=\"400\">\r\n\t<Options>\r\n\t\t<Resolve>\r\n\t\t\t<AutoModeDetection enabled=\"false\" />\r\n\t\t\t<ViaProxy enabled=\"true\" />\r\n\t\t\t<ExclusionList OnlyFromListMode=\"false\">%ComputerName%; localhost; *.local</ExclusionList>\r\n\t\t\t<DnsUdpMode>0</DnsUdpMode>\r\n\t\t</Resolve>\r\n\t\t<Encryption mode=\"disabled\" />\r\n\t\t<ConnectionLoopDetection enabled=\"true\" resolve=\"true\" />\r\n\t\t<ProcessOtherUsers enabled=\"false\" />\r\n\t\t<ProcessServices enabled=\"false\" />\r\n\t\t<HandleDirectConnections enabled=\"false\" />\r\n\t\t<HttpProxiesSupport enabled=\"false\" />\r\n\t\t<ProxificationPortableEngine subsystem=\"32\">\r\n\t\t\t<Type hotpatch=\"true\">Prologue</Type>\r\n\t\t\t<Location>BaseProvider</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t\t<ProxificationPortableEngine subsystem=\"64\">\r\n\t\t\t<Type hotpatch=\"false\">Modcopy</Type>\r\n\t\t\t<Location>Winsock</Location>\r\n\t\t</ProxificationPortableEngine>\r\n\t</Options>\r\n\t<ProxyList>\r\n\t\t<Proxy id=\"100\" type=\"{0}\">\r\n\t\t\t<Authentication enabled=\"{3}\">\r\n\t\t\t\t<Password>{5}</Password>\r\n\t\t\t\t<Username>{4}</Username>\r\n\t\t\t</Authentication>\r\n\t\t\t<Options>0</Options>\r\n\t\t\t<Port>{2}</Port>\r\n\t\t\t<Address>{1}</Address>\r\n\t\t</Proxy>\r\n\t</ProxyList>\r\n\t<ChainList />\r\n\t<RuleList>\r\n <Rule enabled=\"true\">\r\n<Name>Localhost</Name>\r\n<Targets>localhost;127.0.0.1-127.255.255.255;%ComputerName%;::1;192.168.1.1-192.168.255.255</Targets>\r\n<Action type=\"Direct\" />\r\n</Rule>\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Applications>zebedee.exe; tstunnel.exe; overproxy-fte.exe; overproxy-obfs.exe; powervpn.exe; vpnplus.exe;</Applications>\r\n\t\t\t<Name>Direct access</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>Telegram.exe;WhatsApp.exe</Applications>\r\n\t\t\t<Name>New</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Proxy\">100</Action>\r\n\t\t\t<Applications>\"IRSpeedyVPN.exe\"</Applications>\r\n\t\t\t<Name>IRSpeedyVPN</Name>\r\n\t\t</Rule>\r\n\t\t<Rule enabled=\"true\">\r\n\t\t\t<Action type=\"Direct\" />\r\n\t\t\t<Name>Default</Name>\r\n\t\t</Rule>\r\n\t</RuleList>\r\n</ProxifierProfile>\r\n";
        string proxifierPath = "Proxifier";
        bool isAttached;
        ProxifierType routeType;
        public GlobalInfo globalInfo => AppServices.GlobalInfo;

        public ProxifierType ProxyType => routeType;

        public event OnResult onResult;
        Process pr;
        
        public void Attach(string ip,int port,string username,string password,ProxyType proxyType, ProxifierType RouteType)
        {
                        
            ((Action)(() => RunProxifier(ip,port,username,password, proxyType, RouteType))).BeginInvoke(null, null);

        }

        private void RunProxifier(string ip, int port, string username, string password,ProxyType proxyType, ProxifierType RouteType)
        {   try
            {
                isAttached = false;
                routeType = RouteType;
                string profile=null;
                switch (RouteType)
                {
                  //  case ProxifierType.Smart:
                    //    profile = profileSmart;
                      //  break;
                    case ProxifierType.Telegram:
                        profile = profileTelegram;
                        break;
                    case var _ :
                        profile = profileNoraml;
                        break;
                }

                var currProfile = string.Format(profile, proxyType==Common.ProxyType.SOCKS? "SOCKS5":"HTTPS", ip, port, (!string.IsNullOrEmpty(username)).ToString().ToLower(), username, password);
                File.WriteAllText(Path.Combine(globalInfo.TempPath, "Proxifier/Profiles/Default.ppx"), currProfile);
                pr = ShellExecute.ShellexecAndReturnProcess(Path.Combine(globalInfo.TempPath, "Proxifier/Proxifier.exe"), "");
                if (onResult != null)
                    onResult.Invoke(true, null);
                isAttached = true;
            }
            catch(Exception ex)
            {
                onResult.Invoke(false, ex.Message);
            }
        }
        public void Detach()
        {
            try
            {
                isAttached = false;
                if (pr != null)
                    pr.Kill();
            }
            catch{}
            ShellExecute.KillProccess("proxifier");
         
        }

        public bool IsAttached()
        {
            return isAttached;
        }
    }
}
