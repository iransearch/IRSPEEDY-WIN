using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Security;
using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.Windows;
using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Services
{
    class SSRService : IVPNService
    {        
        NewServiceController serviceController { get; set; }
        GlobalInfo gInfo;      
        private string name;
        public string Name
        {
            get => name ?? "PowerVpn";
            set => name = value;
        }
        public int ID => server.ID;
        public string[] Protocols => server.Protocol == null ? new string[0] : server.Protocol.Split(' ');
        public bool IsUsingProxifire => true;
        public string Country => server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");

        public string SelectedProtocol => null;
        public string CountryCode => server.Country;
        public byte Order => 2;
        public byte CountryIndex { get; set; }
        public Type SettingType => typeof(SSRServiceSetting);
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;
        public ProxifierType ProxifierRuleType => (RegHelper.GetSettingValue("SSRGlobalProxy") != "0" ? ProxifierType.Global :
            ((RegHelper.GetSettingValue("SSRTelegramRoute") == "1") ? ProxifierType.Telegram : ProxifierType.Normal));

        public bool ProxifierWithPassword => false;

        public ProxyType ProxyType => ProxyType.SOCKS;
        public void ApplyShareSetting()
        {
        }

        public bool IsUrlTestSupported => false;

        public long UrlTestSpeed => throw new NotImplementedException();

        public Url SelectedServerUrl { get; set; }

        Server server;
        
        public event OnConnectDisconnect onConnectDisconnect;
        Process ssrprocess;
        bool useSystemProxy = false;
        string powerVpnPath;
        string userRulePath;
        public SSRService(Server server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            powerVpnPath = Path.Combine(gInfo.TempPath, "PowerVpn\\powervpn.exe");
            userRulePath = Path.Combine(gInfo.TempPath, "PowerVpn\\user.rule");
            serviceController = (NewServiceController)Program.container.GetInstance(typeof(NewServiceController));
        }

        public void Connect(string protocol)
        {
            useSystemProxy = RegHelper.GetSettingValue("SSRGlobalProxy") != "0";
            ((Action)(() => RunSsr(server.Address))).BeginInvoke(null,null);            
        }
        void RunSsr(string ssrUrl)
        {
            try
            {
                if(serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
               // if (ServiceHelper.CheckAvailabilty(gInfo.Username,gInfo.Password))
                {
                   bool userCustom = true;
                    try
                    {
                        string HashUrl = "http://apichcek-p.isdm.ir/dl/rule.crc.txt";                        
                        bool needDownloadRule = true;
                        if (File.Exists(userRulePath))
                            using (Stream s = File.OpenRead(userRulePath))
                            {

                                try
                                {
                                    if (SimpleDownloadManager.DownloadString(HashUrl) == Tools.GetCRC32(s))
                                        needDownloadRule = false;
                                }
                                catch { }

                            }
                        if (needDownloadRule)
                        {
                            string RuleURL = "http://apichcek-p.isdm.ir/dl/user.zip.txt";
                            File.WriteAllBytes(userRulePath + ".zip", SimpleDownloadManager.DownloadData(RuleURL));
                            var ResourceManager = ((ResourceManager)Program.container.GetInstance(typeof(ResourceManager)));
                            ResourceManager.Extract(userRulePath + ".zip", Path.GetDirectoryName(powerVpnPath));
                            File.Delete(userRulePath + ".zip");
                        }
                    

                    }
                    catch
                    {
                        userCustom = false;
                    }
                    TripleDesHelper tdes = new TripleDesHelper(0x12, 0xff, "bc", "8C", "FA", "d1", "e2", 0xea, 0xb8, "82", 0x9c, "3F", "4b", 0x85, "85", "F9", 0xcc, "41", "18", 0x94, BitConverter.GetBytes((int)(DateTime.Now.Ticks / 1000000000)));
                    var uData = Convert.ToBase64String(tdes.Encrypt(Encoding.UTF8.GetBytes(ssrUrl)));

                    ssrprocess = ShellExecute.ShellexecAndReturnProcess(powerVpnPath, string.Format("connectx {0} SystemProxy {1} uc {2}", uData, useSystemProxy ? 1 : 0, userCustom ? 1 : 0));
                    if (onConnectDisconnect != null)
                    {
                        onConnectDisconnect.Invoke(this, true, 1080, "");

                    }
                }
                else
                {
                    onConnectDisconnect.Invoke(this, false, 0, "تعداد اتصالات بیش از حد مجاز است");
                }
            }catch(Exception ex)
            {
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, ex.Message);
            }

        }

       

        public void Disconnect()
        {
            if (useSystemProxy)
                SystemProxy.Disable();
            ShellExecute.KillProccess("powervpn");
            if (onConnectDisconnect != null)
                onConnectDisconnect.Invoke(this, false, 0, "");
            
        }

        public void DisconnectAll()
        {
            Disconnect();
        }

        public bool IsRequirementAvailable()
        {
            return File.Exists(powerVpnPath);
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
