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
            // The banner overlays the rocket's existing fixed area. Recovery and
            // manual tests never resize the content or move the account/server cards.
            NetworkRecoveryBanner.Visibility = status != ConnectionRecoveryStatus.None && connectionTestRequest == null
                ? Visibility.Visible : Visibility.Collapsed;
            NetworkRecoveryStatus.Text = status == ConnectionRecoveryStatus.WaitingForInternet
                ? "در انتظار برگشت اینترنت" : "در حال بازیابی اتصال…";
            ConnectedCheckBadge.Visibility = status == ConnectionRecoveryStatus.None && connectionTestRequest == null
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
