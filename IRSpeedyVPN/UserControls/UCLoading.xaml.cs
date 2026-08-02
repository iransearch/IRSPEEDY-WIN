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

namespace IRSpeedyVPN.UserControls
{
    /// <summary>
    /// Interaction logic for UCLoading.xaml
    /// </summary>
    public partial class UCLoading : UserControl
    {
        public event EventHandler OnCancelRequest;
        Timer timer;
        public UCLoading()
        {
            InitializeComponent();
            timer = new Timer(mainTimerCallback, null, int.MaxValue,int.MaxValue);
        }
        public void mainTimerCallback(object state)
        {
            timer.Change(int.MaxValue, int.MaxValue);
            Dispatcher.Invoke((Action)(() =>
            {
                lblMessage.Visibility = Visibility.Collapsed;
                btn_cancel.Visibility = Visibility.Visible;
            }));
          
        }
        private void btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            btn_cancel.Visibility = Visibility.Hidden;
            if (OnCancelRequest != null)
                OnCancelRequest.Invoke(this, e);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
          
        }
        public void SetMessage(string message)
        {
            lblMessage.Text = message;
        }
        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            btn_cancel.Visibility = Visibility.Hidden;
            if ((bool)e.NewValue )
            {
              
                timer.Change(25000, 25000);
            }
            else
            {                
                lblMessage.Text = "";
                timer.Change(int.MaxValue, int.MaxValue);
            }
        }
    }
}
