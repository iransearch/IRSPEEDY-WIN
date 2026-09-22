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

            var previousServiceName = _selectedServiceName;
            var items = serviceFactory.Services
                .OrderBy(y => y.Order)
                .GroupBy(x => x.Name)
                .Select(x => x.Key)
                .ToArray();

            _isLoading = true;
            selectedService = null;

            var preferred = !string.IsNullOrWhiteSpace(previousServiceName)
                && items.Contains(previousServiceName)
                ? previousServiceName
                : globalInfo?.CurrentService?.Name;

            _selectedServiceName = !string.IsNullOrWhiteSpace(preferred)
                && items.Contains(preferred)
                ? preferred
                : items.FirstOrDefault();

            // No visible protocol picker any more (§ service selection removed) --
            // resolve it the same way ResolveServiceAndProtocol() does on first load.
            var protocols = serviceFactory.Services
                .Where(x => x.Name == _selectedServiceName)
                .SelectMany(i => i.Protocols).Distinct().ToArray();

            selectedProtocol = protocols.Length > 1
                ? (selectedProtocol != null && protocols.Contains(selectedProtocol) ? selectedProtocol : protocols[0])
                : null;

            RefreshCountry(selectedProtocol);
            UpdateHeaderIcons();
        }
    }
}
