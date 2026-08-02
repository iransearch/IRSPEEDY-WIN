using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
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
    /// Interaction logic for UCChangePassword.xaml
    /// </summary>
    public partial class UCChangePassword : UserControl, IHasTitle
    {
        public string Title => "تغییر رمز عبور";

        public delegate void OnResultDelegate(UCChangePassword sender, string oldPassword,string newpassword,bool cancel);

        public event OnResultDelegate OnResult;        
  
        public UCChangePassword()
        {
            InitializeComponent();
        }

        private void btnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            OnResult.Invoke(this,txtPassword.Text, txtNewPassword.Text, false);
        }

        private void btnBack_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            OnResult.Invoke(this, null, null, true);
        }

        internal void ResetInput()
        {
            Dispatcher.Invoke((Action)(() =>
            {
                txtPassword.Text = "";
                txtNewPassword.Text = "";
            }));
        }
    }
}
