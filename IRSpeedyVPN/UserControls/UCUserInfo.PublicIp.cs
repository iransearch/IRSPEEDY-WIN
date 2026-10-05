using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Services;
using System;
using System.Threading;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private readonly PublicIpLookup publicIpLookup = new PublicIpLookup();
        private CancellationTokenSource publicIpRequest;
        private IVPNService publicIpService;
        private DateTime publicIpConnectedAt;
        private int publicIpPort;
        private string publicIpAddress;

        private void CancelPublicIpRequest()
        {
            var request = publicIpRequest;
            publicIpRequest = null;
            request?.Cancel(); // The owning async operation disposes it in finally.
            if (txtReceivedIp != null)
            {
                txtReceivedIp.Text = "—";
                txtReceivedIp.ToolTip = "آی‌پی عمومی در دسترس نیست";
            }
        }

        private async void RefreshPublicIp()
        {
            var service = globalInfo?.CurrentService;
            var port = service?.HttpPort;
            if (!IsLoaded || !IsVisible || !port.HasValue || port.Value < 1 || port.Value > 65535) return;
            var connectedAt = globalInfo.ConnectionTime;
            // Loaded and IsVisibleChanged may both fire. Keep one lookup and keep
            // its successful result when this same connection's page is reopened.
            if (publicIpService == service && publicIpConnectedAt == connectedAt && publicIpPort == port.Value)
            {
                if (publicIpRequest != null) return;
                if (publicIpAddress != null)
                {
                    txtReceivedIp.Text = publicIpAddress;
                    txtReceivedIp.ToolTip = "آی‌پی خروجی مشاهده‌شده برای این اتصال";
                    return;
                }
            }
            CancelPublicIpRequest();
            publicIpService = service;
            publicIpConnectedAt = connectedAt;
            publicIpPort = port.Value;
            publicIpAddress = null;
            var request = new CancellationTokenSource();
            publicIpRequest = request;
            string diagnosticRequest = Guid.NewGuid().ToString("N");
            string diagnosticService = (service as TunnelPlusService)?.DiagnosticIdentity ?? "service=other";
            var diagnosticClock = System.Diagnostics.Stopwatch.StartNew();
            string diagnosticFields = diagnosticService + " request=" + diagnosticRequest;
            ConnectionDiagnostics.Write("public-ip-begin", diagnosticFields
                + " route=explicit-loopback-proxy port=" + port.Value + " directFallback=False");
            txtReceivedIp.ToolTip = "در حال دریافت آی‌پی خروجی اتصال";
            try
            {
                // Lookup failures never control the VPN lifecycle. Alternate
                // providers and retries still use the same explicit VPN listener.
                var result = await publicIpLookup.ResolveAsync(port.Value, request.Token,
                    (stage, fields) => ConnectionDiagnostics.Write(stage, diagnosticFields + " " + fields
                        + " elapsedMs=" + diagnosticClock.ElapsedMilliseconds));
                if (request.IsCancellationRequested || publicIpRequest != request || !IsLoaded || !IsVisible
                    || globalInfo.CurrentService != service || globalInfo.ConnectionTime != connectedAt
                    || service.HttpPort != port) return;
                publicIpAddress = result.Address.ToString();
                ConnectionDiagnostics.Write("public-ip-displayed", diagnosticFields
                    + " provider=" + result.Provider + " ipId=" + ConnectionDiagnostics.Fingerprint(publicIpAddress)
                    + " family=" + result.Address.AddressFamily + " elapsedMs=" + diagnosticClock.ElapsedMilliseconds);
                txtReceivedIp.Text = publicIpAddress;
                txtReceivedIp.ToolTip = "آی‌پی خروجی مشاهده‌شده برای این اتصال";
            }
            catch (OperationCanceledException)
            {
                ConnectionDiagnostics.Write("public-ip-canceled", diagnosticFields
                    + " requested=" + request.IsCancellationRequested + " elapsedMs=" + diagnosticClock.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                ConnectionDiagnostics.Write("public-ip-error", diagnosticFields
                    + " elapsedMs=" + diagnosticClock.ElapsedMilliseconds + " exception=" + ex.GetType().Name);
            }
            finally
            {
                if (publicIpRequest == request)
                {
                    publicIpRequest = null;
                    if (publicIpAddress == null) txtReceivedIp.ToolTip = "دریافت آی‌پی ممکن نشد؛ اتصال شما قطع نشده است";
                }
                request.Dispose();
            }
        }
    }
}
