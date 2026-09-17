using IRSpeedyVPN.Authentication;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCLogin : UserControl, IHasTitle
    {
        private string renewLink;
        private bool synchronizingPassword;
        private bool editingPassword;
        public string Title => string.Empty;
        public LoginViewModel ViewModel { get; private set; }

        public UCLogin()
        {
            InitializeComponent();
            // Fail closed until MainWindow installs the real account workflow.
            ConfigureAuthentication(new DelegateAuthService((u, p) => Task.FromResult(
                AuthResult.Failed("ورود هنوز آماده نیست. چند لحظه دیگر تلاش کنید."))), () => { });
        }

        // For an isolated preview, explicitly inject new StubAuthService() here.
        public void ConfigureAuthentication(IAuthService service, Action navigateToMainShell)
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
            }
            ViewModel = new LoginViewModel(service, navigateToMainShell, RecoverPassword);
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = ViewModel;
            UpdatePasswordVisibility();
        }

        public void ShowRenewMessage(string link) { renewLink = link; boxRenew.Visibility = Visibility.Visible; }
        public void HideRenewMessage() { boxRenew.Visibility = Visibility.Collapsed; }
        public void SetUserName(string username) { ViewModel.Username = username; }
        public void SetUserPassword(string username, string password)
        {
            ViewModel.Username = username;
            using (var secure = ToSecureString(password)) ViewModel.Password = secure;
            ViewModel.RememberMe = true;
        }
        public void ResetInput()
        {
            ViewModel.Username = string.Empty;
            ViewModel.ClearPassword();
            ViewModel.RememberMe = true;
            ViewModel.ErrorMessage = string.Empty;
        }

        private static SecureString ToSecureString(string value)
        {
            var secure = new SecureString();
            foreach (char c in value ?? string.Empty) secure.AppendChar(c);
            secure.MakeReadOnly();
            return secure;
        }
        private void Password_Changed(object sender, RoutedEventArgs e)
        {
            if (synchronizingPassword || ViewModel == null) return;
            editingPassword = true;
            try { using (var secure = txtPassword.SecurePassword) ViewModel.Password = secure; }
            finally { editingPassword = false; }
        }
        private void VisiblePassword_Changed(object sender, TextChangedEventArgs e)
        {
            if (synchronizingPassword || ViewModel == null || !ViewModel.IsPasswordVisible) return;
            editingPassword = true;
            try { using (var secure = ToSecureString(txtPasswordShow.Text)) ViewModel.Password = secure; }
            finally { editingPassword = false; }
        }
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoginViewModel.IsPasswordVisible)
                || (e.PropertyName == nameof(LoginViewModel.Password) && !editingPassword))
                UpdatePasswordVisibility();
            if (e.PropertyName == nameof(LoginViewModel.ErrorMessage) && ViewModel.HasError)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    if (!IsVisible || !ViewModel.HasError) return;
                    var peer = UIElementAutomationPeer.FromElement(errorText)
                        ?? UIElementAutomationPeer.CreatePeerForElement(errorText);
                    peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    if (ViewModel.HasUsernameError) txtUsername.Focus();
                    else if (ViewModel.HasPasswordError) txtPassword.Focus();
                }));
            }
        }
        private void UpdatePasswordVisibility()
        {
            synchronizingPassword = true;
            try
            {
                using (var secure = ViewModel.Password)
                {
                    var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
                    try
                    {
                        string value = Marshal.PtrToStringUni(pointer);
                        if (ViewModel.IsPasswordVisible)
                        {
                            txtPasswordShow.Text = value;
                            txtPassword.Clear();
                        }
                        else
                        {
                            txtPassword.Password = value;
                            txtPasswordShow.Clear();
                        }
                    }
                    finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
                }
                txtPassword.Visibility = ViewModel.IsPasswordVisible ? Visibility.Collapsed : Visibility.Visible;
                txtPasswordShow.Visibility = ViewModel.IsPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
                var toggleName = ViewModel.IsPasswordVisible ? "مخفی کردن رمز عبور" : "نمایش رمز عبور";
                AutomationProperties.SetName(passwordToggle, toggleName);
                passwordToggle.ToolTip = toggleName;
            }
            finally { synchronizingPassword = false; }
        }
        private void RecoverPassword()
        {
            var link = AppServices.GlobalInfo?.settings?.setting?.support_url;
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else MessageBox.Show("برای بازیابی رمز عبور با پشتیبانی فروشنده حساب تماس بگیرید.", "بازیابی رمز عبور");
        }
        private void Renew_Click(object sender, RoutedEventArgs e)
        {
            if (!Uri.TryCreate(renewLink, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { ViewModel.ErrorMessage = "باز کردن صفحه تمدید ممکن نشد."; }
        }
        private void Credential_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (ViewModel.LoginCommand.CanExecute(null)) ViewModel.LoginCommand.Execute(null);
        }
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsInteractive) txtUsername.Focus();
        }
    }
}
