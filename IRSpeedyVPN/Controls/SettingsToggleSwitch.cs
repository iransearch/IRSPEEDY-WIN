using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Controls
{
    /// <summary>
    /// Animated switch used on Connection Methods and Split Tunnel.
    /// Look: Themes/ConnectionSplit/Controls.xaml (implicit style for this type).
    /// The thumb is a circle (Height - 2*3px padding) that slides 150 ms to the right when checked.
    /// Size is chosen by the caller (Width/Height), e.g. 42x24, 46x26, 40x23.
    /// </summary>
    public class SettingsToggleSwitch : ToggleButton
    {
        public bool IsOn { get => IsChecked == true; set => SetCurrentValue(IsCheckedProperty, value); }

        private const double TrackPadding = 3;
        private static readonly Duration SlideDuration = new Duration(TimeSpan.FromMilliseconds(150));

        private FrameworkElement _thumb;
        private readonly TranslateTransform _shift = new TranslateTransform();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _thumb = GetTemplateChild("PART_Thumb") as FrameworkElement;
            if (_thumb != null)
            {
                _thumb.RenderTransform = _shift;
            }
            UpdateThumb(animate: false);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateThumb(animate: false);
        }

        protected override void OnChecked(RoutedEventArgs e)
        {
            base.OnChecked(e);
            UpdateThumb(animate: IsLoaded);
        }

        protected override void OnUnchecked(RoutedEventArgs e)
        {
            base.OnUnchecked(e);
            UpdateThumb(animate: IsLoaded);
        }

        private void UpdateThumb(bool animate)
        {
            if (_thumb == null || ActualHeight <= 0 || ActualWidth <= 0)
            {
                return;
            }

            double size = Math.Max(0, ActualHeight - 2 * TrackPadding);
            _thumb.Width = size;

            double target = IsChecked == true
                ? Math.Max(0, ActualWidth - 2 * TrackPadding - size)
                : 0;

            if (!animate)
            {
                _shift.BeginAnimation(TranslateTransform.XProperty, null);
                _shift.X = target;
                return;
            }

            var slide = new DoubleAnimation(target, SlideDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _shift.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }
}
