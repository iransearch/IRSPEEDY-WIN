using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRSpeedyVPN.UserControls
{
    /// <summary>
    /// Interaction logic for UCLogin.xaml
    /// </summary>
    public partial class UCLogin : UserControl, IHasTitle
    {
        public delegate void UserPasswordEntered(UCLogin sender, string username,string password,bool remember);

        public event UserPasswordEntered OnCredentialEntered;
        string renewLink;

        public string Title => "";

        public UCLogin()
        {
            InitializeComponent();
        }
        public void ShowRenewMessage(string renewLink)
        {
            this.renewLink = renewLink;
            boxRenew.Visibility = Visibility.Visible;
        }
        public void HideRenewMessage()
        {            
            boxRenew.Visibility = Visibility.Collapsed;
        }
        public void SetUserPassword(string username,string password)
        {
            txtUsername.Text = username;
            txtPassword.Password = txtPasswordShow.Text = password;
            chkRemember.IsChecked = true;            
        }
        public void SetUserName(string username)
        {
            txtUsername.Text = username;                        
        }
        //  public string Message { get => lblErrorMessage.Text; set => Dispatcher.Invoke((Action)(() => lblErrorMessage.Text = value)); }

        private void eye_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ShowPassword(true);
        }

        private void eye_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ShowPassword(false);
        }
        void ShowPassword(Boolean show)
        {
            txtPasswordShow.Text = txtPassword.Password;
            txtPassword.Visibility = show ? Visibility.Hidden : Visibility.Visible;
            txtPasswordShow.Visibility = !show ? Visibility.Hidden : Visibility.Visible;
            if (show)
                txtPasswordShow.Focus();
            else
                txtPassword.Focus();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
           
          
            if (OnCredentialEntered != null)
            {
               
               OnCredentialEntered.Invoke(this, txtUsername.Text, txtPassword.Password,chkRemember.IsChecked.Value);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ShowPassword(false);
            UpdatePasswordPlaceholder();
            btnSignup.IsEnabled = GetSignupUri() != null;
            btnSignup.ToolTip = btnSignup.IsEnabled ? "ثبت‌نام" : "برای تهیه حساب با پشتیبانی تماس بگیرید";
            ToolTipService.SetShowOnDisabled(btnSignup, true);
        }
        private static Uri GetSignupUri()
        {
            // Optional deployment setting; never invent a registration destination.
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signup-url.txt");
            try
            {
                Uri uri;
                if (System.IO.File.Exists(path) && Uri.TryCreate(System.IO.File.ReadAllText(path).Trim(), UriKind.Absolute, out uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)) return uri;
            }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
        private void Signup_Click(object sender, RoutedEventArgs e)
        {
            var uri = GetSignupUri();
            if (uri != null) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            ShowPassword(txtPassword.Visibility == Visibility.Visible);
        }
        public void ResetInput()
        {
            chkRemember.IsChecked = true;
            //lblErrorMessage.Text = "";
            txtUsername.Text = "";
            txtPassword.Password = "";
            txtPasswordShow.Text = "";
        }
        private void btnRenew_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(renewLink);
        }

        private void txtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordPlaceholder();
        }

        /// <summary>
        /// The password box has no placeholder of its own, so the "رمز عبور" label
        /// behind it is shown only while both the masked and the revealed field are empty.
        /// </summary>
        void UpdatePasswordPlaceholder()
        {
            if (lblPasswordPlaceholder == null)
                return;

            bool empty = string.IsNullOrEmpty(txtPassword.Password);
            lblPasswordPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

    }
}
