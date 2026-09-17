using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Resource;
using Shadowsocks.Controller;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IRSpeedyVPN
{
    public partial class MainWindow
    {
        private int fastStartupPrepared;
        private int deferredAutoLoginVisible;
        private Timer sessionMaintenanceTimer;
        private int sessionMaintenanceTick;
        private int serverRefreshBusy;
        private string sessionMaintenanceUser;

        private const int SessionMaintenancePeriodMs = 30 * 60 * 1000;
        private const int ServerRefreshTicks = 1; // one maintenance pass every 30 minutes

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
            uCServerList.Loaded += FastStartup_ServerListLoaded;
            uCLogin.OnCredentialEntered += UCLogin_OnCredentialEntered;
            uCLogin.Loaded += FastStartup_LoginLoaded;
            uCUserInfo.OnChangeServerRequest += UCUserInfo_OnChangeServerRequest;
            uCUserInfo.OnDisconnectRequest += UCUserInfo_OnDisconnectRequest;
            uCUserInfo.OnLoadingRequest += OnLoadingRequest;
            uCLoading.OnCancelRequest += UCLoading_OnCancelRequest;
            uCChangePassword.OnResult += UCChangePassword_OnResult;
            proxifier.onResult += Proxifier_onResult;
            Closing += FastStartup_Closing;

            ShowControl(uCLogin);
            TransitionBox.Transition = null;
            TransitionBox.TransitionEnded += TransitionBox_TransitionEnded;

#if _PREMIUM
            lblPremium.Visibility = Visibility.Visible;
#endif

            // This hint is only an HKCU read / oneclick.txt existence check and does not
            // require WMI or ResourceManager. Put the auto-login overlay in place BEFORE
            // the first window paint so remembered users never see blank credentials.
            if (HasRememberedLoginHint())
                ShowDeferredAutoLogin();

            ContentRendered += FastStartup_ContentRendered;
        }

        private void FastStartup_ContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= FastStartup_ContentRendered;

            // Never put the first paint behind filesystem/WMI/network work.
            Task.Run((Action)CompleteDeferredStartup);
        }

        private static bool HasRememberedLoginHint()
        {
            try
            {
                return File.Exists("./oneclick.txt")
                    || !string.IsNullOrWhiteSpace(RegHelper.GetSettingValue("UserInfo"));
            }
            catch
            {
                return false;
            }
        }

        private void ShowDeferredAutoLogin()
        {
            if (Interlocked.Exchange(ref deferredAutoLoginVisible, 1) != 0)
                return;

            ShowLoading("در حال ورود خودکار...");
        }

        private void HideDeferredAutoLogin()
        {
            if (Interlocked.Exchange(ref deferredAutoLoginVisible, 0) == 0)
                return;

            Dispatcher.BeginInvoke((Action)(() => uCLoading.Visibility = Visibility.Hidden));
        }

        /// <summary>
        /// A valid remembered account is trusted at startup. No Login2/GetSettings call is
        /// made on the critical startup path; the encrypted cached groups/settings are used
        /// immediately and credentials/session are validated by the 30-minute maintenance
        /// task. Manual login behavior is unchanged.
        /// </summary>
        private bool TryApplyCachedAccount(AccountInfoEx account, string password)
        {
            try
            {
                if (account?.UserAccount == null || account.groups == null)
                    return false;

                var status = account.UserAccount.Status;
                var validStatus = status == "OK" || status == "FirstUse" || status == null;
                if (!validStatus)
                    return false;

                if (account.UserAccount.ExpiryDate != null
                    && account.UserAccount.ExpiryDate.Value < DateTime.Now)
                    return false;

                IsRememberChecked = true;
                lastLoginUsername = account.UserAccount.Username;
                lastLoginPassword = password;

                gInfo.Import(account, password, localResource.TempPath);
                gInfo.settings = account.Settings;

                // Preserve the existing update-enforcement behavior, but use only the
                // cached setting instead of calling GetSettings during startup.
                if (CheckUpdateExist())
                {
                    if (gInfo.settings?.last_version != null)
                    {
                        uCUpdate.SetInfos(
                            Version.Parse(gInfo.settings.last_version.version_number),
                            gInfo.settings.last_version.version_url,
                            gInfo.settings.last_version.update_change_log.ToString());
                        ShowControl(uCUpdate);
                        return true;
                    }
                    return false;
                }

                IsUserLogin = true;
                serviceFactory.RenewServiceList(account.groups);

                // Disable the legacy 10-minute Login() renew loop. Session maintenance
                // below validates credentials/session and refreshes servers every 30 min.
                mainTimer.Change(Timeout.Infinite, Timeout.Infinite);

                btnSettings.Visibility = Visibility.Visible;
                txtUsername.Text = gInfo.Username;
                ShowMessage("");
                ShowControl(uCServerList);
                StartSessionMaintenance();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                return false;
            }
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
                            try
                            {
                                IsRememberChecked = true;
                                ProcessInfo(localAccount, password);
                            }
                            finally
                            {
                                HideDeferredAutoLogin();
                            }
                        }));
                    }
                    else
                    {
                        HideDeferredAutoLogin();
                    }
                    return;
                }

                // Device fingerprint + decrypting the remembered account happen on this
                // worker. GetConfig already verifies username consistency and local expiry.
                var account = resource.GetConfig();
                if (account == null)
                {
                    HideDeferredAutoLogin();
                    return;
                }

                var username = account.UserAccount?.Username;
                var passwordValue = resource.Password;
                if (string.IsNullOrWhiteSpace(username))
                {
                    HideDeferredAutoLogin();
                    return;
                }

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    // Do not race an explicit login the user already started while the
                    // deferred remembered-account read was running.
                    if (IsUserLogin || !string.IsNullOrEmpty(lastLoginUsername))
                    {
                        HideDeferredAutoLogin();
                        return;
                    }

                    // Keep credentials ready underneath the overlay. If the local cache
                    // cannot be applied, the user gets the populated manual-login form.
                    uCLogin.SetUserPassword(username, passwordValue);
                    TryApplyCachedAccount(account, passwordValue);
                    HideDeferredAutoLogin();
                }));
            }
            catch (Exception ex)
            {
                HideDeferredAutoLogin();
                LogHelper.WriteLog(ex);
            }
        }

        private void FastStartup_ServerListLoaded(object sender, RoutedEventArgs e)
        {
            if (IsUserLogin)
                StartSessionMaintenance();
        }

        private void FastStartup_LoginLoaded(object sender, RoutedEventArgs e)
        {
            if (IsUserLogin)
                return;

            sessionMaintenanceUser = null;
            Interlocked.Exchange(ref sessionMaintenanceTick, 0);
            try
            {
                sessionMaintenanceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
            }
        }

        private void StartSessionMaintenance()
        {
            // ProcessInfo from a manual login still starts the legacy one-second timer.
            // Stop it immediately so it cannot perform the old Login() call at 10 minutes.
            try
            {
                mainTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
            }

            var username = gInfo?.Username ?? string.Empty;
            if (!string.Equals(sessionMaintenanceUser, username, StringComparison.Ordinal))
            {
                sessionMaintenanceUser = username;
                Interlocked.Exchange(ref sessionMaintenanceTick, 0);
            }

            if (sessionMaintenanceTimer == null)
            {
                sessionMaintenanceTimer = new Timer(
                    SessionMaintenanceCallback,
                    null,
                    SessionMaintenancePeriodMs,
                    SessionMaintenancePeriodMs);
            }
            else
            {
                sessionMaintenanceTimer.Change(
                    SessionMaintenancePeriodMs,
                    SessionMaintenancePeriodMs);
            }
        }

        private void SessionMaintenanceCallback(object state)
        {
            if (!IsUserLogin || LoadLocal)
                return;

            try
            {
                if (Interlocked.Increment(ref sessionMaintenanceTick) < ServerRefreshTicks)
                    return;

                Interlocked.Exchange(ref sessionMaintenanceTick, 0);
                ValidateSessionAndRefreshServerList();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        private static bool IsExplicitSessionFailure(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Unauthorized
                || statusCode == HttpStatusCode.Forbidden
                || statusCode == HttpStatusCode.NotAcceptable;
        }

        private void LogoutInvalidSession(string message)
        {
            if (!IsUserLogin)
                return;

            try
            {
                sessionMaintenanceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
            }

            Logout(
                string.IsNullOrWhiteSpace(message)
                    ? "نشست کاربری معتبر نیست، لطفاً دوباره وارد شوید"
                    : message,
                false);
        }

        /// <summary>
        /// Every 30 minutes Login2 validates username, password and the device/session
        /// token in the same authenticated server-list request. Explicit auth/session
        /// failures log the user out. Network/server failures are treated as transient and
        /// never cause a false logout. A valid response refreshes the server list without
        /// disconnecting the live VPN session.
        /// </summary>
        private void ValidateSessionAndRefreshServerList()
        {
            if (Interlocked.Exchange(ref serverRefreshBusy, 1) != 0)
                return;

            try
            {
                var username = gInfo?.Username;
                var password = gInfo?.Password;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                {
                    LogoutInvalidSession("نشست کاربری معتبر نیست، لطفاً دوباره وارد شوید");
                    return;
                }

                var response = serviceController.Login2(username, password);
                if (response == null)
                    return;

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    // Do not log out on timeouts, endpoint outages, 5xx responses, etc.
                    // Only statuses that explicitly mean authentication/session rejection
                    // terminate the local login session.
                    if (IsExplicitSessionFailure(response.StatusCode))
                        LogoutInvalidSession(response.ResponseData?.message);
                    return;
                }

                var account = response.ResponseData?.Decrypted;
                if (account?.UserAccount == null || account.groups == null)
                    return;

                account.UserAccount.Username = username;

                if (account.UserAccount.ExpiryDate != null
                    && account.UserAccount.ExpiryDate.Value < DateTime.Now)
                {
                    RechareLogout(false);
                    return;
                }

                var status = account.UserAccount.Status;
                if (status == "Expired")
                {
                    RechareLogout(false);
                    return;
                }

                // AuthServerError means the validation service itself is unavailable;
                // it is not proof that the user's credentials/session are invalid.
                if (status == "AuthServerError")
                    return;

                var validStatus = status == "OK" || status == "FirstUse" || status == null;
                if (!validStatus)
                {
                    LogoutInvalidSession("نام کاربری یا رمز عبور صحیح نیست");
                    return;
                }

                // Login2 may not carry the separate settings payload. Keep the currently
                // active settings so the cache remains complete without a GetSettings call.
                if (account.Settings == null)
                    account.Settings = gInfo.settings;

                var currentService = gInfo.CurrentService;

                // Replace only the list/account cache. Keep CurrentService pointing at the
                // exact live object so a successful 30-minute refresh cannot disconnect or
                // replace the currently running VPN session.
                Dispatcher.Invoke((Action)(() =>
                {
                    serviceFactory.RenewServiceList(account.groups);
                    gInfo.Import(account, password, localResource.TempPath);
                    gInfo.CurrentService = currentService;
                    gInfo.ServerResponse = response.ResponseData.DecryptedString;
                    uCServerList.RefreshServicesFromFactory();
                }));

                if (IsRememberChecked)
                    localResource.SaveConfig(account, password);
            }
            catch (Exception ex)
            {
                // DNS/timeout/failover/decryption/server errors are transient here. Never
                // log out a valid user unless the API explicitly rejected credentials,
                // session/device token, or returned an invalid account status.
                LogHelper.WriteLog(ex);
            }
            finally
            {
                Interlocked.Exchange(ref serverRefreshBusy, 0);
            }
        }

        private void FastStartup_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                sessionMaintenanceTimer?.Dispose();
            }
            catch
            {
            }

            // A 30-minute refresh replaces ServiceFactory.Services while a connected
            // CurrentService can remain the original live object. Ensure that exact live
            // object is also stopped during real application shutdown.
            try
            {
                gInfo?.CurrentService?.Disconnect();
            }
            catch
            {
            }
        }
    }
}

