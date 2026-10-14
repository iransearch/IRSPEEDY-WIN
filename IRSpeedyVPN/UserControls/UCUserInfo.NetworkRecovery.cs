using IRSpeedyVPN.Services;
using System.Windows;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private void RefreshNetworkRecoveryUi()
        {
            var status = (globalInfo?.CurrentService as TunnelPlusService)?.RecoveryStatus
                ?? ConnectionRecoveryStatus.None;
            NetworkRecoveryStatus.Visibility = status == ConnectionRecoveryStatus.None ? Visibility.Collapsed : Visibility.Visible;
            NetworkRecoveryStatus.Text = status == ConnectionRecoveryStatus.WaitingForInternet
                ? "در انتظار برگشت اینترنت" : status == ConnectionRecoveryStatus.Recovering
                ? "در حال بازیابی اتصال…" : "ارتباط VPN برقرار نشده؛ در حال بازیابی…";
            ConnectedCheckBadge.Visibility = status == ConnectionRecoveryStatus.None && connectionTestRequest == null
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
