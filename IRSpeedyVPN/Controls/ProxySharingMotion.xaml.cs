using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Controls
{
    // Separate from the direct-hotspot diagram: timings and positions follow SettingsShare.dc.html.
    public partial class ProxySharingMotion : UserControl
    {
        private Storyboard motion;
        public ProxySharingMotion() { InitializeComponent(); }
        private void Motion_Loaded(object sender, RoutedEventArgs e) => UpdateMotion();
        private void Motion_Unloaded(object sender, RoutedEventArgs e) => StopMotion();
        private void Motion_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMotion();
        private void StopMotion() { motion?.Remove(this); motion = null; }
        private void UpdateMotion()
        {
            StopMotion();
            if (!IsLoaded || !IsVisible) return;
            motion = new Storyboard();
            var delays = new[] { 0.0, 0.65, 1.3, 0.4, 1.05 };
            for (int i = 0; i < delays.Length; i++)
            {
                Scale("Ring" + i, 0.7, 2.15, 2, delays[i], false, true);
                Loop("Ring" + i, "Opacity", 0.55, 0, 2, delays[i], false, true);
            }
            foreach (string prefix in new[] { "Http", "Socks" })
                for (int i = 0; i < 3; i++)
                    Loop(prefix + i, "(UIElement.RenderTransform).(TranslateTransform.X)", 0, 158, 1.3,
                        (prefix == "Socks" ? 0.65 : 0) + (2 - i) * 0.07);
            Loop("LockRing", "(UIElement.RenderTransform).(RotateTransform.Angle)", 0, 360, 4);
            Loop("Tunnel", "(Shape.Stroke).(Brush.Opacity)", 0.35, 0.95, 0.65, 0, true);
            Spark("SparkLeft", 0);
            Spark("SparkRight", 0.65);
            Scale("OnlineHalo", 1, 1.67, 0.9, 0, true);
            Loop("OnlineHalo", "Opacity", 0.5, 0, 0.9, 0, true);
            motion.Begin(this, true);
        }
        private void Scale(string name, double from, double to, double seconds, double delay, bool reverse = false, bool easeOut = false)
        {
            Loop(name, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", from, to, seconds, delay, reverse, easeOut);
            Loop(name, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", from, to, seconds, delay, reverse, easeOut);
        }
        private void Loop(string name, string property, double from, double to, double seconds, double delay = 0, bool reverse = false, bool easeOut = false)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(seconds), BeginTime = TimeSpan.FromSeconds(delay),
                RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverse
            };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            // CSS ease-out (0,0,.58,1) or ease-in-out (.42,0,.58,1); packets/rotation are linear.
            if (easeOut || reverse)
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new KeySpline(easeOut ? 0 : 0.42, 0, 0.58, 1)));
            else animation.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            Storyboard.SetTargetName(animation, name);
            Storyboard.SetTargetProperty(animation, AnimationPath(property));
            motion.Children.Add(animation);
        }
        private static PropertyPath AnimationPath(string property)
        {
            if (property == "(Shape.Stroke).(Brush.Opacity)")
                return new PropertyPath("(0).(1)", System.Windows.Shapes.Shape.StrokeProperty, System.Windows.Media.Brush.OpacityProperty);
            if (property.Contains("TranslateTransform.X"))
                return new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, System.Windows.Media.TranslateTransform.XProperty);
            if (property.Contains("RotateTransform.Angle"))
                return new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, System.Windows.Media.RotateTransform.AngleProperty);
            if (property.Contains("ScaleTransform.ScaleX"))
                return new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, System.Windows.Media.ScaleTransform.ScaleXProperty);
            if (property.Contains("ScaleTransform.ScaleY"))
                return new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, System.Windows.Media.ScaleTransform.ScaleYProperty);
            return new PropertyPath(UIElement.OpacityProperty);
        }
        private void Spark(string name, double delay)
        {
            Scale(name, 0.3, 2.3, 1.3, delay, false, true);
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.3), BeginTime = TimeSpan.FromSeconds(delay), RepeatBehavior = RepeatBehavior.Forever };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.91)), new KeySpline(0, 0, 0.58, 1)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3))));
            Storyboard.SetTargetName(animation, name);
            Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
            motion.Children.Add(animation);
        }
    }
}
