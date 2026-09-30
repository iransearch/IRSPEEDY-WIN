using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private const double TrafficClosedOffset = -416;
        private readonly BlurEffect trafficBackgroundBlur = new BlurEffect
        {
            Radius = 0,
            RenderingBias = RenderingBias.Performance
        };
        private Storyboard trafficMotion;
        private bool trafficPanelOpen;

        private void TrafficToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SetTrafficPanelOpen(TrafficToggleButton.IsChecked == true);
        }

        private void TrafficBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            SetTrafficPanelOpen(false);
            TrafficToggleButton.Focus();
        }

        private void Connected_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Consume Escape before the existing IsCancel disconnect button sees it.
            if (e.Key != Key.Escape || (!trafficPanelOpen && trafficMotion == null)) return;
            e.Handled = true;
            SetTrafficPanelOpen(false);
            TrafficToggleButton.Focus();
        }

        private void SetTrafficPanelOpen(bool open)
        {
            if (!IsLoaded || !IsVisible)
            {
                ResetTrafficPanel();
                return;
            }
            if (trafficPanelOpen == open && trafficMotion == null) return;

            // Capture the currently displayed values before removing the old clocks.
            // This lets a second click reverse a partially opened/closed panel smoothly.
            var nextMotion = new Storyboard();
            AddTrafficAnimation(nextMotion, TrafficPanelTranslate, TranslateTransform.XProperty, open ? 0 : TrafficClosedOffset);
            AddTrafficAnimation(nextMotion, TrafficPanelScale, ScaleTransform.ScaleXProperty, open ? 1 : 0.78);
            AddTrafficAnimation(nextMotion, TrafficPanelSkew, SkewTransform.AngleYProperty, open ? 0 : -4);
            AddTrafficAnimation(nextMotion, TrafficPanelContainer, UIElement.OpacityProperty, open ? 1 : 0);
            AddTrafficAnimation(nextMotion, TrafficFoldShade, UIElement.OpacityProperty, open ? 0 : 0.55);
            AddTrafficAnimation(nextMotion, TrafficArrowRotation, RotateTransform.AngleProperty, open ? 180 : 0);
            AddTrafficAnimation(nextMotion, TrafficBackdrop, UIElement.OpacityProperty, open ? 1 : 0);
            AddTrafficAnimation(nextMotion, ConnectedContent, UIElement.OpacityProperty, open ? 0.6 : 1);
            AddTrafficAnimation(nextMotion, TrafficBackgroundScale, ScaleTransform.ScaleXProperty, open ? 0.975 : 1);
            AddTrafficAnimation(nextMotion, TrafficBackgroundScale, ScaleTransform.ScaleYProperty, open ? 0.975 : 1);
            AddTrafficAnimation(nextMotion, TrafficBackgroundTranslate, TranslateTransform.XProperty, open ? 10 : 0);
            AddTrafficAnimation(nextMotion, trafficBackgroundBlur, BlurEffect.RadiusProperty, open ? 6 : 0);

            StopTrafficMotion();
            StopConnectedMotion();
            trafficPanelOpen = open;
            UpdateTrafficToggle(open);
            TrafficPanelContainer.Visibility = Visibility.Visible;
            TrafficPanelContainer.IsEnabled = open;
            TrafficBackdrop.Visibility = Visibility.Visible;
            ConnectedContent.IsEnabled = false;
            ConnectedContent.Effect = trafficBackgroundBlur;

            if (!SystemParameters.ClientAreaAnimation)
            {
                ApplyTrafficState(open);
                if (!open) UpdateConnectedMotion();
                return;
            }

            trafficMotion = nextMotion;
            nextMotion.Completed += (sender, args) =>
            {
                if (!ReferenceEquals(trafficMotion, nextMotion)) return;
                StopTrafficMotion();
                ApplyTrafficState(open);
                if (!open) UpdateConnectedMotion();
            };
            nextMotion.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        }

        private static void AddTrafficAnimation(Storyboard motion, DependencyObject target,
            DependencyProperty property, double destination)
        {
            var animation = new DoubleAnimation((double)target.GetValue(property), destination,
                TimeSpan.FromMilliseconds(620))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            motion.Children.Add(animation);
        }

        private void StopTrafficMotion()
        {
            var motion = trafficMotion;
            trafficMotion = null;
            motion?.Remove(this);
        }

        private void UpdateTrafficToggle(bool open)
        {
            TrafficToggleButton.IsChecked = open;
            TrafficToggleButton.ToolTip = open ? "بستن آمار مصرف" : "آمار مصرف برنامه‌ها";
            AutomationProperties.SetName(TrafficToggleButton,
                open ? "بستن آمار مصرف برنامه‌ها" : "باز کردن آمار مصرف برنامه‌ها");
            // Keep the existing Escape-to-disconnect behavior only while the panel is closed.
            btnDisConnect.IsCancel = !open;
        }

        private void ApplyTrafficState(bool open)
        {
            TrafficPanelTranslate.X = open ? 0 : TrafficClosedOffset;
            TrafficPanelScale.ScaleX = open ? 1 : 0.78;
            TrafficPanelSkew.AngleY = open ? 0 : -4;
            TrafficPanelContainer.Opacity = open ? 1 : 0;
            TrafficPanelContainer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            TrafficPanelContainer.IsEnabled = open;
            TrafficFoldShade.Opacity = open ? 0 : 0.55;
            TrafficArrowRotation.Angle = open ? 180 : 0;
            TrafficBackdrop.Opacity = open ? 1 : 0;
            TrafficBackdrop.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ConnectedContent.Opacity = open ? 0.6 : 1;
            ConnectedContent.IsEnabled = !open;
            TrafficBackgroundScale.ScaleX = TrafficBackgroundScale.ScaleY = open ? 0.975 : 1;
            TrafficBackgroundTranslate.X = open ? 10 : 0;
            trafficBackgroundBlur.Radius = open ? 6 : 0;
            // Remove the effect entirely when closed so normal text stays crisp.
            ConnectedContent.Effect = open ? trafficBackgroundBlur : null;
        }

        private void ResetTrafficPanel()
        {
            if (TrafficPanelContainer == null || TrafficToggleButton == null || btnDisConnect == null) return;
            StopTrafficMotion();
            trafficPanelOpen = false;
            UpdateTrafficToggle(false);
            ApplyTrafficState(false);
        }
    }
}
