using DotRas;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Exceptions;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    class VPNService : IVPNService
    {
        
        RasConnectionWatcher watcher;
        GlobalInfo gInfo;
        public int ID => server.ID;
        private string name;
        public string Name
        {
            get => name ?? "VPN";
            set => name = value;
        }

        public bool IsUsingProxifire => false;

        public string[] Protocols => server.Protocol.Split(' ');

        public string Country => server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");
        string selectedProtocol = null;
        public string SelectedProtocol => selectedProtocol;

        public byte Order => 2;

        public Type SettingType => null;
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;

        public ProxifierType ProxifierRuleType => throw new NotImplementedException();

        public byte CountryIndex { get; set; }

        public string CountryCode => server.Country;

        public bool ProxifierWithPassword => throw new NotImplementedException();

        public ProxyType ProxyType => throw new NotImplementedException();

        public bool IsUrlTestSupported => false;

        public long UrlTestSpeed => throw new NotImplementedException();

        public Url SelectedServerUrl { get; set; }

        public void ApplyShareSetting()
        {
        }

        Timer timerDisconnect;
        string[] ProtocolsData;
        string PPTP = "[PPTP]\r\nEncoding=1\r\nPBVersion=5\r\nType=2\r\nAutoLogon=0\r\nUseRasCredentials=1\r\nLowDateTime=1692543744\r\nHighDateTime=30800386\r\nDialParamsUID=16485953\r\nGuid=550B3E1CB2E3684E9DBBC57C4BCE6B22\r\nVpnStrategy=1\r\nExcludedProtocols=8\r\nLcpExtensions=1\r\nDataEncryption=8\r\nSwCompression=0\r\nNegotiateMultilinkAlways=0\r\nSkipDoubleDialDialog=0\r\nDialMode=0\r\nOverridePref=15\r\nRedialAttempts=3\r\nRedialSeconds=60\r\nIdleDisconnectSeconds=0\r\nRedialOnLinkFailure=1\r\nCallbackMode=0\r\nCustomDialDll=\r\nCustomDialFunc=\r\nCustomRasDialDll=\r\nForceSecureCompartment=0\r\nDisableIKENameEkuCheck=0\r\nAuthenticateServer=0\r\nShareMsFilePrint=0\r\nBindMsNetClient=0\r\nSharedPhoneNumbers=0\r\nGlobalDeviceSettings=0\r\nPrerequisiteEntry=\r\nPrerequisitePbk=\r\nPreferredPort=VPN4-0\r\nPreferredDevice=WAN Miniport (PPTP)\r\nPreferredBps=0\r\nPreferredHwFlow=1\r\nPreferredProtocol=1\r\nPreferredCompression=1\r\nPreferredSpeaker=1\r\nPreferredMdmProtocol=0\r\nPreviewUserPw=1\r\nPreviewDomain=1\r\nPreviewPhoneNumber=0\r\nShowDialingProgress=1\r\nShowMonitorIconInTaskBar=1\r\nCustomAuthKey=0\r\nAuthRestrictions=640\r\nIpPrioritizeRemote=1\r\nIpInterfaceMetric=0\r\nIpHeaderCompression=0\r\nIpAddress=0.0.0.0\r\nIpDnsAddress=0.0.0.0\r\nIpDns2Address=0.0.0.0\r\nIpWinsAddress=0.0.0.0\r\nIpWins2Address=0.0.0.0\r\nIpAssign=1\r\nIpNameAssign=1\r\nIpDnsFlags=0\r\nIpNBTFlags=1\r\nTcpWindowSize=0\r\nUseFlags=2\r\nIpSecFlags=0\r\nIpDnsSuffix=\r\nIpv6Assign=1\r\nIpv6Address=::\r\nIpv6PrefixLength=0\r\nIpv6PrioritizeRemote=1\r\nIpv6InterfaceMetric=0\r\nIpv6NameAssign=1\r\nIpv6DnsAddress=::\r\nIpv6Dns2Address=::\r\nIpv6Prefix=0000000000000000\r\nIpv6InterfaceId=0000000000000000\r\nDisableClassBasedDefaultRoute=0\r\nDisableMobility=0\r\nNetworkOutageTime=0\r\nIDI=\r\nIDR=\r\nImsConfig=0\r\nIdiType=0\r\nIdrType=0\r\nProvisionType=0\r\nPreSharedKey=\r\nCacheCredentials=1\r\nNumCustomPolicy=0\r\nNumEku=0\r\nUseMachineRootCert=0\r\nDisable_IKEv2_Fragmentation=0\r\nNumServers=0\r\nRouteVersion=1\r\nNumRoutes=0\r\nNumNrptRules=0\r\nAutoTiggerCapable=0\r\nNumAppIds=0\r\nNumClassicAppIds=0\r\nSecurityDescriptor=\r\nApnInfoProviderId=\r\nApnInfoUsername=\r\nApnInfoPassword=\r\nApnInfoAccessPoint=\r\nApnInfoAuthentication=1\r\nApnInfoCompression=0\r\nDeviceComplianceEnabled=0\r\nDeviceComplianceSsoEnabled=0\r\nDeviceComplianceSsoEku=\r\nDeviceComplianceSsoIssuer=\r\nFlagsSet=0\r\nOptions=0\r\nDisableDefaultDnsSuffixes=0\r\nNumTrustedNetworks=0\r\nNumDnsSearchSuffixes=0\r\nPowershellCreatedProfile=0\r\nProxyFlags=0\r\nProxySettingsModified=0\r\nProvisioningAuthority=\r\nAuthTypeOTP=0\r\nGREKeyDefined=0\r\nNumPerAppTrafficFilters=0\r\nAlwaysOnCapable=0\r\nDeviceTunnel=0\r\nPrivateNetwork=0\r\n\r\nNETCOMPONENTS=\r\nms_msclient=0\r\nms_server=0\r\n\r\nMEDIA=rastapi\r\nPort=VPN4-0\r\nDevice=WAN Miniport (PPTP)\r\n\r\nDEVICE=vpn\r\nPhoneNumber={0}\r\nAreaCode=\r\nCountryCode=0\r\nCountryID=0\r\nUseDialingRules=0\r\nComment=\r\nFriendlyName=\r\nLastSelectedPhone=0\r\nPromoteAlternates=0\r\nTryNextAlternateOnFail=1\r\n\r\n";
        string L2TP = "[L2TP]\r\nEncoding=1\r\nPBVersion=5\r\nType=2\r\nAutoLogon=0\r\nUseRasCredentials=1\r\nLowDateTime=190835280\r\nHighDateTime=30808445\r\nDialParamsUID=16485953\r\nGuid=550B3E1CB2E3684E9DBBC57C4BCE6B22\r\nVpnStrategy=3\r\nExcludedProtocols=8\r\nLcpExtensions=1\r\nDataEncryption=8\r\nSwCompression=0\r\nNegotiateMultilinkAlways=0\r\nSkipDoubleDialDialog=0\r\nDialMode=0\r\nOverridePref=15\r\nRedialAttempts=3\r\nRedialSeconds=60\r\nIdleDisconnectSeconds=0\r\nRedialOnLinkFailure=1\r\nCallbackMode=0\r\nCustomDialDll=\r\nCustomDialFunc=\r\nCustomRasDialDll=\r\nForceSecureCompartment=0\r\nDisableIKENameEkuCheck=0\r\nAuthenticateServer=0\r\nShareMsFilePrint=0\r\nBindMsNetClient=0\r\nSharedPhoneNumbers=0\r\nGlobalDeviceSettings=0\r\nPrerequisiteEntry=\r\nPrerequisitePbk=\r\nPreferredPort=VPN4-0\r\nPreferredDevice=WAN Miniport (L2TP)\r\nPreferredBps=0\r\nPreferredHwFlow=1\r\nPreferredProtocol=1\r\nPreferredCompression=1\r\nPreferredSpeaker=1\r\nPreferredMdmProtocol=0\r\nPreviewUserPw=1\r\nPreviewDomain=1\r\nPreviewPhoneNumber=0\r\nShowDialingProgress=1\r\nShowMonitorIconInTaskBar=1\r\nCustomAuthKey=0\r\nAuthRestrictions=512\r\nIpPrioritizeRemote=1\r\nIpInterfaceMetric=0\r\nIpHeaderCompression=0\r\nIpAddress=0.0.0.0\r\nIpDnsAddress=0.0.0.0\r\nIpDns2Address=0.0.0.0\r\nIpWinsAddress=0.0.0.0\r\nIpWins2Address=0.0.0.0\r\nIpAssign=1\r\nIpNameAssign=1\r\nIpDnsFlags=0\r\nIpNBTFlags=1\r\nTcpWindowSize=0\r\nUseFlags=2\r\nIpSecFlags=1\r\nIpDnsSuffix=\r\nIpv6Assign=1\r\nIpv6Address=::\r\nIpv6PrefixLength=0\r\nIpv6PrioritizeRemote=1\r\nIpv6InterfaceMetric=0\r\nIpv6NameAssign=1\r\nIpv6DnsAddress=::\r\nIpv6Dns2Address=::\r\nIpv6Prefix=0000000000000000\r\nIpv6InterfaceId=0000000000000000\r\nDisableClassBasedDefaultRoute=0\r\nDisableMobility=0\r\nNetworkOutageTime=0\r\nIDI=\r\nIDR=\r\nImsConfig=0\r\nIdiType=0\r\nIdrType=0\r\nProvisionType=0\r\nPreSharedKey=123456789\r\nCacheCredentials=1\r\nNumCustomPolicy=0\r\nNumEku=0\r\nUseMachineRootCert=0\r\nDisable_IKEv2_Fragmentation=0\r\nNumServers=0\r\nRouteVersion=1\r\nNumRoutes=0\r\nNumNrptRules=0\r\nAutoTiggerCapable=0\r\nNumAppIds=0\r\nNumClassicAppIds=0\r\nSecurityDescriptor=\r\nApnInfoProviderId=\r\nApnInfoUsername=\r\nApnInfoPassword=\r\nApnInfoAccessPoint=\r\nApnInfoAuthentication=1\r\nApnInfoCompression=0\r\nDeviceComplianceEnabled=0\r\nDeviceComplianceSsoEnabled=0\r\nDeviceComplianceSsoEku=\r\nDeviceComplianceSsoIssuer=\r\nFlagsSet=0\r\nOptions=0\r\nDisableDefaultDnsSuffixes=0\r\nNumTrustedNetworks=0\r\nNumDnsSearchSuffixes=0\r\nPowershellCreatedProfile=0\r\nProxyFlags=0\r\nProxySettingsModified=0\r\nProvisioningAuthority=\r\nAuthTypeOTP=0\r\nGREKeyDefined=0\r\nNumPerAppTrafficFilters=0\r\nAlwaysOnCapable=0\r\nDeviceTunnel=0\r\nPrivateNetwork=0\r\n\r\nNETCOMPONENTS=\r\nms_msclient=0\r\nms_server=0\r\n\r\nMEDIA=rastapi\r\nPort=VPN4-0\r\nDevice=WAN Miniport (L2TP)\r\n\r\nDEVICE=vpn\r\nPhoneNumber={0}\r\nAreaCode=\r\nCountryCode=0\r\nCountryID=0\r\nUseDialingRules=0\r\nComment=\r\nFriendlyName=\r\nLastSelectedPhone=0\r\nPromoteAlternates=0\r\nTryNextAlternateOnFail=1\r\n\r\n";
        string IKEV2 = "[IKEv2]\r\nEncoding=1\r\nPBVersion=5\r\nType=2\r\nAutoLogon=0\r\nUseRasCredentials=1\r\nLowDateTime=-50370016\r\nHighDateTime=30795820\r\nDialParamsUID=20638890\r\nGuid=D770AAB2E5D385438C3B39E9CA1C82F8\r\nVpnStrategy=7\r\nExcludedProtocols=0\r\nLcpExtensions=1\r\nDataEncryption=8\r\nSwCompression=0\r\nNegotiateMultilinkAlways=0\r\nSkipDoubleDialDialog=0\r\nDialMode=0\r\nOverridePref=15\r\nRedialAttempts=3\r\nRedialSeconds=60\r\nIdleDisconnectSeconds=0\r\nRedialOnLinkFailure=1\r\nCallbackMode=0\r\nCustomDialDll=\r\nCustomDialFunc=\r\nCustomRasDialDll=\r\nForceSecureCompartment=0\r\nDisableIKENameEkuCheck=0\r\nAuthenticateServer=0\r\nShareMsFilePrint=0\r\nBindMsNetClient=0\r\nSharedPhoneNumbers=0\r\nGlobalDeviceSettings=0\r\nPrerequisiteEntry=\r\nPrerequisitePbk=\r\nPreferredPort=VPN2-0\r\nPreferredDevice=WAN Miniport (IKEv2)\r\nPreferredBps=0\r\nPreferredHwFlow=1\r\nPreferredProtocol=1\r\nPreferredCompression=1\r\nPreferredSpeaker=1\r\nPreferredMdmProtocol=0\r\nPreviewUserPw=1\r\nPreviewDomain=1\r\nPreviewPhoneNumber=0\r\nShowDialingProgress=1\r\nShowMonitorIconInTaskBar=1\r\nCustomAuthKey=26\r\nAuthRestrictions=128\r\nIpPrioritizeRemote=1\r\nIpInterfaceMetric=0\r\nIpHeaderCompression=0\r\nIpAddress=0.0.0.0\r\nIpDnsAddress=0.0.0.0\r\nIpDns2Address=0.0.0.0\r\nIpWinsAddress=0.0.0.0\r\nIpWins2Address=0.0.0.0\r\nIpAssign=1\r\nIpNameAssign=1\r\nIpDnsFlags=0\r\nIpNBTFlags=1\r\nTcpWindowSize=0\r\nUseFlags=2\r\nIpSecFlags=0\r\nIpDnsSuffix=\r\nIpv6Assign=1\r\nIpv6Address=::\r\nIpv6PrefixLength=0\r\nIpv6PrioritizeRemote=1\r\nIpv6InterfaceMetric=0\r\nIpv6NameAssign=1\r\nIpv6DnsAddress=::\r\nIpv6Dns2Address=::\r\nIpv6Prefix=0000000000000000\r\nIpv6InterfaceId=0000000000000000\r\nDisableClassBasedDefaultRoute=0\r\nDisableMobility=0\r\nNetworkOutageTime=1800\r\nIDI=\r\nIDR=\r\nImsConfig=0\r\nIdiType=0\r\nIdrType=0\r\nProvisionType=0\r\nPreSharedKey=\r\nCacheCredentials=1\r\nNumCustomPolicy=0\r\nNumEku=0\r\nUseMachineRootCert=0\r\nDisable_IKEv2_Fragmentation=0\r\nNumServers=0\r\nRouteVersion=1\r\nNumRoutes=0\r\nNumNrptRules=0\r\nAutoTiggerCapable=0\r\nNumAppIds=0\r\nNumClassicAppIds=0\r\nSecurityDescriptor=\r\nApnInfoProviderId=\r\nApnInfoUsername=\r\nApnInfoPassword=\r\nApnInfoAccessPoint=\r\nApnInfoAuthentication=1\r\nApnInfoCompression=0\r\nDeviceComplianceEnabled=0\r\nDeviceComplianceSsoEnabled=0\r\nDeviceComplianceSsoEku=\r\nDeviceComplianceSsoIssuer=\r\nFlagsSet=0\r\nOptions=0\r\nDisableDefaultDnsSuffixes=0\r\nNumTrustedNetworks=0\r\nNumDnsSearchSuffixes=0\r\nPowershellCreatedProfile=0\r\nProxyFlags=0\r\nProxySettingsModified=0\r\nProvisioningAuthority=\r\nAuthTypeOTP=0\r\nGREKeyDefined=0\r\nNumPerAppTrafficFilters=0\r\nAlwaysOnCapable=0\r\nDeviceTunnel=0\r\nPrivateNetwork=0\r\n\r\nNETCOMPONENTS=\r\nms_msclient=0\r\nms_server=0\r\n\r\nMEDIA=rastapi\r\nPort=VPN2-0\r\nDevice=WAN Miniport (IKEv2)\r\n\r\nDEVICE=vpn\r\nPhoneNumber={0}\r\nAreaCode=\r\nCountryCode=0\r\nCountryID=0\r\nUseDialingRules=0\r\nComment=\r\nFriendlyName=\r\nLastSelectedPhone=0\r\nPromoteAlternates=0\r\nTryNextAlternateOnFail=1\r\n\r\n";

        string phoneBookPath;
        Server server;

        public event OnConnectDisconnect onConnectDisconnect;
        RasHandle rasHandle;

        string username;
        string password;
        public VPNService(Server server, GlobalInfo globalInfo)
        {
          //  timerDisconnect = new Timer(DisconnectTimerCallback, null, int.MaxValue, int.MaxValue);
            gInfo = globalInfo;
            this.username = gInfo.Username;
            this.password = gInfo.Password;
            this.server = server;
            ProtocolsData = new string[] { PPTP, L2TP, IKEV2 };
            phoneBookPath = Path.Combine(Path.GetTempPath(), "vpn.pbk");
        }
       /* public void DisconnectTimerCallback(object state)
        {
            timerDisconnect.Change(int.MaxValue, int.MaxValue);
            if (onConnectDisconnect != null)
                onConnectDisconnect.Invoke(this, false, 0, null);
        }*/
        public void Connect(string protocol)
        {
            selectedProtocol = protocol;
               var pData = ProtocolsData.Where(v => Regex.IsMatch(v, string.Format("\\[{0}\\]", protocol)));
            if (pData.Count() > 0)
            {
                watcher = new RasConnectionWatcher();
                watcher.Disconnected += Watcher_Disconnected;
                watcher.EnableRaisingEvents = true;
                var ProtocolStr = string.Format(pData.First(), server.Address);
                File.WriteAllText(phoneBookPath, ProtocolStr);
                RasDialer dialer = new RasDialer();
                dialer.DialCompleted += Dialer_DialCompleted;
                dialer.PhoneBookPath = phoneBookPath;
                dialer.Timeout = 20000;
                dialer.EntryName = protocol;
                dialer.Credentials = new System.Net.NetworkCredential(username,password);
                rasHandle = dialer.DialAsync();

            }
            else
                throw new ProtocolNotFoundException(protocol);

            
        }

        private void Watcher_Disconnected(object sender, RasConnectionEventArgs e)
        {
            if (watcher != null)
            {
                watcher.Disconnected -= Watcher_Disconnected;
                watcher.Dispose();
                watcher = null;
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, null);
            }
        }

        private void Dialer_DialCompleted(object sender, DialCompletedEventArgs e)
        {
            if( onConnectDisconnect!=null)
            {
                onConnectDisconnect.Invoke(this, e.Connected,0, e.Connected ? "" : e.Error.Message);
            }
        }

        public void Disconnect()
        {

            Disconnect(false);
        }
        public void Disconnect(bool force)
        {
            Action act = delegate ()
            {
                bool hangeedup = false;
                var target = RasConnection.GetActiveConnections();
                foreach (RasConnection con in target)
                {
                    if (force || con.Handle == rasHandle)
                    {
                        hangeedup = true;
                        con.HangUp();

                    }
                }
                //timerDisconnect.Change(int.MaxValue, int.MaxValue);
                //if (target.Count() == 0&& onConnectDisconnect!=null && rasHandle==null)
                  //  onConnectDisconnect.Invoke(this, false, 0, "");
                /*else*/ if (!force && hangeedup && onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, "");
               

            };
            act.BeginInvoke(null,null);
             

        }        
        public void DisconnectAll()
        {
             Disconnect(true);
        }

        public bool IsRequirementAvailable()
        {
            return File.Exists( Path.Combine(gInfo.TempPath, "Ras\\7\\DotRas.dll")) && File.Exists(Path.Combine(gInfo.TempPath, "Ras\\XP\\DotRas.dll"));

        }

        public long UrlTest()
        {
            throw new NotImplementedException();
        }

        public List<Url> GetServerUrls()
        {
            return server.urls;
        }
    }
}
