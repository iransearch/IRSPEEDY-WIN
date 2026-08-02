using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.Windows;
using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Services
{
    class sslProxyService : IVPNService
    {
        ServiceController serviceController { get; set; }
        GlobalInfo gInfo;
        public string Name => "VPN+".ToUpper();
        public int ID => server.ID;
        public string[] Protocols => server.Protocol.Split(' ');
        public bool IsUsingProxifire => true;
        public string Country => server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");
        public string CountryCode => server.Country;
        public string SelectedProtocol => null;

        public byte Order => 0;
        public byte CountryIndex { get; set; }
        public Type SettingType => typeof(VPNPlusServiceSetting);
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;



        public ProxifierType ProxifierRuleType {
            get
            {
                if (RegHelper.GetSettingValue("VPNPlusGlobalProxy") != "0")
                    return ProxifierType.Global;
               else if (RegHelper.GetSettingValue("ProxifierSmartRoute") == "1")
                    return ProxifierType.Smart;
                else if (RegHelper.GetSettingValue("ProxifierSmartRoute") == "2")
                    return ProxifierType.Telegram;
                return ProxifierType.Normal;

            }
        }

        public bool ProxifierWithPassword => false;

        public ProxyType ProxyType => ProxyType.SOCKS;
        public void ApplyShareSetting()
        {
        }

        Server server;

        public event OnConnectDisconnect onConnectDisconnect;
        Process ssrprocess;
        bool useSystemProxy = false;
        string vpnPlusPath;
        private string configPath;
        public sslProxyService(Server server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            vpnPlusPath = Path.Combine(gInfo.TempPath, "VPNPlus\\vpnplus.exe");
            configPath = Path.Combine(Path.GetTempPath(), "v.json");
            serviceController = (ServiceController)Program.container.GetInstance(typeof(ServiceController));
        }

        public void Connect(string protocol)
        {
            useSystemProxy = RegHelper.GetSettingValue("VPNPlusGlobalProxy") != "0";
            ((Action)(() => RunGo(server.Address))).BeginInvoke(null, null);
        }
        void RunGo(string goUrl)
        {
            try
            {
                if (serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
                // if (ServiceHelper.CheckAvailabilty(gInfo.Username,gInfo.Password))
                {

                        if (File.Exists(this.configPath))
                        {
                            File.Delete(this.configPath);
                        }


                    if (this.server.Address.Contains("@"))
                    {
                        ssrprocess = ShellExecute.ShellexecAndReturnProcess(vpnPlusPath, $"-url \"{server.Address.Replace("trojan:","trojan-go:")}\"");
                    }
                    else
                    {
                        File.WriteAllBytes(this.configPath, Convert.FromBase64String(this.server.Address.Substring(this.server.Address.IndexOf("://") + 3)));
                        File.SetAttributes(this.configPath, FileAttributes.Hidden);

                        ssrprocess = ShellExecute.ShellexecAndReturnProcess(vpnPlusPath, "-config " + configPath);
                    }
                        if(useSystemProxy)
                        {
                            WinINet.SetIEProxy(true, true, "http://127.0.0.1:1080", null);
                        }
                    if (onConnectDisconnect != null)
                    {
                        onConnectDisconnect.Invoke(this, true, 1080, "");

                    }
                }
                else
                {
                    onConnectDisconnect.Invoke(this, false, 0, "تعداد اتصالات بیش از حد مجاز است");
                }
            }
            catch (Exception ex)
            {
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, ex.Message);
            }

        }



        public void Disconnect()
        {
            if (useSystemProxy)
                SystemProxy.Disable();
            ShellExecute.KillProccess("vpnplus");
            if (onConnectDisconnect != null)
                onConnectDisconnect.Invoke(this, false, 0, "");

        }

        public void DisconnectAll()
        {
            Disconnect();
        }

        public bool IsRequirementAvailable()
        {
            return File.Exists(vpnPlusPath);
        }
    }
}
