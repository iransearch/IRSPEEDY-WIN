using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SettingsPassword : Window
    {
        public Func<string, string, Task<string>> ChangePasswordAsync { get; set; }
        public Action<string> PasswordChangeAccepted { get; set; }
        private bool saving;
        public SettingsPassword() { InitializeComponent(); }
        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) { if (!saving) Close(); }
        private static string Read(PasswordBox masked, TextBox visible)
            => visible.Visibility == Visibility.Visible ? visible.Text : masked.Password;
        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            var prefix = (string)((Button)sender).Tag;
            var masked = (PasswordBox)FindName(prefix + "Password");
            var visible = (TextBox)FindName(prefix + "Visible");
            if (visible.Visibility == Visibility.Visible)
            {
                masked.Password = visible.Text;
                visible.Clear();
                visible.Visibility = Visibility.Collapsed;
                masked.Visibility = Visibility.Visible;
                masked.Focus();
            }
            else
            {
                visible.Text = masked.Password;
                masked.Visibility = Visibility.Collapsed;
                visible.Visibility = Visibility.Visible;
                visible.Focus();
                visible.CaretIndex = visible.Text.Length;
            }
        }
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (saving) return;
            string oldPassword = Read(CurrentPassword, CurrentVisible);
            string password = Read(NewPassword, NewVisible);
            if (oldPassword.Length == 0) { ErrorText.Text = "رمز فعلی را وارد کنید."; return; }
            if (password.Length < 5 || password.Any(c => c < '0' || c > '9'))
            { ErrorText.Text = "رمز جدید باید حداقل ۵ رقم و فقط شامل اعداد 0 تا 9 باشد."; return; }
            if (password != Read(ConfirmPassword, ConfirmVisible))
            { ErrorText.Text = "تکرار رمز عبور با رمز جدید یکسان نیست."; return; }
            if (password == oldPassword) { ErrorText.Text = "رمز جدید باید با رمز فعلی متفاوت باشد."; return; }
            if (ChangePasswordAsync == null) { ErrorText.Text = "ابتدا وارد حساب کاربری شوید."; return; }
            saving = true;
            SaveButton.IsEnabled = false;
            ErrorText.Text = "در حال ذخیره…";
            try
            {
                var error = await ChangePasswordAsync(oldPassword, password);
                if (error == null)
                {
                    // Close the modal before revealing the account verification screen.
                    DialogResult = true;
                    PasswordChangeAccepted?.Invoke(password);
                }
                else ErrorText.Text = error;
            }
            catch { ErrorText.Text = "ارتباط با سرویس برقرار نشد. دوباره تلاش کنید."; }
            finally { saving = false; SaveButton.IsEnabled = true; }
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Do not let a second request race with an in-flight account update.
            if (saving && DialogResult != true) e.Cancel = true;
            base.OnClosing(e);
        }
        protected override void OnClosed(EventArgs e)
        {
            CurrentPassword.Clear(); NewPassword.Clear(); ConfirmPassword.Clear();
            CurrentVisible.Clear(); NewVisible.Clear(); ConfirmVisible.Clear();
            base.OnClosed(e);
        }
    }
}
