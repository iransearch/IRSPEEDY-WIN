using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private ConnectionTrafficDrawer trafficDrawer;
        private bool trafficPreloadQueued;

        private void TrafficToggleButton_Click(object sender, RoutedEventArgs e) =>
            SetTrafficPanelOpen(TrafficToggleButton.IsChecked == true);

        private void Connected_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || trafficDrawer == null || !trafficDrawer.IsVisible) return;
            e.Handled = true;
            if (trafficDrawer.Panel.DismissReset()) return;
            SetTrafficPanelOpen(false);
        }

        private ConnectionTrafficDrawer EnsureTrafficDrawer()
        {
            if (trafficDrawer != null && !trafficDrawer.IsDisposed) return trafficDrawer;
            var owner = Window.GetWindow(this);
            if (owner == null) return null;

            var drawer = new ConnectionTrafficDrawer(owner, this);
            trafficDrawer = drawer;
            drawer.OpenStateChanged += (sender, args) =>
            {
                if (!ReferenceEquals(trafficDrawer, drawer)) return;
                UpdateTrafficToggle(drawer.IsOpeningOrOpen, true);
                // Keep the edge toggle interactive while the drawer is moving. SetOpen
                // reverses from the current rendered X, so fast repeated clicks are safe.
                TrafficToggleButton.IsEnabled = true;
                btnDisConnect.IsCancel = false;
            };
            drawer.DrawerClosed += (sender, args) =>
            {
                if (!ReferenceEquals(trafficDrawer, drawer)) return;
                UpdateTrafficToggle(false, true);
                TrafficToggleButton.IsEnabled = true;
                btnDisConnect.IsCancel = true;
                if (owner.IsActive && IsVisible) TrafficToggleButton.Focus();
            };
            drawer.Closed += (sender, args) =>
            {
                if (ReferenceEquals(trafficDrawer, drawer)) trafficDrawer = null;
            };
            return drawer;
        }

        private void QueueTrafficDrawerPreload()
        {
            if (trafficPreloadQueued || !IsLoaded || !IsVisible) return;
            trafficPreloadQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                trafficPreloadQueued = false;
                if (!IsLoaded || !IsVisible) return;
                EnsureTrafficDrawer()?.Prepare();
            }));
        }

        private void SetTrafficPanelOpen(bool open)
        {
            if (!IsLoaded || !IsVisible) { ResetTrafficPanel(); return; }

            if (open)
            {
                var drawer = EnsureTrafficDrawer();
                if (drawer == null) { UpdateTrafficToggle(false, false); return; }
                // Normally this is already complete from QueueTrafficDrawerPreload.
                // It remains an idempotent safety net for a click during the first frame.
                drawer.Prepare();
                btnDisConnect.IsCancel = false;
                drawer.SetOpen(true);
                return;
            }

            btnDisConnect.IsCancel = trafficDrawer == null;
            trafficDrawer?.SetOpen(false);
        }

        private void UpdateTrafficToggle(bool open, bool animate)
        {
            TrafficToggleButton.IsChecked = open;
            TrafficToggleButton.ToolTip = open ? "بستن آمار مصرف" : "آمار مصرف برنامه‌ها";
            AutomationProperties.SetName(TrafficToggleButton, TrafficToggleButton.ToolTip.ToString());
            double from = TrafficArrowRotation.Angle;
            TrafficArrowRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            TrafficArrowRotation.Angle = open ? 180 : 0;
            if (animate)
                TrafficArrowRotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(from, TrafficArrowRotation.Angle, TimeSpan.FromMilliseconds(240))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void ResetTrafficPanel()
        {
            trafficPreloadQueued = false;
            var drawer = trafficDrawer;
            trafficDrawer = null;
            drawer?.CloseImmediately();
            if (TrafficToggleButton == null || btnDisConnect == null) return;
            UpdateTrafficToggle(false, false);
            TrafficToggleButton.IsEnabled = true;
            btnDisConnect.IsCancel = true;
        }
    }
}
