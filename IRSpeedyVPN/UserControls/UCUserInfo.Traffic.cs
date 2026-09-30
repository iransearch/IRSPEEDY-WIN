using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private ConnectionTrafficDrawer trafficDrawer;

        private void TrafficToggleButton_Click(object sender, RoutedEventArgs e) =>
            SetTrafficPanelOpen(TrafficToggleButton.IsChecked == true);

        private void Connected_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || trafficDrawer == null || !trafficDrawer.IsVisible) return;
            e.Handled = true;
            if (trafficDrawer.Panel.DismissReset()) return;
            SetTrafficPanelOpen(false);
        }

        private void SetTrafficPanelOpen(bool open)
        {
            if (!IsLoaded || !IsVisible) { ResetTrafficPanel(); return; }
            if (open && trafficDrawer == null)
            {
                var owner = Window.GetWindow(this);
                if (owner == null) { UpdateTrafficToggle(false, false); return; }
                var drawer = new ConnectionTrafficDrawer(owner, this);
                trafficDrawer = drawer;
                drawer.OpenStateChanged += (sender, args) =>
                {
                    if (!ReferenceEquals(trafficDrawer, drawer)) return;
                    UpdateTrafficToggle(drawer.IsOpeningOrOpen, true);
                    btnDisConnect.IsCancel = false;
                };
                drawer.DrawerClosed += (sender, args) =>
                {
                    if (!ReferenceEquals(trafficDrawer, drawer)) return;
                    UpdateTrafficToggle(false, true);
                    btnDisConnect.IsCancel = true;
                    if (owner.IsActive && IsVisible) TrafficToggleButton.Focus();
                };
                drawer.Closed += (sender, args) =>
                {
                    if (ReferenceEquals(trafficDrawer, drawer)) trafficDrawer = null;
                };
            }
            // Escape remains reserved until the drawer has finished closing.
            btnDisConnect.IsCancel = trafficDrawer == null;
            trafficDrawer?.SetOpen(open);
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
                    new DoubleAnimation(from, TrafficArrowRotation.Angle, TimeSpan.FromMilliseconds(320))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void ResetTrafficPanel()
        {
            var drawer = trafficDrawer;
            trafficDrawer = null;
            drawer?.CloseImmediately();
            if (TrafficToggleButton == null || btnDisConnect == null) return;
            UpdateTrafficToggle(false, false);
            btnDisConnect.IsCancel = true;
        }
    }
}
