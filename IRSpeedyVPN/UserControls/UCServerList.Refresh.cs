using IRSpeedyVPN.Common;
using System;
using System.Linq;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCServerList
    {
        /// <summary>
        /// Rebind the visible server picker after the 30-minute background refresh.
        /// If the user is on the connection-info screen, do nothing here; the normal
        /// Loaded handler will bind the fresh ServiceFactory list when they return.
        /// </summary>
        internal void RefreshServicesFromFactory()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)RefreshServicesFromFactory);
                return;
            }

            if (!IsVisible || serviceFactory.Services == null)
                return;

            var previousServiceName = cmbService.SelectedItem?.ToString();
            var items = serviceFactory.Services
                .OrderBy(y => y.Order)
                .GroupBy(x => x.Name)
                .Select(x => x.Key)
                .ToArray();

            _isLoading = true;
            selectedService = null;

            cmbService.Items.Clear();
            cmbService.Items.AddRange(items);

            var preferred = !string.IsNullOrWhiteSpace(previousServiceName)
                && items.Contains(previousServiceName)
                ? previousServiceName
                : globalInfo?.CurrentService?.Name;

            cmbService.SelectedItem = !string.IsNullOrWhiteSpace(preferred)
                && items.Contains(preferred)
                ? preferred
                : items.FirstOrDefault();

            UpdateHeaderIcons();
        }
    }
}
