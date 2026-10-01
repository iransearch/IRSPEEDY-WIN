using IRSpeedyVPN.Common;
using IRSpeedyVPN.Windows;
using System;
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

        private static long?[] CheckPing(string[] sites, int? httpPort, CancellationToken token)
        {
            var results = new long?[sites.Length];
            Parallel.For(0, sites.Length, new ParallelOptions { CancellationToken = token }, i =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    long delay = ServiceHelper.ConnectionUrlTest(sites[i], 4000, httpPort);
                    if (delay >= 0) results[i] = delay;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(ex);
                }
            });
            token.ThrowIfCancellationRequested();
            return results;
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
                string[] sites =
                {
                    "https://www.google.com/generate_204",
                    "https://www.youtube.com",
                    "https://www.instagram.com",
                    "https://telegram.org"
                };
                var results = await Task.Run(() => CheckPing(sites, httpPort, request.Token), request.Token);
                if (request.IsCancellationRequested || connectionTestRequest != request || !IsLoaded || !IsVisible
                    || globalInfo.CurrentService != service || globalInfo.ConnectionTime != connectedAt) return;

                SetConnectionTestPending(false);
                var result = new PingResult
                {
                    GoogleSpeed = results[0],
                    YoutubeSpeed = results[1],
                    InstaSpeed = results[2],
                    TelegramSpeed = results[3],
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
