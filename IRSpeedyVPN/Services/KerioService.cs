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
    internal class KerioService : IVPNService
    {
        ServiceController serviceController { get; set; }
        GlobalInfo gInfo;
        public string Name => "STunnel".ToUpper();
        public int ID => server.ID;
        public string[] Protocols => server.Protocol.Split(' ');
        public bool IsUsingProxifire => true;
        public string Country => server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");

        public string SelectedProtocol => null;
        public string CountryCode => server.Country;
        public byte Order => 0;
        public byte CountryIndex { get; set; }
        public Type SettingType => typeof(STunnelServiceSetting);
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;
        public ProxifierType ProxifierRuleType => (RegHelper.GetSettingValue("STunnelGlobalProxy") != "0" ? ProxifierType.Global :
            ((RegHelper.GetSettingValue("STunnelTelegramRoute") == "1") ? ProxifierType.Telegram : ProxifierType.Normal));

        public bool ProxifierWithPassword => true;

        public ProxyType ProxyType => ProxyType.HTTPS;
        public void ApplyShareSetting()
        {
        }

        Server server;

        public event OnConnectDisconnect onConnectDisconnect;
        Process stprocess;
        bool useSystemProxy = false;
        string stPath;
        string stconfigPath;
        public KerioService(Server server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            stPath = Path.Combine(gInfo.TempPath, "STunnel\\tstunnel.exe");
            stconfigPath = Path.Combine(gInfo.TempPath, "s.cfg");
            serviceController = (ServiceController)Program.container.GetInstance(typeof(ServiceController));
        }

        public void Connect(string protocol)
        {
            useSystemProxy = RegHelper.GetSettingValue("STunnelGlobalProxy") != "0";
            ((Action)(() => RunSTunnel())).BeginInvoke(null, null);
        }
        void RunSTunnel()
        {
            try
            {
                if (serviceController.CheckUserPermission(gInfo.Username, gInfo.Password))
                // if (ServiceHelper.CheckAvailabilty(gInfo.Username,gInfo.Password))
                {

                    
                        string strcfgtemp;

                        if (server.Address.ToLower().IndexOf("http") == 0)
                        {
                            strcfgtemp = $"fips = yes\r\noptions = NO_SSLv2\r\ncompression = zlib\r\n[openvpn]\r\nclient = yes\r\naccept = 127.0.0.1:1080\r\nconnect ={server.Address.Substring(server.Address.IndexOf(":") + 1)}";
                        }
                        else
                        {
                            strcfgtemp = $"cert = stunnel.pem\r\nclient = yes\r\ntaskbar = no\r\n[squid]\r\naccept = 127.0.0.1:1080\r\nconnect = { server.Address}";
                        }
                        File.Delete(stconfigPath);
                        File.WriteAllText(stconfigPath, strcfgtemp);
                        File.SetAttributes(stconfigPath, FileAttributes.Hidden);
                        stprocess = ShellExecute.ShellexecAndReturnProcess(stPath, stconfigPath);
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
                Disconnect();
                if (onConnectDisconnect != null)
                    onConnectDisconnect.Invoke(this, false, 0, ex.Message);
            }

        }



        public void Disconnect()
        {
            if (useSystemProxy)
                SystemProxy.Disable();
            ShellExecute.KillProccess("st");
            if (onConnectDisconnect != null)
                onConnectDisconnect.Invoke(this, false, 0, "");

        }

        public void DisconnectAll()
        {
            Disconnect();
        }

        public bool IsRequirementAvailable()
        {
            return File.Exists(stPath);
        }
    }
}

