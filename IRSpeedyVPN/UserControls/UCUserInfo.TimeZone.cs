using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;
using System;
using System.Windows;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private bool timeZoneBusy;
        private async void TimeZone_Click(object sender, RoutedEventArgs e)
        {
            if (timeZoneBusy) return;
            var session = VpnTimeZone.Session;
            var owner = session.Connection;
            var service = globalInfo?.CurrentService;
            if (!IsLoaded || service == null || !ReferenceEquals(owner, service)) return;
            var port = service.HttpPort;
            timeZoneBusy = true;
            RefreshTimeZoneUi();
            try
            {
                if (session.Active != null) session.Disable();
                else
                {
                    if (!port.HasValue || port.Value < 1 || port.Value > 65535) throw new InvalidOperationException("VPN listener is unavailable.");
                    await session.EnableAsync(owner, port.Value);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                GetMainWindow()?.ShowHintPopup(session.Active == null
                    ? "تنظیم منطقهٔ زمانی انجام نشد؛ دوباره امتحان کنید."
                    : "بازگرداندن منطقهٔ زمانی انجام نشد؛ دوباره امتحان کنید.", btnTimeZone);
            }
            finally { timeZoneBusy = false; RefreshTimeZoneUi(); }
        }

        private void RefreshTimeZoneUi()
        {
            if (btnTimeZone == null || TimeZoneClockIcon == null) return;
            var session = VpnTimeZone.Session;
            var active = session.Active;
            var service = globalInfo?.CurrentService;
            btnTimeZone.IsEnabled = !timeZoneBusy && service != null && ReferenceEquals(session.Connection, service)
                && (!(service is TunnelPlusService tunnel) || tunnel.IsTunnelConnected);
            btnTimeZone.Opacity = timeZoneBusy ? 0.5 : 1;
            TimeZoneClockIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
                active == null ? "IconStrokeBrush" : "ConnectedGreenBrush");
            var text = timeZoneBusy ? "در حال شناسایی منطقهٔ زمانی IP خروجی VPN…"
                : active == null ? "هماهنگی منطقهٔ زمانی با VPN"
                : "منطقهٔ زمانی فعال: " + active.IanaId + "\nساعت: "
                    + PersianDigits(DateTime.Now.ToString("HH:mm:ss")) + "\nبرای بازگشت به تنظیم قبلی کلیک کنید.";
            btnTimeZone.ToolTip = text;
            System.Windows.Automation.AutomationProperties.SetName(btnTimeZone,
                active == null ? "فعال‌سازی منطقهٔ زمانی VPN" : "بازگرداندن منطقهٔ زمانی قبلی");
            System.Windows.Automation.AutomationProperties.SetHelpText(btnTimeZone, text);
        }
    }
}
