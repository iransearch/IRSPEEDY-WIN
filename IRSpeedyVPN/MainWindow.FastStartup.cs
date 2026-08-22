using IRSpeedyVPN.Common;
using Shadowsocks.Controller;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IRSpeedyVPN
{
    public partial class MainWindow
    {
        private int fastStartupPrepared;

        /// <summary>
        /// Installs the minimum UI/event wiring required for the first Login frame and
        /// marks the legacy Activated handler as already initialized. Heavy runtime,
        /// device-fingerprint and remembered-login work continues after first render.
        /// </summary>
        internal void PrepareFastStartup()
        {
            if (Interlocked.Exchange(ref fastStartupPrepared, 1) != 0)
                return;

            // Window_Activated contains the old synchronous startup path. We replace it
            // here so Activated becomes a no-op while preserving the handler for older
            // entry points that do not opt into PrepareFastStartup().
            Interlocked.Exchange(ref Initialized, 1);

            uCServerList.OnConnectRequest += UCServerList_OnConnectRequest;
            uCServerList.OnLoadingRequest += OnLoadingRequest;
            uCLogin.OnCredentialEntered += UCLogin_OnCredentialEntered;
            uCUserInfo.OnChangeServerRequest += UCUserInfo_OnChangeServerRequest;
            uCUserInfo.OnDisconnectRequest += UCUserInfo_OnDisconnectRequest;
            uCUserInfo.OnLoadingRequest += OnLoadingRequest;
            uCLoading.OnCancelRequest += UCLoading_OnCancelRequest;
            uCChangePassword.OnResult += UCChangePassword_OnResult;
            proxifier.onResult += Proxifier_onResult;

            ShowControl(uCLogin);
            TransitionBox.Transition = new Transitionals.Transitions.RotateTransition
            {
                Direction = Transitionals.Transitions.RotateDirection.Right
            };
            TransitionBox.TransitionEnded += TransitionBox_TransitionEnded;

#if _PREMIUM
            lblPremium.Visibility = Visibility.Visible;
#endif

            ContentRendered += FastStartup_ContentRendered;
        }

        private void FastStartup_ContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= FastStartup_ContentRendered;

            // Never put the first paint behind filesystem/WMI/network work.
            Task.Run((Action)CompleteDeferredStartup);
        }

        private void CompleteDeferredStartup()
        {
            try
            {
                // These are cheap and independent of ResourceManager. Run them directly
                // after first paint instead of leaving the previous system proxy active
                // while hardware/runtime initialization completes.
                try
                {
                    SystemProxy.Disable();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(ex);
                }

                if (File.Exists("./debug.txt"))
                    ShellExecute.HideWindow = false;

                LoadLocal = File.Exists("./oneclick.txt");

                // Program starts this task before showing the window. If a very fast
                // machine reaches ContentRendered first, only this worker waits.
                var resource = AppServices.ResourceManager;

                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                resource.onResourceExtracted += LocalResource_onResourceExtracted;

                // ExtractResource is asynchronous itself. Existing active runtime remains
                // usable immediately; validation/activation proceeds off the UI thread.
                resource.ExtractResource();

                if (LoadLocal)
                {
                    var localAccount = resource.GetLocalConfig();
                    if (localAccount != null)
                    {
                        var password = resource.Password;
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            IsRememberChecked = true;
                            ProcessInfo(localAccount, password);
                        }));
                    }
                    return;
                }

                // Device fingerprint + decrypting the remembered account now happen on
                // this worker. The Login page has already been painted and is interactive.
                var account = resource.GetConfig();
                if (account == null)
                    return;

                var username = account.UserAccount?.Username;
                var passwordValue = resource.Password;
                if (string.IsNullOrWhiteSpace(username))
                    return;

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    // Do not race an explicit login the user already started while the
                    // deferred remembered-account read was running.
                    if (IsUserLogin || !string.IsNullOrEmpty(lastLoginUsername))
                        return;

                    uCLogin.SetUserPassword(username, passwordValue);
                    RunAsync(() =>
                    {
                        if (!Login(username, passwordValue, true))
                        {
                            var fallback = resource.GetConfig();
                            if (fallback != null)
                                ProcessInfo(fallback, resource.Password);
                        }
                    });
                }));
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }
    }
}
