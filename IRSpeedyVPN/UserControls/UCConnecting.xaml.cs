using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCConnecting : UserControl
    {
        public event EventHandler CancelRequested;
        public UCConnecting() { InitializeComponent(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, e);
        private void Visibility_Changed(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Orbit == null) return;
            bool active = (bool)e.NewValue;
            ((RotateTransform)Orbit.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                active ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.8)) { RepeatBehavior = RepeatBehavior.Forever } : null);
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                ((ScaleTransform)BreathingRing.RenderTransform).BeginAnimation(property,
                    active ? new DoubleAnimation(0.97, 1.04, TimeSpan.FromSeconds(1.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
            ((TranslateTransform)ProgressSegment.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                active ? new DoubleAnimation(-58, 200, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        }
    }
}
