using IRSpeedyVPN.Services.Hotspot;
using System.Windows;

namespace IRSpeedyVPN.Windows
{
    public partial class ShareVPNSetting
    {
        private void ApplyDirectAvailability(HotspotView view)
        {
            // The existing UI tick reads a cached result only; opening/reopening
            // this window never launches a probe or starts network sharing.
            var directSupport = DirectSharingProbe.Session.Current;
            bool available = DirectSharingAvailability.CanSelect(directSupport, view.State, view.Error);
            DirectTab.IsEnabled = available;
            DirectTabLock.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            DirectAvailabilityText.Text = available ? "" : DirectSharingAvailability.Message(directSupport);
            DirectAvailabilityNotice.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            if (!available && DirectPanel.Visibility == Visibility.Visible) SelectTab(false);
        }
    }
}
