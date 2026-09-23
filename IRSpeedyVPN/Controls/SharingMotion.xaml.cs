using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Controls
{
    public partial class SharingMotion : UserControl
    {
        public static readonly DependencyProperty IsDirectProperty = DependencyProperty.Register(
            nameof(IsDirect), typeof(bool), typeof(SharingMotion), new PropertyMetadata(false, ModeChanged));
        public bool IsDirect { get => (bool)GetValue(IsDirectProperty); set => SetValue(IsDirectProperty, value); }
        public SharingMotion() { InitializeComponent(); UpdateMode(); }
        private static void ModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SharingMotion)d).UpdateMode();
        private void UpdateMode()
        {
            if (DesktopNode == null) return;
            DesktopNode.Visibility = PhoneNode.Visibility = IsDirect ? Visibility.Visible : Visibility.Collapsed;
            LockBadge.Visibility = IsDirect ? Visibility.Collapsed : Visibility.Visible;
            RelayLines.Data = Geometry.Parse(IsDirect ? "M72,100 Q172,36 314,36 M72,100 L314,100 M72,100 Q172,164 314,164" : "M72,100 L314,100");
        }
        private void Visibility_Changed(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Pulse == null) return;
            bool active = (bool)e.NewValue;
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                ((ScaleTransform)Pulse.RenderTransform).BeginAnimation(property,
                    active ? new DoubleAnimation(0.95, 1.2, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
            ((TranslateTransform)Packet.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                active ? new DoubleAnimation(0, 162, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        }
    }
}
