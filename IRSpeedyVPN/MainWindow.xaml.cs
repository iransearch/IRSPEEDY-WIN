using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Resource;
using System.Reflection;
using System.Diagnostics;
using IRSpeedyVPN.Security;
using Shadowsocks.Controller;
using System.IO;
using IRSpeedyVPN.Models.NewService;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace IRSpeedyVPN
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window

    {
        UCLogin uCLogin => AppServices.UCLogin;
        UCServerList uCServerList => AppServices.UCServerList;
        UCUserInfo uCUserInfo => AppServices.UCUserInfo;
        UCUpdate uCUpdate => AppServices.UCUpdate;
        UCChangePassword uCChangePassword => AppServices.UCChangePassword;

        NewServiceController serviceController => AppServices.NewServiceController;

        private ServiceFactory serviceFactory => AppServices.ServiceFactory;
        private IProxifier proxifier => AppServices.Proxifier;

        GlobalInfo gInfo => AppServices.GlobalInfo;
        ResourceManager localResource => AppServices.ResourceManager;
        System.Windows.Forms.NotifyIcon notify;
        int Initialized = 0;        
        StringSocketListener ManagementListener;
        Timer mainTimer;
        uint timercounter = 0;
        uint timerRenewInfo = 0;
        bool isUpdateAvailable;
        bool LoadLocal =false &&  Debugger.IsAttached;
        object lastControl;
        bool IsRememberChecked;
        bool IsUserLogin = false;
        string lastLoginUsername;
        string lastLoginPassword;
        DeviceLimitWindow deviceLimitWindow;
        GeoIp IpInfo = null;
        readonly Dictionary<object, List<HeaderIconRegistration>> headerIconMap = new Dictionary<object, List<HeaderIconRegistration>>();
        object currentHeaderOwner;
        private Stopwatch loginUiStopwatch;
        private readonly SemaphoreSlim connectionRequestGate = new SemaphoreSlim(1, 1);
        private long connectionRequestVersion;
        private IVPNService registeredVpnService;
        private IRSpeedyVPN.Events.OnConnectDisconnect registeredVpnHandler;
        public MainWindow()
        {

            InitializeComponent();
            mainTimer = new Timer(mainTimerCallback, null, int.MaxValue, int.MaxValue);
            SetupNotify();
            txtVersion.Text = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            /* double netVersion = 0;                       
            try
            {


                netVersion = double.Parse($"{NetVersions.NETInstalled.Major}.{NetVersions.NETInstalled.Minor}");
                File.WriteAllText(".\\netVersion.txt", NetVersions.NETInstalled.ToString());
               
                if (netVersion < 4.8)
                    txtGlobalMessage.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { 
                //File.WriteAllText(".\\netVersion.txt", ex.Message);
                }
         */ 
          //  var isobf=ObfuscateManager.IsObfucated();
        }
        void SetupNotify()
        {
            notify = new System.Windows.Forms.NotifyIcon();
            // Read the embedded branding resource, not Windows' executable icon cache.
            using (var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/Irspeedy/Logo/irspeedy.ico", UriKind.Absolute)).Stream)
            using (var icon = new System.Drawing.Icon(stream))
            {
                notify.Icon = (System.Drawing.Icon)icon.Clone();
            }
            notify.Visible = false;
            notify.DoubleClick += Notify_DoubleClick;
            notify.BalloonTipClosed += Notify_BalloonTipClosed;
            notify.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            notify.ContextMenuStrip.Items.Add("Show", null, this.Notify_DoubleClick);
            notify.ContextMenuStrip.Items.Add("Exit", null, this.Notify_Exit);
            notify.Visible = true;
        }

        private void Notify_BalloonTipClosed(object sender, EventArgs e)
        {

            //notify.Visible = (this.Visibility != Visibility.Visible);
        }

        private void Notify_DoubleClick(object sender, EventArgs e)
        {
            this.Visibility = Visibility.Visible;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
            //  notify.Visible = false;
        }

        private void Notify_Exit(object sender, EventArgs e)
        {
            Services.Hotspot.DirectHotspot.Controller.Stop();
            proxifier.Detach();
            DisconnectAll();
            System.Windows.Application.Current.Shutdown();
        }
        public void mainTimerCallback(object state)
        {
            timercounter++;

            timerRenewInfo++;
            if (timercounter % 30 == 0)
            {

                if (gInfo.ServerResponse.GetJsonString("user_data.ExpiryDate").IsValidTimeFormat() && gInfo.ExpiryDate != null && gInfo.ExpiryDate < DateTime.Now)
                {
                    RechareLogout();
                }
            }
            /*
            if (timercounter>=5*60 &&DateTime.Now>new DateTime(2020,08,16))
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    if (gInfo.CurrentService != null)
                        gInfo.CurrentService.Disconnect();
                    proxifier.Detach();
                }));
            }*/
            if (timerRenewInfo >= 10 * 60 && !isUpdateAvailable && !LoadLocal)
            {
                timerRenewInfo = 0;
                RunAsync(() =>
             {
                 Login(gInfo.Username, gInfo.Password, IsRememberChecked, true);

             }, false);
            }
        }
        private void Window_Activated(object sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref Initialized, 1, 0) == 0)
            {
                //                Initialized = 1;
                uCServerList.OnConnectRequest += UCServerList_OnConnectRequest;
                uCServerList.OnLoadingRequest += OnLoadingRequest;
                uCLogin.OnCredentialEntered += UCLogin_OnCredentialEntered;
                uCUserInfo.OnChangeServerRequest += UCUserInfo_OnChangeServerRequest;
                uCUserInfo.OnDisconnectRequest += UCUserInfo_OnDisconnectRequest;
                uCUserInfo.OnLoadingRequest += OnLoadingRequest;
                uCLoading.OnCancelRequest += UCLoading_OnCancelRequest;
                uCChangePassword.OnResult += UCChangePassword_OnResult;                
                ShowControl(uCLogin);
                proxifier.onResult += Proxifier_onResult;
                TransitionBox.Transition = new Transitionals.Transitions.RotateTransition() { Direction=Transitionals.Transitions.RotateDirection.Right};
                TransitionBox.TransitionEnded += TransitionBox_TransitionEnded;

#if _PREMIUM
                lblPremium.Visibility = Visibility.Visible;
#endif

                //ManagementListener = new StringSocketListener(gInfo.ManagementPort, 100);
                //  try
                //  {
                //      ManagementListener.Listen();
                //  }
                //  catch { }
                //  ManagementListener.onDataReceived += ManagementListener_onDataReceived;

                SystemProxy.Disable();
                AppDomain currentDomain = AppDomain.CurrentDomain;
                currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                localResource.onResourceExtracted += LocalResource_onResourceExtracted;
                localResource.ExtractResource();
               // ShareVPNSetting speedyShieldSetting = new ShareVPNSetting();
                //speedyShieldSetting.ShowDialog(); //hint: test
                if (File.Exists("./debug.txt"))
                {
                    ShellExecute.HideWindow = false;
                }
                if (File.Exists("./oneclick.txt"))
                {
                    LoadLocal = true;
                }
                if (!LoadLocal)
                {
                    var acc = localResource.GetConfig();
                    if (acc != null)
                    {
                        uCLogin.SetUserPassword(acc.UserAccount.Username, localResource.Password);
                        RunAsync(() =>
                        {

                            if (!Login(acc.UserAccount.Username, localResource.Password, true))
                            {
                                acc = localResource.GetConfig();
                                if (acc != null)
                                    ProcessInfo(acc, localResource.Password);

                            }
                        });

                    }
                }
                else
                {
                    var acc = localResource.GetLocalConfig();
                    if (acc != null)
                    {
                        IsRememberChecked = true;
                        ProcessInfo(acc, localResource.Password);
                    }
                }


            }
        }

        private void OnLoadingRequest(bool Show, string Message)
        {
            if (Show)
                ShowLoading(Message);
            else
                HideLoading();

        }

        private void TransitionBox_TransitionEnded(object sender, Transitionals.Controls.TransitionEventArgs e)
        {
            if (TransitionBox.Content is IHasTitle)
            {
                lblCtrlTitle.Text = ((IHasTitle)TransitionBox.Content).Title;
            }
        }

        private void UCChangePassword_OnResult(UCChangePassword sender, string oldPassword, string newpassword, bool cancel)
        {

            if (!cancel)
            {
                if (string.IsNullOrEmpty(oldPassword))
                {
                    ShowMessage("رمز عبور فعلی را وارد کنید");
                }
                else if (newpassword.Length < 3)
                {
                    ShowMessage("طول رمز عبور جدید کوتاه است ");
                }
                else if (oldPassword!=gInfo.Password)
                {
                    ShowMessage("رمز فعلی صحیح نیست");
                }
                else if(oldPassword==newpassword)
                {
                    ShowMessage("رمز فعلی و رمز جدید یکسان است");
                }
                else
                {
                    ShowMessage("");
                    RunAsync(() =>
                    {
                        try
                        {
                            var ret = serviceController.ChangePassword(gInfo.Username, oldPassword, newpassword);
                            if (ret.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                if (ret.ResponseData.IsSuccess)
                                {
                                    ShowMessage("تغییر رمز با موفقیت انجام شد", true);
                                    gInfo.Password = newpassword;
                                    uCChangePassword.ResetInput();
                                    if (IsRememberChecked)
                                        localResource.SaveConfig(localResource.GetConfig(), newpassword);
                                }
                                else
                                {
                                    ShowMessage(string.IsNullOrEmpty(ret.ResponseData.ErrorMessage)?"عملیات تغییر رمز با خطا مواجه شد": ret.ResponseData.ErrorMessage);
                                }

                            }
                            else
                                ShowMessage("خطا در فراخوانی سرویس");
                        }
                        catch(Exception ex)
                        {
                            ShowMessage(ex.Message);
                        }
                });
                }
            }
            else
            {
                ShowPreviousControl();
            }
        }
        void ShowPreviousControl()
        {
            ShowControl(lastControl);
        }
        GeoIp GetIPInfo()
        {
            if (IpInfo == null)
            {
                var res = serviceController.GetIpInfo();
                if (res.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    IpInfo = res.ResponseData;
                }
            }
            return IpInfo;
        }
        private void LocalResource_onResourceExtracted(object sender, EventArgs e)
        {
            /*
            var res = GetIPInfo();
            if (res!=null)
            {
                AppCenter.SetCountryCode(res.countryCode);
                //var c = new CustomProperties();
                //c.Set("Isp", res.ResponseData.isp)
                //    .Set("As", res.ResponseData.@as);
                //AppCenter.SetCustomProperties(c);
            }
            AppCenter.LogLevel = LogLevel.Verbose;

            //AppCenter.Start("011bf984-b7c4-4fa5-8b76-f96e2184e850", //test
            AppCenter.Start("95f1579c-3553-407d-b84f-e84e062a00ff",
                typeof(Analytics), typeof(Crashes));

            */
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var miscPath = System.IO.Path.Combine(localResource.TempPath, "misc");
            var resourcePath = System.IO.Path.Combine(miscPath, args.Name.Split(',')[0]);
           
            if (!string.IsNullOrEmpty(resourcePath))
                return Assembly.LoadFrom(resourcePath + ".dll");
            return null;
        }

        private void Proxifier_onResult(bool connected, string message)
        {
            HideLoading();
            Dispatcher.Invoke((Action)(() =>
           {
               ProcessConnectionResult(connected, message);
           }));
        }

        //private void ManagementListener_onDataReceived(System.Net.Sockets.TcpClient client, string data)
        //{
        //    if (CurrentService is IManagementSupport)
        //    {
        //        string ret = ((IManagementSupport)CurrentService).ManagementDataProccess(data);
        //        if (!string.IsNullOrEmpty(ret))
        //        {
        //            ManagementListener.Write(ret, client);
        //        }
        //    }
        //}
        private void UCLoading_OnCancelRequest(object sender, EventArgs e)
        {
            UCUserInfo_OnDisconnectRequest(sender, e);
            HideLoading();
        }

        private async void UCUserInfo_OnDisconnectRequest(object sender, EventArgs e)
        {
            // Accept the user's intent immediately, without exposing cleanup details.
            long version = Interlocked.Increment(ref connectionRequestVersion);
            UnRegiserVpnService();
            HideLoading();
            if (IsUserLogin)
            {
                ShowControl(uCServerList);
                ShowMessage("");
            }
            await ApplyConnectionRequestAsync(null, null, version);
        }

        private void UCUserInfo_OnChangeServerRequest(object sender, EventArgs e)
        {
            UCUserInfo_OnDisconnectRequest(sender, e);
        }

        private async void UCServerList_OnConnectRequest(UCServerList sender, IVPNService service, string protocol)
        {
            if (isUpdateAvailable)
            {
                ShowMessage("Please Upddate Program now");
                return;
            }

            long version = Interlocked.Increment(ref connectionRequestVersion);
            UnRegiserVpnService();
            // Acknowledge Connect immediately, including time spent waiting for cleanup.
            // The same loading view remains visible until this request completes or is cancelled.
            ShowLoading("در حال اتصال به سرویس");
            await ApplyConnectionRequestAsync(service, protocol, version);
        }

        private async Task ApplyConnectionRequestAsync(IVPNService next, string protocol, long version)
        {
            await connectionRequestGate.WaitAsync();
            try
            {
                if (version != Interlocked.Read(ref connectionRequestVersion)) return;
                var previous = gInfo.CurrentService;
                if (previous != null)
                {
                    // Disconnect drains the previous startup too, not just its current Core.
                    await Task.Run(() => previous.Disconnect());
                    proxifier.Detach();
                    if (ReferenceEquals(gInfo.CurrentService, previous))
                        gInfo.CurrentService = null;
                }

                // Several clicks may arrive during cleanup. Only the latest one starts.
                if (version != Interlocked.Read(ref connectionRequestVersion) || next == null || !IsUserLogin)
                    return;

                gInfo.CurrentService = next;
                RegiserVpnService();
                await Task.Run(() => next.Connect(protocol));
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                if (version == Interlocked.Read(ref connectionRequestVersion))
                {
                    UnRegiserVpnService();
                    HideLoading();
                    if (IsUserLogin) ShowControl(uCServerList);
                    ShowMessage(ex.Message);
                }
            }
            finally
            {
                connectionRequestGate.Release();
            }
        }

        void RegiserVpnService()
        {
            UnRegiserVpnService();
            var service = gInfo.CurrentService;
            if (!service.IsRequirementAvailable())
                localResource.ExtractResource(true);
            long version = Interlocked.Read(ref connectionRequestVersion);
            registeredVpnService = service;
            registeredVpnHandler = (source, connected, port, message) =>
                CurrentService_onConnectDisconnect(source, connected, port, message, version);
            service.onConnectDisconnect += registeredVpnHandler;
        }

        private void CurrentService_onConnectDisconnect(IVPNService service, bool connected,
            int listenPort, string message, long version)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Check on the dispatcher as well: an old callback may already be queued.
                if (version != Interlocked.Read(ref connectionRequestVersion) ||
                    !ReferenceEquals(service, gInfo.CurrentService))
                    return;
                if (!service.IsUsingProxifire || !connected)
                    HideLoading();
                if (connected && service.IsUsingProxifire)
                    proxifier.Attach("127.0.0.1", listenPort,
                        service.ProxifierWithPassword ? gInfo.Username : null,
                        service.ProxifierWithPassword ? gInfo.Password : null,
                        service.ProxyType, service.ProxifierRuleType);
                else
                    ProcessConnectionResult(connected, message);
            }));
        }
        void ProcessConnectionResult(bool connected,string Message)
        {
            if (connected)
            {
                ShowMessage("");
                gInfo.ConnectionTime = DateTime.Now;
                ShowControl(uCUserInfo);                

            }
            else
            {
                        
                proxifier.Detach();
                UnRegiserVpnService();
                if (IsUserLogin)
                {
                    ShowMessage(Message);
                    ShowControl(uCServerList);
                }
            }
        }
        void ShowControl(object ctrl)
        {
            txtVersion.Visibility = ReferenceEquals(ctrl, uCLogin) ? Visibility.Collapsed : Visibility.Visible;
            btnSettings.Visibility = Visibility.Collapsed;
            accountMenu.IsEnabled = IsUserLogin;
            panelHeaderIcons.Visibility = ReferenceEquals(ctrl, uCServerList) ? Visibility.Visible : Visibility.Collapsed;

            if (TransitionBox.Content == null || !TransitionBox.Content.Equals(ctrl))
            {
                lblCtrlTitle.Text = "";
                ShowMessage("");
                lastControl = TransitionBox.Content;
                if (ReferenceEquals(ctrl, uCServerList))
                {
                    // The server list is the result of login: reveal it immediately
                    // instead of adding a decorative transition to the user's wait.
                    var transition = TransitionBox.Transition;
                    try
                    {
                        TransitionBox.Transition = null;
                        TransitionBox.Content = ctrl;
                        // The new server-list design has no header title slot (the
                        // search field replaces it) -- leave lblCtrlTitle blank here.
                    }
                    finally
                    {
                        TransitionBox.Transition = transition;
                    }
                }
                else
                {
                    TransitionBox.Content = ctrl;
                }
                currentHeaderOwner = ctrl;
                RefreshHeaderIcons();
            }
        }

        public void SetHeaderIcons(object owner, IEnumerable<HeaderIconRegistration> icons)
        {
            if (owner == null) return;
            var list = icons == null ? new List<HeaderIconRegistration>() : icons.Where(x => x != null).ToList();
            headerIconMap[owner] = list;
            if (ReferenceEquals(currentHeaderOwner, owner))
            {
                RenderHeaderIcons(list);
            }
        }

        public void ClearHeaderIcons(object owner)
        {
            if (owner == null) return;
            if (headerIconMap.Remove(owner) && ReferenceEquals(currentHeaderOwner, owner))
            {
                RenderHeaderIcons(null);
            }
        }

        private void RefreshHeaderIcons()
        {
            if (currentHeaderOwner != null && headerIconMap.TryGetValue(currentHeaderOwner, out var icons))
            {
                RenderHeaderIcons(icons);
            }
            else
            {
                RenderHeaderIcons(null);
            }
        }

        private void RenderHeaderIcons(List<HeaderIconRegistration> icons)
        {
            panelHeaderIcons.Children.Clear();
            if (icons == null || icons.Count == 0) return;

            foreach (var icon in icons)
            {
                var label = new Label
                {
                    Style = (Style)FindResource("LabelButton"),
                    Content = string.IsNullOrEmpty(icon.Icon)
                        ? (object)new System.Windows.Shapes.Path {
                            Data = (Geometry)FindResource(icon.ToolTip.Contains("Shield") ? "IconShield" : "IconGear"),
                            Stroke = (Brush)FindResource("IconStrokeBrush"), StrokeThickness = 1.7,
                            Width = 18, Height = 18, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight }
                        : icon.Icon,
                    Background = null,
                    // Dark icons on the opaque light header.
                    Foreground = (Brush)FindResource("IconStrokeBrush"),
                    FontFamily = (FontFamily)FindResource("fa_ProLight"),
                    FontSize = 16,
                    Width = 30,
                    Height = 30,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = icon.ToolTip,
                };
                ToolTipService.SetInitialShowDelay(label, 400);
                ToolTipService.SetShowDuration(label, 2000);
                ToolTipService.SetBetweenShowDelay(label, 10000);

                var handler = icon.OnClick;
                if (handler != null)
                {
                    label.PreviewMouseDown += (s, e) => handler();
                }

                panelHeaderIcons.Children.Add(label);
            }
        }
        void ShowNotifiy(string Title, string Message)
        {
            notify.Visible = true;
            notify.ShowBalloonTip(5000, Title, Message, System.Windows.Forms.ToolTipIcon.Info);
        }
        void ShowMessage(string Message, bool success = false)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                
                if (!string.IsNullOrEmpty(Message))
                {
                    lblErrorMessage.Foreground = success ? Brushes.DarkGreen : Brushes.Red;
                    //ShowNotifiy("Conection Status", Message);
                    if (Message.Contains("The remote name could not be resolved: 'apichcek-p.isdm.ir'"))
                        lblErrorMessage.Text = "وب سرویس اعتبارسنجی در دسترس نیست";
                    else
                    if (Message.Length < 60)
                        lblErrorMessage.Text = Message;
                    else
                    {
                        lblErrorMessage.Text = "برقراری ارتباط با خطا مواجه شد";
                        LogHelper.WriteLog(Message);
                    }
                    lblErrorMessage.Visibility = Visibility.Visible;
                }
                else
                {
                    lblErrorMessage.Visibility = Visibility.Hidden;

                }
            }));
            
            
        }

        public void ShowUserMessage(string message, bool success = false)
        {
            ShowMessage(message, success);
        }

        public void ShowHintPopup(string message, UIElement target = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                txtHintMessage.Text = message;
                popHint.PlacementTarget = target ?? panelHeaderIcons;
                popHint.IsOpen = true;

                Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => popHint.IsOpen = false);
                });
            });
        }

        void UnRegiserVpnService()
        {
            if (registeredVpnService != null && registeredVpnHandler != null)
                registeredVpnService.onConnectDisconnect -= registeredVpnHandler;
            registeredVpnService = null;
            registeredVpnHandler = null;
        }


        private void UCLogin_OnCredentialEntered(UCLogin sender, string username, string password,bool Remember)
        {
            gInfo.CurrentService = null;
            if (string.IsNullOrEmpty(username))
            {
                ShowMessage("نام کاربری را وارد کنید");
            }
            else if (password.Length < 3)
            {
                ShowMessage("طول رمز عبور کوتاه است ");
            }
            else
            {
                loginUiStopwatch = Stopwatch.StartNew();
                LogHelper.WriteExLog("[LoginPerformance] stage=credentials-submitted elapsedMs=0");
                var uiStopwatch = loginUiStopwatch;
                uCLogin.HideRenewMessage();
                ShowMessage("");
                RunAsync(() =>
                {
                    LogHelper.WriteExLog("[LoginPerformance] stage=worker-start elapsedMs=" + uiStopwatch.ElapsedMilliseconds);
                    Login(username, password, Remember);
                });
            }
        }
        bool Login(string username,string password,bool Remember,bool onlyRenew=false)
        {
            var loginStopwatch = Stopwatch.StartNew();
            try
            {
                IsRememberChecked = Remember;
                lastLoginUsername = username;
                lastLoginPassword = password;
                var res = serviceController.Login2(username, password);
                LogHelper.WriteExLog("[LoginPerformance] stage=auth-complete elapsedMs=" + loginStopwatch.ElapsedMilliseconds);
                if (res.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
                {
                    var devices = TryParseDeviceList(res.ResponseData?.data);
                    ShowDeviceLimitPopup(devices, res.ResponseData?.message);
                    localResource.RemoveConfig();
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.OK)
                {

                    //var acc = res.ResponseData;
                    var acc = res.ResponseData.Decrypted;
                    if (acc.UserAccount != null)
                        acc.UserAccount.Username = username;
                    if (ProcessInfo(acc, password, onlyRenew))
                    {
                        gInfo.ServerResponse = res.ResponseData.DecryptedString;

                        if (Remember)
                            localResource.SaveConfig(acc, password);
                        else
                            localResource.Password = password;
                        return true;
                    }
                }
                else if (!onlyRenew)
                {

                        ShowMessage(res.ResponseData.message);
                    
                }

            }
            catch (Exception ex)
            {
                if (!onlyRenew)
                {
                    ShowMessage("ارتباط با سرور برقرار نیست");
                    LogHelper.WriteLog(ex);
                }
            }
            finally
            {
                LogHelper.WriteExLog("[LoginPerformance] stage=complete elapsedMs=" + loginStopwatch.ElapsedMilliseconds);
            }
            return false;
        }
        private List<DeviceInfo> TryParseDeviceList(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return new List<DeviceInfo>();
            try
            {
                return data.JsonDeserilize<List<DeviceInfo>>();
            }
            catch
            {
                return new List<DeviceInfo>();
            }
        }

        private void ShowDeviceLimitPopup(List<DeviceInfo> devices, string message)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                if (deviceLimitWindow == null || !deviceLimitWindow.IsVisible)
                {
                    deviceLimitWindow = new DeviceLimitWindow();
                    deviceLimitWindow.Owner = this;
                    deviceLimitWindow.OnRemoveRequested += DeviceLimitWindow_OnRemoveRequested;
                    deviceLimitWindow.Closed += DeviceLimitWindow_Closed;
                }
                deviceLimitWindow.SetDevices(devices, message);
                deviceLimitWindow.Show();
                deviceLimitWindow.Activate();
            }));
        }

        private void DeviceLimitWindow_OnRemoveRequested(DeviceLimitWindow sender, DeviceInfo device)
        {
            if (device == null)
                return;

            ShowMessage("");
            RunAsync(() =>
            {
                try
                {
                    var res = serviceController.RemoveToken(
                        lastLoginUsername,
                        lastLoginPassword,
                        device.device_name,
                        device.device_token);

                    if (res != null && res.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Dispatcher.Invoke((Action)(() =>
                        {
                            if (deviceLimitWindow != null)
                                deviceLimitWindow.Close();
                        }));
                        Login(lastLoginUsername, lastLoginPassword, IsRememberChecked);
                    }
                    else
                    {
                        Dispatcher.Invoke((Action)(() =>
                        {
                            deviceLimitWindow?.ShowError(res?.ResponseData?.message ?? "خطا در حذف دستگاه");
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke((Action)(() =>
                    {
                        deviceLimitWindow?.ShowError("خطا در حذف دستگاه");
                    }));
                    LogHelper.WriteLog(ex);
                }
            });
        }

        private void DeviceLimitWindow_Closed(object sender, EventArgs e)
        {
            if (deviceLimitWindow != null)
            {
                deviceLimitWindow.OnRemoveRequested -= DeviceLimitWindow_OnRemoveRequested;
                deviceLimitWindow.Closed -= DeviceLimitWindow_Closed;
                deviceLimitWindow = null;
            }
        }
        SettingInfo GetSetting()
        {
            try
            {
                var res = serviceController.GetSettings();
                if (res.StatusCode == System.Net.HttpStatusCode.OK &&
                    res.ResponseData?.data != null)
                    return res.ResponseData.data;
            }
            catch
            { 
            }
            return null;
        }
        private bool ProcessInfo(AccountInfoEx acc, string password, bool onlyRenew = false)
        {
            var processStopwatch = Stopwatch.StartNew();
            // Prefer settings already returned by Login response to avoid an extra
            // network call during initial login latency.
            var setting = acc.Settings ?? GetSetting();
            LogHelper.WriteExLog("[LoginPerformance] stage=settings-ready processElapsedMs=" + processStopwatch.ElapsedMilliseconds);
            if (setting != null)
                acc.Settings = setting;

            if (password != null)
                gInfo.Import(acc, password, localResource.TempPath);
            else
                gInfo.TempPath = localResource.TempPath;

            if (!CheckUpdateExist())
            {


                gInfo.settings = acc.Settings;
                 
                if (/*res.StatusCode == System.Net.HttpStatusCode.OK &&*/ acc.UserAccount.Status == "OK"|| acc.UserAccount.Status == "FirstUse" || acc.UserAccount.Status==null)
                {
                    //TODO: Remove This this line And remove pwergo test config from resource
                    //var server = acc.Servers[0];
                    //server.Service = "sslProxy";
                    //acc.Servers.Add(server);
                    /////////
                    IsUserLogin = true;
                    //serviceFactory.RenewServiceList(acc.Servers);
                    serviceFactory.RenewServiceList(acc.groups);
                    LogHelper.WriteExLog("[LoginPerformance] stage=services-ready processElapsedMs=" + processStopwatch.ElapsedMilliseconds);
                    if (!onlyRenew &&!isUpdateAvailable)
                    {
                        DisconnectAll();
                        proxifier.Detach();
                        LogHelper.WriteExLog("[LoginPerformance] stage=cleanup-complete processElapsedMs=" + processStopwatch.ElapsedMilliseconds);
                        mainTimer.Change(1000,1000);
                        Dispatcher.Invoke((Action)(() =>
                        {
                            btnSettings.Visibility = Visibility.Visible;
                            txtUsername.Text = gInfo.Username;
                            ShowMessage("");                            
                            ShowControl(uCServerList);
                            LogHelper.WriteExLog("[LoginPerformance] stage=list-content-set processElapsedMs=" + processStopwatch.ElapsedMilliseconds);
                        }));
                        /*
                        var ip = GetIPInfo();
                        if (ip != null)
                        {
                            var properties = new Dictionary<string, string>
                            {
                               // { "Username", acc.UserAccount.Username },
                                { "Isp", ip.isp },
                                { "As", ip.@as },
                              //  { "city",ip.city }
                            };

                            Analytics.TrackEvent("UserInfo", properties);
                        }*/
                    }
                    return true;
                }
                else if(acc.UserAccount.Status == "Expired")
                {
                    RechareLogout(false);
                }
                else if(acc.UserAccount.Status == "AuthServerError" && !onlyRenew)
                {
                    ShowMessage("ارتباط با سرور اعتبار سنجی مقدور نیست");
                }
                else
                {
                    Logout("نام کاربری یا رمز عبور صحیح نیست",false);
                    /*localResource.RemoveConfig();
                    ShowMessage("نام کاربری یا رمز عبور صحیح نیست");*/
                }
                return false;
            }
            else
            {
                DisconnectAll();
                proxifier.Detach();
                Dispatcher.Invoke((Action)(() =>
                {
                    uCUpdate.SetInfos(Version.Parse(gInfo.settings.last_version.version_number), gInfo.settings.last_version.version_url, gInfo.settings.last_version.update_change_log.ToString());
                    ShowControl(uCUpdate);                    
                }));
                return true;

            }
        }

        private void RechareLogout(bool resetInput=true)
        {

            DisconnectAll();
            proxifier.Detach();
            var rechargeUrl = gInfo.settings.setting.shop_url;
#if !_RESELLER
            if (!string.IsNullOrEmpty(rechargeUrl))
            {
                Dispatcher.Invoke((Action)(() => uCLogin.ShowRenewMessage(rechargeUrl)));
            }
#endif
         
            Dispatcher.Invoke((Action)(() =>
            {
                
                 
                Logout("اعتبار اکانت شما پایان یافته است لطفا تمدید نمایید",resetInput);
                uCLogin.SetUserName(gInfo.Username);

            }));
            localResource.RemoveConfig();
        }

        private bool CheckUpdateExist()
        {
            bool ret = false;
            var lastVersion = gInfo?.settings?.last_version;
            if (lastVersion != null)
            {
                // Only prompt when the advertised version is actually newer than what
                // is running. Without this comparison a last_version left over in the
                // cached seed.set keeps asking an already-updated app to update on
                // every launch. Unknown formats fall back to the previous behavior so
                // a real update is never hidden.
                if (Version.TryParse(lastVersion.version_number, out var latest)
                    && Version.TryParse(Assembly.GetExecutingAssembly().GetName().Version.ToString(), out var current))
                {
                    ret = latest > current;
                }
                else
                {
                    ret = true;
                }
            }
            isUpdateAvailable = ret;
            return ret;
        }
        private void DisconnectAll()
        {
            Interlocked.Increment(ref connectionRequestVersion);
            UnRegiserVpnService();
            if (serviceFactory.Services != null)
                serviceFactory.Services.GroupBy(x => x.GetType())
                   .Select(grp => grp.First())
                   .ToList().ForEach(s => s.DisconnectAll());
        }

        void RunAsync(Action action, bool showloading = true)
        {
            if (showloading)
                Dispatcher.Invoke((Action)(() => ShowLoading("")));
            action.BeginInvoke((AsyncCallback)((ar) =>
            {
                if (showloading)
                    HideLoading();
            }), null);

        }
       
        private void ShowLoading(string Messgae)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                ShowMessage("");
                uCLoading.SetMessage(Messgae);
                uCLoading.Visibility = Visibility.Visible;

            }));
        }
        private void HideLoading()
        {

            this.Dispatcher.Invoke((Action)(() =>
            {
                uCLoading.Visibility = Visibility.Hidden;
                var uiStopwatch = loginUiStopwatch;
                if (uiStopwatch == null)
                    return;

                // ContextIdle runs after the queued layout/render work. This measures
                // UI readiness, not the physical display's presentation timestamp.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
                    (Action)(() =>
                    {
                        if (!ReferenceEquals(loginUiStopwatch, uiStopwatch))
                            return;
                        if (ReferenceEquals(TransitionBox.Content, uCServerList)
                            && uCServerList.IsVisible && !uCLoading.IsVisible)
                        {
                            LogHelper.WriteExLog("[LoginPerformance] stage=list-ui-ready elapsedMs="
                                + uiStopwatch.ElapsedMilliseconds);
                        }
                        else
                        {
                            LogHelper.WriteExLog("[LoginPerformance] stage=loading-hidden-without-list elapsedMs="
                                + uiStopwatch.ElapsedMilliseconds);
                        }
                        loginUiStopwatch = null;
                    }));
            }));
        }
        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IRSpeedyVPN.Common.WindowDrag.Begin(this, e);
        }

        private void Minimize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
            notify.Visible = true;
        }

        private void Exit_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isUpdateAvailable)
                Notify_Exit(sender, e);
            else
            {
                this.Visibility = Visibility.Hidden;
                notify.Visible = true;
            }
        }

        private void AccountMenu_Click(object sender, RoutedEventArgs e) { if (IsUserLogin) btnSettings_MouseDown(sender, null); }

        private void btnSettings_MouseDown(object sender, MouseButtonEventArgs e)
        {

            uCChangePassword.ResetInput();
            //                  TransitionBox.Transition = new Transitionals.Transitions.TranslateTransition();
            ShowControl(uCChangePassword);
            
        }

        private void Logout_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Logout("");
        }
        void Logout(string Message,bool resetInput=true)
        {

            Interlocked.Increment(ref connectionRequestVersion);
            UnRegiserVpnService();
            IsUserLogin = false;
            mainTimer.Change(int.MaxValue,int.MaxValue);
            if (gInfo?.CurrentService != null)
                gInfo.CurrentService.Disconnect();
            localResource.RemoveConfig();
            Dispatcher.Invoke((Action)(() =>
            {
                btnSettings.Visibility = Visibility.Collapsed;
                txtUsername.Text = "";
                ShowMessage(Message);
                if (resetInput)
                    uCLogin.ResetInput();
                ShowControl(uCLogin);              
            }));
        }
        private void btnWeb_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
#if !_RESELLER
            try
            {
                /*
                TransitionBox.Transition = new Transitionals.Transitions.RotateTransition();
                TransitionBox.Content = uCServerList;*/
                var val = gInfo.settings.setting.shop_url;
            if (!string.IsNullOrEmpty(val))
                System.Diagnostics.Process.Start(val);
            }
            catch { }
#endif
        }
        private void btnSupport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
#if !_RESELLER
            try
            {
                var val = gInfo.settings.setting.support_url;
                if (!string.IsNullOrEmpty(val))
                    System.Diagnostics.Process.Start(val);
            }
            catch { }

            /*
            TransitionBox.Transition = new Transitionals.Transitions.TranslateTransition();
            TransitionBox.Content = uCUserInfo;*/
#endif
        }

        private void btnRenew_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
          //  TransitionBox.Transition = new Transitionals.Transitions.TranslateTransition();
          

        }
    }
}
