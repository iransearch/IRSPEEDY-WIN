using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    /// <summary>
    /// Interaction logic for DeviceLimitWindow.xaml
    /// </summary>
    public partial class DeviceLimitWindow : Window
    {
        public delegate void RemoveRequested(DeviceLimitWindow sender, DeviceInfo device);
        public event RemoveRequested OnRemoveRequested;

        public DeviceLimitWindow()
        {
            InitializeComponent();
        }

        public void SetDevices(IEnumerable<DeviceInfo> devices, string message)
        {
            txtMessage.Text = message ?? "";
            listDevices.ItemsSource = devices;
            txtError.Visibility = Visibility.Collapsed;
        }

        public void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                txtError.Visibility = Visibility.Collapsed;
                txtError.Text = "";
                return;
            }

            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        private void btnRemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DeviceInfo device)
            {
                if (OnRemoveRequested != null)
                    OnRemoveRequested.Invoke(this, device);
                Close();
            }
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
