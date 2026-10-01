using IRSpeedyVPN.Common;
using IRSpeedyVPN.Windows;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private CancellationTokenSource connectionTestRequest;
        private Window connectionTestWindow;
        private Storyboard connectionTestMotion;

        private static Task<bool[]> CheckConnectionsAsync(string[] hosts, int? httpPort, CancellationToken token)
        {
            return Task.WhenAll(hosts.Select(host => ConnectionTlsTest.CheckAsync(host, 8000, httpPort, token)));
        }

        private async void ConnectionTest_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var service = globalInfo?.CurrentService;
            if (service == null || connectionTestRequest != null || !IsLoaded || !IsVisible) return;

            // Bind the result to the active connection; changing servers invalidates it.
            var connectedAt = globalInfo.ConnectionTime;
            int? httpPort = service.HttpPort;
            var request = new CancellationTokenSource();
            connectionTestRequest = request;
            connectionTestWindow = Window.GetWindow(this);
            if (connectionTestWindow != null) connectionTestWindow.StateChanged += ConnectionTestWindow_StateChanged;
            try
            {
                SetConnectionTestPending(true);
                string[] hosts =
                {
                    "www.google.com",
                    "www.youtube.com",
                    "www.instagram.com",
                    "telegram.org"
                };
                var results = await Task.Run(() => CheckConnectionsAsync(hosts, httpPort, request.Token), request.Token);
                if (request.IsCancellationRequested || connectionTestRequest != request || !IsLoaded || !IsVisible
                    || globalInfo.CurrentService != service || globalInfo.ConnectionTime != connectedAt) return;

                SetConnectionTestPending(false);
                var result = new PingResult
                {
                    GoogleConfirmed = results[0],
                    YoutubeConfirmed = results[1],
                    InstagramConfirmed = results[2],
                    TelegramConfirmed = results[3],
                    Owner = Window.GetWindow(this)
                };
                result.ShowDialog();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogHelper.WriteLog(ex); }
            finally
            {
                if (connectionTestRequest == request)
                {
                    connectionTestRequest = null;
                    DetachConnectionTestWindow();
                    SetConnectionTestPending(false);
                }
                request.Dispose();
            }
        }

        private void SetConnectionTestPending(bool pending)
        {
            TestConnectionMenu.IsEnabled = !pending;
            ConnectionTestVisual.Visibility = ConnectionTestStatus.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            ConnectedCheckBadge.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
            UpdateConnectionTestMotion(pending);
        }

        private void ConnectionTestWindow_StateChanged(object sender, EventArgs e)
            => UpdateConnectionTestMotion(connectionTestRequest != null);

        private void UpdateConnectionTestMotion(bool pending)
        {
            connectionTestMotion?.Remove(this);
            connectionTestMotion = null;
            if (!pending || !IsLoaded || !IsVisible || !ConnectionTestVisual.IsVisible || connectionTestWindow == null
                || connectionTestWindow.WindowState == WindowState.Minimized) return;
            connectionTestMotion = StartMotion("ConnectionTestMotion");
        }

        private void DetachConnectionTestWindow()
        {
            if (connectionTestWindow != null) connectionTestWindow.StateChanged -= ConnectionTestWindow_StateChanged;
            connectionTestWindow = null;
        }

        private void CancelConnectionTest()
        {
            var request = connectionTestRequest;
            connectionTestRequest = null;
            request?.Cancel(); // The owning async operation disposes it after the worker finishes.
            DetachConnectionTestWindow();
            SetConnectionTestPending(false);
        }
    }
}
