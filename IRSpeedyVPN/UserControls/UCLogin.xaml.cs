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
        private bool passwordVisible;
        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            ShowPassword(!passwordVisible);
        }
        void ShowPassword(bool show)
        {
            if (passwordVisible) txtPassword.Password = txtPasswordShow.Text;
            else txtPasswordShow.Text = txtPassword.Password;
            passwordVisible = show;
            txtPassword.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            txtPasswordShow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            imgEye.Source = (ImageSource)TryFindResource(show ? "eyeSlash" : "eye");
            if (show) txtPasswordShow.Focus(); else txtPassword.Focus();
        }
        private void RecoverPassword_Click(object sender, RoutedEventArgs e)
        {
            var link = AppServices.GlobalInfo?.settings?.setting?.support_url;
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch { MessageBox.Show("باز کردن صفحه پشتیبانی ممکن نشد.", "IRSPEEDY"); }
            }
            else MessageBox.Show("برای بازیابی رمز عبور با پشتیبانی فروشنده حساب تماس بگیرید.", "بازیابی رمز عبور");
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
           
          
            if (OnCredentialEntered != null)
            {
               
               OnCredentialEntered.Invoke(this, txtUsername.Text, passwordVisible ? txtPasswordShow.Text : txtPassword.Password, chkRemember.IsChecked == true);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
   
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

    }
}

