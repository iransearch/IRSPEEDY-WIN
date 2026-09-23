using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace IRSpeedyVPN.Controls
{
    public partial class HotspotBroadcastMotion : UserControl
    {
        private Storyboard motion;
        public HotspotBroadcastMotion() { InitializeComponent(); }
        private void Motion_Loaded(object sender, RoutedEventArgs e) => UpdateMotion();
        private void Motion_Unloaded(object sender, RoutedEventArgs e) => StopMotion();
        private void Motion_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMotion();
        private void StopMotion() { motion?.Remove(this); motion = null; }
        private void UpdateMotion()
        {
            StopMotion();
            if (!IsLoaded || !IsVisible) return;
            motion = new Storyboard();
            for (int i = 0; i < 3; i++)
            {
                Animate("Ring" + i, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, ScaleTransform.ScaleXProperty), .7, 2.15, 2, i * .65, false, true);
                Animate("Ring" + i, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, ScaleTransform.ScaleYProperty), .7, 2.15, 2, i * .65, false, true);
                Animate("Ring" + i, new PropertyPath(UIElement.OpacityProperty), .55, 0, 2, i * .65, false, true);
                Animate("Online" + i, new PropertyPath("(0).(1)", UIElement.EffectProperty, DropShadowEffect.OpacityProperty), .5, 0, .9, i * .4, true);
                Animate("Online" + i, new PropertyPath("(0).(1)", UIElement.EffectProperty, DropShadowEffect.BlurRadiusProperty), 0, 8, .9, i * .4, true);
            }
            var paths = new[] { Route0, Route1, Route2 };
            var duration = new[] { 1.7, 1.9, 1.5 };
            for (int i = 0; i < 3; i++)
            {
                var packet = new MatrixAnimationUsingPath
                {
                    PathGeometry = PathGeometry.CreateFromGeometry(paths[i].Data),
                    Duration = TimeSpan.FromSeconds(duration[i]), BeginTime = TimeSpan.FromSeconds(i * .35),
                    DoesRotateWithTangent = false, RepeatBehavior = RepeatBehavior.Forever
                };
                Storyboard.SetTargetName(packet, "Packet" + i);
                Storyboard.SetTargetProperty(packet, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, MatrixTransform.MatrixProperty));
                motion.Children.Add(packet);
            }
            motion.Begin(this, true);
        }
        private void Animate(string name, PropertyPath property, double from, double to, double seconds, double delay, bool reverse = false, bool easeOut = false)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            { Duration = TimeSpan.FromSeconds(seconds), BeginTime = TimeSpan.FromSeconds(delay), RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverse };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new KeySpline(easeOut ? 0 : .42, 0, .58, 1)));
            Storyboard.SetTargetName(animation, name);
            Storyboard.SetTargetProperty(animation, property);
            motion.Children.Add(animation);
        }
    }
}
