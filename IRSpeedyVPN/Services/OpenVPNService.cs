using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Socket;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace IRSpeedyVPN.Services
{
    internal class OpenVPNService : IVPNService
    {

        GlobalInfo gInfo;
        string lastData;
        char[] buffer = new char[1024];
        string name;
        public string Name
        {
            get => name ?? "OpenVpn";
            set => name = value;
        }
        public int ID => server.ID;
        public string[] Protocols => server.Protocol.Split(' ');
        public bool IsUsingProxifire => false;
        public string Country => server.Country.GetCountryName() + (CountryIndex>0?$" {CountryIndex}":"");
        public string CountryCode => server.Country;
        string selectedProtocol = null;
        public string SelectedProtocol => selectedProtocol;

        public byte Order => 1;

        public Type SettingType => null;
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;

        public ProxifierType ProxifierRuleType => throw new NotImplementedException();

        public byte CountryIndex { get; set; }

        public bool ProxifierWithPassword => throw new NotImplementedException();

        public ProxyType ProxyType => throw new NotImplementedException();
        public void ApplyShareSetting()
        {
        }

        public bool IsUrlTestSupported => false;

        public long UrlTestSpeed => throw new NotImplementedException();

        public Url SelectedServerUrl { get; set; }

        Server server;
        public event OnConnectDisconnect onConnectDisconnect;
        string username;
        string password;
        string openvpnPath = "OpenVpn\\openvpn.exe";
        string tapPath = "OpenVpn\\tap-windows.exe";
        string logPath = "OpenVpnlog.txt";
        StringSocketClient managementClient;
        string[] OptionalData;
        bool isConnected;
        bool manualDisconnected = false;
        public OpenVPNService(Server server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            this.username = globalInfo.Username;
            this.password = globalInfo.Password;
            OptionalData = new string[] { server.Optional1, server.Optional2 };
            isConnected = false;
        }
        public void Connect(string protocol)
        {
           
            selectedProtocol = protocol;
               Action act = () =>
            {
                InstallTapIfRequired();
                ConnectOpenVpn(protocol);
            };
            act.BeginInvoke(null, null);
            

        }

        private void ConnectOpenVpn(string protocol)
        {
            isConnected = false;
            manualDisconnected = false;
            //var pData = ProtocolsData.Where(v => Regex.IsMatch(v, string.Format("{0}\\r\\n", protocol.ToLower())));

            //var ProtocolStr = string.Format(pData.First(), server.Address, string.IsNullOrEmpty(server.Port) ? (protocol == "TCP" ? "7080" : "82") : server.Port);
            var filename = OptionalData.Any(x => x.Contains(protocol)) ? OptionalData.Where(x => x.Contains(protocol)).First() : OptionalData.Where(x => x.Length > 0).FirstOrDefault();
                var ProfileUrl = Path.Combine(gInfo.settings.setting.openvpnProfile_url, filename);

            try
            {
                var ProfilePath = Path.Combine(Path.GetTempPath(), "vpn.ovpn");
                File.WriteAllText(ProfilePath, SimpleDownloadManager.DownloadString(ProfileUrl));
                string args = string.Format("--management {0}  {1} --management-query-passwords --config {2}", "127.0.0.1", gInfo.ManagementPort, ProfilePath);
                var p = ShellExecute.ShellexecAndReturnProcessRedirectOutput(Path.Combine(gInfo.TempPath, openvpnPath), args);
                p.BeginOutputReadLine();
                p.OutputDataReceived += P_OutputDataReceived;
           
                Thread.Sleep(1000);
                File.WriteAllText(Path.Combine(gInfo.TempPath, logPath), "");
                managementClient = new StringSocketClient();
                managementClient.Connect("127.0.0.1", gInfo.ManagementPort);
                managementClient.onDataReceived += ManagementClient_onDataReceived;

                p.WaitForExit();
                if (!manualDisconnected)
                    Disconnect(lastData);

            }
            catch (Exception e)
            {
                Disconnect(e.Message);
               
            }
         
        }

        private void P_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {            
            if(e?.Data!=null)
            {
                lastData = e.Data;
                if (e.Data.Contains("Initialization Sequence Completed"))
                {
                    isConnected = true;
                    onConnectDisconnect.Invoke(this, true,0, "");
                }
                File.AppendAllText(Path.Combine(gInfo.TempPath, logPath),e.Data + "\n");
            }
            else
            {
              //  Disconnect();
            }
        }

        private void ManagementClient_onDataReceived(string data)
        {
                        
            string ret =ManagementDataProccess(data);
            if (!string.IsNullOrEmpty(ret)&& managementClient!=null)
            {
                managementClient.Write(ret);
            }
        }

        void InstallTapIfRequired()
        {
            string ret =ShellExecute.ShellexecAndReturnStringOutput(Path.Combine(gInfo.TempPath, openvpnPath), "--show-adapters");
            
            if (!ret.Contains("{"))
            {
                
                var p=ShellExecute.ShellexecAndReturnProcess(Path.Combine(gInfo.TempPath, tapPath), "/SELECT_TAP=1 /SELECT_UTILITIES=0 /SELECT_SDK=0 /S");
             //   MessageBox.Show("installing");
                p.WaitForExit();
               // MessageBox.Show("installed");
            }

        }
        public void Disconnect()
        {
            manualDisconnected = true;
            Disconnect(null);
        }
        public void Disconnect(string message)
        {
            lastData = "";
            ShellExecute.KillProccess("openvpn");
            if (onConnectDisconnect != null)
            {
                onConnectDisconnect.Invoke(this, false, 0, message);
            }
        }

        public void DisconnectAll()
        {
            Disconnect();
        }

        public string ManagementDataProccess(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {

                if (data.Contains("PASSWORD:Need "))
                {
                    return string.Format("username {0} {1}\npassword {0} {2}\n", Regex.Match(data, "PASSWORD:Need '(.+)'").Groups[1].Value, username, password);
                }
                
               
            }
            return null;
        }

        public bool IsRequirementAvailable()
        {            
            return File.Exists(Path.Combine(gInfo.TempPath, openvpnPath)) && File.Exists(Path.Combine(gInfo.TempPath, tapPath));
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
