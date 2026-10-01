using IRSpeedyVPN.Models.NewService;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class DeviceLimitWindow : Window
    {
        public delegate void RemoveRequested(DeviceLimitWindow sender, DeviceInfo device);
        public event RemoveRequested OnRemoveRequested;
        private DeviceInfo pendingDevice;
        public bool IsRemovalInProgress { get; private set; }

        public DeviceLimitWindow()
        {
            InitializeComponent();
        }

        public void SetDevices(IEnumerable<DeviceInfo> devices, string message)
        {
            // A repeated login response must not replace an in-flight selection.
            if (IsRemovalInProgress) return;
            var items = (devices ?? Enumerable.Empty<DeviceInfo>()).Where(device => device != null).ToList();
            txtMessage.Text = string.IsNullOrWhiteSpace(message) ? "ظرفیت دستگاه‌های حساب تکمیل شده" : message;
            listDevices.ItemsSource = items;
            txtDeviceCount.Text = string.Concat(items.Count.ToString(CultureInfo.InvariantCulture)
                .Select(digit => (char)('۰' + digit - '0'))) + " دستگاه";
            txtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            pendingDevice = null;
            ConfirmationBox.Visibility = Visibility.Collapsed;
            ShowError(null);
        }

        public void ShowError(string message)
        {
            SetRemovalInProgress(false);
            txtError.Text = message ?? "";
            txtError.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void CompleteRemoval()
        {
            SetRemovalInProgress(false);
            Close();
        }

        private void SetRemovalInProgress(bool inProgress)
        {
            IsRemovalInProgress = inProgress;
            listDevices.IsEnabled = btnConfirm.IsEnabled = btnCancel.IsEnabled = btnClose.IsEnabled = !inProgress;
            txtConfirmButton.Text = inProgress ? "در حال حذف…" : "حذف و ادامهٔ ورود";
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (IsRemovalInProgress || !(sender is Button button) || !(button.DataContext is DeviceInfo device)) return;
            pendingDevice = device;
            var name = string.IsNullOrWhiteSpace(device.device_name) ? "دستگاه بدون نام" : device.device_name;
            txtConfirmation.Text = "ورود «" + name + "» حذف شود؟";
            txtError.Visibility = Visibility.Collapsed;
            ConfirmationBox.Visibility = Visibility.Visible;
        }

        private void ConfirmRemoval_Click(object sender, RoutedEventArgs e)
        {
            if (IsRemovalInProgress || pendingDevice == null) return;
            if (OnRemoveRequested == null)
            {
                ShowError("امکان حذف دستگاه فراهم نیست. دوباره وارد شوید.");
                return;
            }
            txtError.Visibility = Visibility.Collapsed;
            SetRemovalInProgress(true);
            // Keep the window alive until the server confirms removal or reports an error.
            OnRemoveRequested.Invoke(this, pendingDevice);
        }

        private void CancelRemoval_Click(object sender, RoutedEventArgs e)
        {
            if (IsRemovalInProgress) return;
            pendingDevice = null;
            ConfirmationBox.Visibility = Visibility.Collapsed;
            txtError.Visibility = Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRemovalInProgress) Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!IsRemovalInProgress)
                {
                    if (pendingDevice != null) CancelRemoval_Click(this, new RoutedEventArgs());
                    else Close();
                }
                e.Handled = true;
            }
            else if (IsRemovalInProgress && e.Key == Key.System && e.SystemKey == Key.F4
                && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                // Block user dismissal while pending; owner/application shutdown may still close it.
                e.Handled = true;
            }
        }
    }
}
