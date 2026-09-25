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
        private bool editPasswordRequested;
        private string pendingHotspotSsid, pendingHotspotPassword;
        private void InitializeHotspotUi()
        {
            hotspotUiTimer.Tick += HotspotUiTick;
            Closed += (_, __) => { hotspotUiTimer.Stop(); hotspotUiTimer.Tick -= HotspotUiTick; };
            hotspotUiTimer.Start();
            RefreshHotspotUi();
        }
        private void HotspotUiTick(object sender, EventArgs e) { RefreshHotspotUi(); RefreshProxyUi(); }
        private void RefreshHotspotUi()
        {
            if (!IsLoaded) return;
            var view = DirectHotspot.Controller.View;
            ApplyDirectAvailability(view);
            bool active = view.State == "active";
            bool running = active || view.State == "paused" || view.State == "starting";
            bool starting = view.State == "starting" || (pendingHotspotSsid != null && view.State != "error");
            bool showCredentials = active || starting;
            bool eligible = !running && !hotspotBusy && !DirectHotspot.Controller.CoreChanging
                && (Service as IHotspotSource)?.CaptureTun() != null;
            hotspotToggle.IsChecked = running;
            hotspotStateLabel.Text = active ? "فعال" : starting ? "در حال راه‌اندازی…" : "غیرفعال";
            hotspotStateLabel.Foreground = active ? System.Windows.Media.Brushes.MediumSeaGreen : System.Windows.Media.Brushes.Gray;
            DirectMotion.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            DirectOffHint.Visibility = !showCredentials && !editPasswordRequested ? Visibility.Visible : Visibility.Collapsed;
            PasswordEditorPanel.Visibility = view.State == "off" && editPasswordRequested ? Visibility.Visible : Visibility.Collapsed;
            hotspotToggle.IsEnabled = !hotspotBusy && !proxyBusy && (running || (DirectTab.IsEnabled && eligible && HotspotProcessChannel.Installed && HotspotProcessChannel.SupportedWindows));
            hotspotRetryStop.Visibility = view.State == "error" ? Visibility.Visible : Visibility.Collapsed;
            hotspotRetryStop.IsEnabled = !hotspotBusy;
            hotspotCredentials.Visibility = showCredentials ? Visibility.Visible : Visibility.Collapsed;
            hotspotName.Text = active || view.State == "starting" ? view.Ssid : starting ? pendingHotspotSsid : "";
            hotspotPassword.Text = active || view.State == "starting" ? view.Password : starting ? pendingHotspotPassword : "";
            hotspotClientsRow.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            hotspotClientCount.Text = IRSpeedyVPN.Common.PersianDigits.Format(view.Clients.ToString());
            hotspotPasswordEditor.IsEnabled = !hotspotBusy && view.State == "off";
            hotspotSavePassword.IsEnabled = !hotspotBusy && view.State == "off";
            // Update the RTL Run, not TextBlock.Text: replacing Text removes its bidi direction.
            if (starting) hotspotStatusText.Text = "در حال راه‌اندازی… اتصال پس از فعال‌شدن امکان‌پذیر است.";
            else if (hotspotBusy) hotspotStatusText.Text = "در حال آماده‌سازی یا توقف…";
            else if (view.State == "error") hotspotStatusText.Text = FriendlyHotspotError(view.Error);
            else if (active) hotspotStatusText.Text = "";
            else if (view.State == "paused") hotspotStatusText.Text = "در انتظار اتصال مجدد VPN…";
            else if (view.State == "starting") hotspotStatusText.Text = "در حال راه‌اندازی…";
            else if (!HotspotProcessChannel.SupportedWindows) hotspotStatusText.Text = "این قابلیت به ویندوز ۱۰ نسخه ۲۰۰۴ یا جدیدتر نیاز دارد.";
            else if (!HotspotProcessChannel.Installed) hotspotStatusText.Text = "این نسخهٔ برنامه قابلیت هات‌اسپات را ندارد؛ نسخهٔ کامل را دریافت کنید.";
            else hotspotStatusText.Text = eligible ? "آماده فعال‌سازی" : "ابتدا VPN را در حالت TUN متصل کنید.";
            hotspotStatus.Visibility = string.IsNullOrEmpty(hotspotStatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        private async void HotspotToggle_Click(object sender, RoutedEventArgs e)
        {
            if (hotspotBusy || proxyBusy) { RefreshHotspotUi(); return; }
            hotspotBusy = true;
            RefreshProxyUi();
            RefreshHotspotUi();
            try
            {
                var state = DirectHotspot.Controller.View.State;
                if (state == "active" || state == "paused" || state == "starting")
                    await Task.Run(() => DirectHotspot.Controller.Stop());
                else
                {
                    // A queued/programmatic click cannot start an unavailable feature.
                    if (!DirectTab.IsEnabled) return;
                    var source = Service as IHotspotSource;
                    string ssid = DirectHotspot.Ssid, password = DirectHotspot.Password;
                    // Publish exactly the credentials passed to the worker before it starts.
                    pendingHotspotSsid = ssid;
                    pendingHotspotPassword = password;
                    RefreshHotspotUi();
                    await Task.Run(() => DirectHotspot.Controller.Start(source, ssid, password));
                }
            }
            catch
            {
                pendingHotspotSsid = pendingHotspotPassword = null;
                RefreshHotspotUi();
                if (IsLoaded) MessageBox.Show(this, "راه‌اندازی انجام نشد. وضعیت اتصال و توقف جلسه قبلی را بررسی کنید.", "اشتراک‌گذاری مستقیم");
            }
            finally
            {
                pendingHotspotSsid = pendingHotspotPassword = null;
                hotspotBusy = false;
                RefreshHotspotUi();
            }
        }
        private async void HotspotStop_Click(object sender, RoutedEventArgs e)
        {
            if (hotspotBusy || proxyBusy) { RefreshHotspotUi(); return; }
            hotspotBusy = true; RefreshHotspotUi();
            try { await Task.Run(() => DirectHotspot.Controller.Stop()); }
            catch { if (IsLoaded) { hotspotStatusText.Text = "توقف کامل نشد؛ دوباره تلاش کنید."; hotspotStatus.Visibility = Visibility.Visible; } }
            finally { hotspotBusy = false; RefreshHotspotUi(); }
        }
        private void HotspotSavePassword_Click(object sender, RoutedEventArgs e)
        {
            var state = DirectHotspot.Controller.View.State;
            if (hotspotBusy || state != "off") return;
            string password = hotspotPasswordEditor.Password;
            if (!HotspotCoordinator.ValidPassword(password))
            {
                MessageBox.Show(this, "رمز باید دقیقاً ۱۰ رقم انگلیسی (0 تا 9) باشد.", "رمز وای‌فای"); return;
            }
            try { DirectHotspot.SavePassword(password); hotspotPasswordEditor.Clear(); editPasswordRequested = false; RefreshHotspotUi(); hotspotStatusText.Text = "رمز برای اتصال بعدی ذخیره شد."; }
            catch { MessageBox.Show(this, "ذخیره رمز انجام نشد.", "رمز وای‌فای"); }
        }
        private void HotspotEditPassword_Click(object sender, RoutedEventArgs e)
        {
            editPasswordRequested = true;
            RefreshHotspotUi();
            hotspotStatusText.Text = "برای تغییر رمز، ابتدا اشتراک مستقیم را خاموش کنید.";
            hotspotStatus.Visibility = Visibility.Visible;
        }
        private void HotspotCopyName_Click(object sender, RoutedEventArgs e) { CopyHotspotValue(false); }
        private void HotspotCopyPassword_Click(object sender, RoutedEventArgs e) { CopyHotspotValue(true); }
        private void CopyHotspotValue(bool password)
        {
            RefreshHotspotUi();
            if (hotspotCredentials.Visibility != Visibility.Visible) return;
            string value = password ? hotspotPassword.Text : hotspotName.Text;
            if (string.IsNullOrEmpty(value)) return;
            try { Clipboard.SetText(value); } catch { }
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
