using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public partial class ServerCountryPicker
    {
        private Window _motionWindow;
        private Storyboard _smartShine;

        private void Picker_Loaded(object sender, RoutedEventArgs e)
        {
            DetachMotionWindow();
            _motionWindow = Window.GetWindow(this);
            if (_motionWindow != null) _motionWindow.StateChanged += MotionWindow_StateChanged;
            UpdateSmartShine();
        }

        private void Picker_Unloaded(object sender, RoutedEventArgs e)
        {
            StopSmartShine();
            DetachMotionWindow();
        }

        private void DetachMotionWindow()
        {
            if (_motionWindow != null) _motionWindow.StateChanged -= MotionWindow_StateChanged;
            _motionWindow = null;
        }

        private void MotionWindow_StateChanged(object sender, EventArgs e) => UpdateSmartShine();
        private void Picker_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateSmartShine();
        private void SmartCard_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSmartShine();

        private void StopSmartShine()
        {
            if (_smartShine != null) _smartShine.Remove(this);
            _smartShine = null;
        }

        private void UpdateSmartShine()
        {
            StopSmartShine();
            if (!IsLoaded || !IsVisible || SmartCardHost == null || !SmartCardHost.IsVisible ||
                _motionWindow == null || _motionWindow.WindowState == WindowState.Minimized) return;

            const double inset = 1;
            const double radius = 13;
            var width = smartCard.ActualWidth - 2 * inset;
            var height = smartCard.ActualHeight - 2 * inset;
            if (width <= 2 * radius || height <= 2 * radius) return;

            var right = inset + width;
            var bottom = inset + height;
            var figure = new PathFigure { StartPoint = new Point(inset + radius, inset), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(right - radius, inset), true));
            figure.Segments.Add(Corner(right, inset + radius, radius));
            figure.Segments.Add(new LineSegment(new Point(right, bottom - radius), true));
            figure.Segments.Add(Corner(right - radius, bottom, radius));
            figure.Segments.Add(new LineSegment(new Point(inset + radius, bottom), true));
            figure.Segments.Add(Corner(inset, bottom - radius, radius));
            figure.Segments.Add(new LineSegment(new Point(inset, inset + radius), true));
            figure.Segments.Add(Corner(inset + radius, inset, radius));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            SmartCardShine.Data = geometry;

            // WPF dash lengths are multiples of stroke thickness, unlike SVG pathLength.
            // Include the rounded corners so each linear 1.6-second cycle is one lap.
            var perimeter = 2 * (width + height - 4 * radius) + 2 * Math.PI * radius;
            var dashUnits = perimeter / SmartCardShine.StrokeThickness;
            SmartCardShine.StrokeDashArray = new DoubleCollection { dashUnits * 0.14, dashUnits * 0.86 };
            var animation = new DoubleAnimation(0, -dashUnits, TimeSpan.FromSeconds(1.6));
            Storyboard.SetTargetName(animation, nameof(SmartCardShine));
            Storyboard.SetTargetProperty(animation, new PropertyPath("StrokeDashOffset"));
            _smartShine = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            _smartShine.Children.Add(animation);
            _smartShine.Begin(this, true);
        }

        private static ArcSegment Corner(double x, double y, double radius)
            => new ArcSegment(new Point(x, y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true);
    }
}
