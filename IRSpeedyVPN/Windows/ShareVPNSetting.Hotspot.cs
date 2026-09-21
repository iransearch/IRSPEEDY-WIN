using IRSpeedyVPN.Services.Hotspot;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IRSpeedyVPN.Windows
{
    public partial class ShareVPNSetting
    {
        private readonly DispatcherTimer hotspotUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private bool hotspotBusy;
        private void InitializeHotspotUi()
        {
            hotspotUiTimer.Tick += HotspotUiTick;
            Closed += (_, __) => { hotspotUiTimer.Stop(); hotspotUiTimer.Tick -= HotspotUiTick; };
            hotspotUiTimer.Start();
            RefreshHotspotUi();
        }
        private void HotspotUiTick(object sender, EventArgs e) { RefreshHotspotUi(); }
        private void RefreshHotspotUi()
        {
            if (!IsLoaded) return;
            var view = DirectHotspot.Controller.View;
            bool active = view.State == "active";
            bool running = active || view.State == "paused" || view.State == "starting";
            bool eligible = !DirectHotspot.Controller.CoreChanging && (Service as IHotspotSource)?.CaptureTun() != null;
            hotspotToggle.Content = running ? "توقف اشتراک‌گذاری مستقیم" : "فعال‌سازی اشتراک‌گذاری مستقیم";
            hotspotToggle.IsEnabled = !hotspotBusy && (running || (eligible && HotspotProcessChannel.Installed && HotspotProcessChannel.SupportedWindows));
            hotspotRetryStop.Visibility = view.State == "error" ? Visibility.Visible : Visibility.Collapsed;
            hotspotRetryStop.IsEnabled = !hotspotBusy;
            hotspotCredentials.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            hotspotName.Text = active ? view.Ssid : "";
            hotspotPassword.Text = active ? view.Password : "";
            hotspotClients.Text = active ? "دستگاه‌های متصل: " + view.Clients : "";
            hotspotPasswordEditor.IsEnabled = !hotspotBusy && view.State == "off";
            hotspotSavePassword.IsEnabled = !hotspotBusy && view.State == "off";
            if (hotspotBusy) hotspotStatus.Text = "در حال آماده‌سازی یا توقف…";
            else if (view.State == "error") hotspotStatus.Text = FriendlyHotspotError(view.Error);
            else if (active) hotspotStatus.Text = "فعال — دستگاه را به این وای‌فای متصل کنید.";
            else if (view.State == "paused") hotspotStatus.Text = "در انتظار اتصال مجدد VPN…";
            else if (view.State == "starting") hotspotStatus.Text = "در حال راه‌اندازی…";
            else if (!HotspotProcessChannel.SupportedWindows) hotspotStatus.Text = "این قابلیت به ویندوز ۱۰ نسخه ۲۰۰۴ یا جدیدتر نیاز دارد.";
            else if (!HotspotProcessChannel.Installed) hotspotStatus.Text = "این نسخهٔ برنامه قابلیت هات‌اسپات را ندارد؛ نسخهٔ کامل را دریافت کنید.";
            else hotspotStatus.Text = eligible ? "آماده فعال‌سازی" : "ابتدا VPN را در حالت TUN متصل کنید.";
        }
        private async void HotspotToggle_Click(object sender, RoutedEventArgs e)
        {
            if (hotspotBusy) return;
            hotspotBusy = true;
            RefreshHotspotUi();
            try
            {
                var state = DirectHotspot.Controller.View.State;
                if (state == "active" || state == "paused" || state == "starting")
                    await Task.Run(() => DirectHotspot.Controller.Stop());
                else
                {
                    var source = Service as IHotspotSource;
                    string ssid = DirectHotspot.Ssid, password = DirectHotspot.Password;
                    await Task.Run(() => DirectHotspot.Controller.Start(source, ssid, password));
                }
            }
            catch { if (IsLoaded) MessageBox.Show(this, "راه‌اندازی انجام نشد. وضعیت اتصال و توقف جلسه قبلی را بررسی کنید.", "اشتراک‌گذاری مستقیم"); }
            finally { hotspotBusy = false; RefreshHotspotUi(); }
        }
        private async void HotspotStop_Click(object sender, RoutedEventArgs e)
        {
            if (hotspotBusy) return;
            hotspotBusy = true; RefreshHotspotUi();
            try { await Task.Run(() => DirectHotspot.Controller.Stop()); }
            finally { hotspotBusy = false; RefreshHotspotUi(); }
        }
        private void HotspotSavePassword_Click(object sender, RoutedEventArgs e)
        {
            var state = DirectHotspot.Controller.View.State;
            if (hotspotBusy || state != "off") return;
            string password = hotspotPasswordEditor.Text;
            if (!HotspotCoordinator.ValidPassword(password))
            {
                MessageBox.Show(this, "رمز باید دقیقاً ۱۰ رقم انگلیسی (0 تا 9) باشد.", "رمز وای‌فای"); return;
            }
            try { DirectHotspot.SavePassword(password); hotspotPasswordEditor.Clear(); hotspotStatus.Text = "رمز برای اتصال بعدی ذخیره شد."; }
            catch { MessageBox.Show(this, "ذخیره رمز انجام نشد.", "رمز وای‌فای"); }
        }
        private void HotspotCopyName_Click(object sender, RoutedEventArgs e) { CopyHotspotValue(false); }
        private void HotspotCopyPassword_Click(object sender, RoutedEventArgs e) { CopyHotspotValue(true); }
        private void CopyHotspotValue(bool password)
        {
            var view = DirectHotspot.Controller.View;
            if (view.State != "active") return;
            try { Clipboard.SetText(password ? view.Password : view.Ssid); } catch { }
        }
        private static string FriendlyHotspotError(string code)
        {
            switch (code)
            {
                case "helper-missing": return "این نسخهٔ برنامه قابلیت هات‌اسپات را ندارد؛ نسخهٔ کامل را دریافت کنید.";
                case "helper-extraction-failed": return "آماده‌سازی هات‌اسپات انجام نشد. فضای خالی دیسک و دسترسی برنامه را بررسی کنید و دوباره تلاش کنید.";
                case "windows-10-2004-or-later-required": return "این قابلیت به ویندوز ۱۰ نسخه ۲۰۰۴ یا جدیدتر نیاز دارد.";
                case "tun-required": case "tun-lost": case "tun-changed-during-start":
                    return "تونل فعال در دسترس نیست. VPN را در حالت TUN متصل کنید.";
                case "cleanup-not-confirmed": return "پاک‌سازی اشتراک‌گذاری تأیید نشد؛ «تلاش دوباره برای توقف» را بزنید.";
                case "session-health-or-lease-lost": return "اشتراک‌گذاری به دلیل تغییر وضعیت تونل یا هات‌اسپات متوقف شد.";
                default: return "راه‌اندازی هات‌اسپات انجام نشد. هات‌اسپات ویندوز و اشتراک‌گذاری برنامه‌های دیگر را خاموش کنید و دوباره امتحان کنید.";
            }
        }
    }
}
