using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Common;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using IRSpeedyVPN.Models.NewService;

namespace IRSpeedyVPN.Services
{
    internal class CiscoService : IVPNService
    {

        IServer server;
        NewServiceController serviceController { get; set; }
        GlobalInfo gInfo;
        public byte Order => 3;

        public int ID => server.ID;

        private string name;
        public string Name
        {
            get => name ?? "Cisco";
            set => name = value;
        }        
        public string CountryCode => server.Country;
        public string Country => server.Country.GetCountryName() + (CountryIndex > 0 ? $" {CountryIndex}" : "");

        public string[] Protocols => server.Protocol == null ? new string[0] : server.Protocol.Split(' ');

        public string SelectedProtocol => null;

        public bool IsUsingProxifire => false;

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

        public bool IsUrlTestSupported => true;

        public long UrlTestSpeed =>urlTestSpeed;

        public Url SelectedServerUrl { get; set; }

        public event OnConnectDisconnect onConnectDisconnect;

        string ciscoPath = "Cisco\\openconnect.exe";
        string openvpnPath = "OpenVpn\\openvpn.exe";
        string tapPath = "OpenVpn\\tap-windows.exe";
        string logPath = "Ciscolog.txt";
        private bool isConnected;
        private string password;
        private string username;
        private bool manualDisconnected;
        private string lastData;
        private long urlTestSpeed;
        private string selectedUrl;
        private bool cancelUrlTest;
        private DateTime lastUrlTest;

        public CiscoService(IServer server, GlobalInfo globalInfo)
        {
            gInfo = globalInfo;
            this.server = server;
            this.username = globalInfo.Username;
            this.password = globalInfo.Password;
            //OptionalData = new string[] { server.Optional1, server.Optional2 };
            isConnected = false;
        }
        public void Connect(string protocol)
        {
            Action act = () =>
            {
                InstallTapIfRequired();
                ConnectOpenConnect(protocol);
            };
            act.BeginInvoke(null, null);


        }

        void InstallTapIfRequired()
        {
            string ret = ShellExecute.ShellexecAndReturnStringOutput(Path.Combine(gInfo.TempPath, openvpnPath), "--show-adapters");

            if (!ret.Contains("{"))
            {

                var p = ShellExecute.ShellexecAndReturnProcess(Path.Combine(gInfo.TempPath, tapPath), "/SELECT_TAP=1 /SELECT_UTILITIES=0 /SELECT_SDK=0 /S");
                //   MessageBox.Show("installing");
                p.WaitForExit();
                // MessageBox.Show("installed");
            }

        }

        private void ConnectOpenConnect(string protocol)
        {
            isConnected = false;
            manualDisconnected = false;
            

            try
            {
                
                    UrlTest(true);
                    if (urlTestSpeed < 0)
                    {
                        onConnectDisconnect.Invoke(this, false, 0, "سروری یافت نشد");
                        return;

                    }
                    LogHelper.WriteExLog($"ss\t{selectedUrl}\n");

                
                string args = string.Format("-u {0} -b {1} {2} --http-auth=Basic --no-cert-check", username, password, server.urls[0].url.Replace("cisco://","https://")  );
                var p = ShellExecute.ShellexecAndReturnProcessRedirectOutput(Path.Combine(gInfo.TempPath, ciscoPath), args,false);                
                
                p.OutputDataReceived += P_OutputDataReceived;
                p.ErrorDataReceived += P_ErrorDataReceived;
                p.EnableRaisingEvents = true;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                File.WriteAllText(Path.Combine(gInfo.TempPath, logPath), "");
                Thread.Sleep(1000);              

                 p.WaitForExit();
                if (!manualDisconnected)
                    Disconnect(TranslateMessage(lastData));

            }
            catch (Exception e)
            {
                Disconnect(e.Message);

            }

        }

        private string TranslateMessage(string s)
        {
            if(s.Contains("Failed to obtain WebVPN cookie"))
            {
                s = "Please try again in the next few minutes";
            }
            return s;
        }

        private void P_ErrorDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
        {
            DataReceived(e.Data);
        }

        private void P_OutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
        {
            DataReceived(e.Data);
        }
        void DataReceived(string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                lock (string.Intern(logPath))
                {
                    lastData = s;
                    File.AppendAllText(Path.Combine(gInfo.TempPath, logPath), s + "\n");
                }
                //   if (s.Contains("Established DTLS connection"))
                if (s.Contains("Connected as"))
                {
                    Thread.Sleep(3000); //wait for network ip Indentified
                    isConnected = true;
                    onConnectDisconnect.Invoke(this, true, 0, "");
                }
            }
        }
        public void Disconnect(String message)
        {
            lastData = "";
            ShellExecute.KillProccess("openconnect");
            if (onConnectDisconnect != null)
            {
                onConnectDisconnect.Invoke(this, false, 0, message);
            }
        }
        public void Disconnect()
        {
            manualDisconnected = true;
            Disconnect(null);
        }

        public void DisconnectAll()
        {
            Disconnect();
        }

        public bool IsRequirementAvailable()
        {
            return File.Exists(Path.Combine(gInfo.TempPath, ciscoPath)) && File.Exists(Path.Combine(gInfo.TempPath, openvpnPath)) && File.Exists(Path.Combine(gInfo.TempPath, tapPath));
        }

        public long UrlTest()
        {
            return UrlTest(false);
        }

        public long UrlTest(bool force)
        {
            if (!force && UrlTestCoordinator.AbortRequested)
                return urlTestSpeed;

            if (urlTestSpeed < 0 || lastUrlTest == null || (DateTime.Now - lastUrlTest) > TimeSpan.FromSeconds(60))
            {

                // var task = Task.Run(() => UrlTestFull());
                var task=  Task.Factory.StartNew(() => UrlTestFull(force));
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
        public void UrlTestFull(bool force = false)
        {
            cancelUrlTest = false;
            if (!force && UrlTestCoordinator.AbortRequested)
                return;
            urlTestSpeed = -1;
            List<Action> lstAct = new List<Action>();
            foreach (var u in server.urls.Randomize())
            {
                if ((!force && (cancelUrlTest || UrlTestCoordinator.AbortRequested)) || (urlTestSpeed > 0 && urlTestSpeed < 500))
                    break;
                AutoResetEvent re = new AutoResetEvent(false);
                lock (Locks.UrlTest)
                {

                    Action a = () =>
                    {
                        Process p = null;
                        
                        try
                        {

                          
                            var s = ServiceHelper.UnsafeUrlTest(u.url.Replace("cisco://", "https://"));
                            if (s > 0 && (urlTestSpeed < 0 || s < urlTestSpeed))
                            {
                                urlTestSpeed = s;
                                selectedUrl = u.url;
                                LogHelper.WriteExLog($"{s}\t{u.url}");
                            }
                            u.latency = s > 0 ? s : -1;
                            u.latencychkTime = DateTime.Now;
                            p.Kill();
                        }
                        catch
                        {
                            u.latency = -1;
                            u.latencychkTime = DateTime.Now;
                            if (p != null)
                                try
                                {
                                    p.Kill();
                                }
                                catch { }
                        }
                        re.Set();
                    };
                    lstAct.Add(a);
                    a.BeginInvoke((b) => { lstAct.Remove((Action)b.AsyncState); }, a);
                    re.WaitOne(2000);
                }


            }
            while (lstAct.Count != 0) ;

        }

        public List<Url> GetServerUrls()
        {
            return server.urls;
        }
    }
}
