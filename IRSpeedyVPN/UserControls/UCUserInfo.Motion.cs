using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private Storyboard glowMotion, ringMotion, exhaustMotion, cardMotion;

        private Storyboard StartMotion(string key)
        {
            var motion = ((Storyboard)FindResource(key)).Clone();
            motion.Begin(this, true);
            return motion;
        }

        private void UpdateConnectedMotion()
        {
            StopConnectedMotion();
            if (!IsLoaded || !IsVisible) return;
            glowMotion = StartMotion("ConnectedGlowMotion");
            ringMotion = StartMotion("ConnectedRingMotion");
            exhaustMotion = StartMotion("ConnectedExhaustMotion");
            UpdateCardMotion();
        }

        private void StopConnectedMotion()
        {
            glowMotion?.Remove(this);
            ringMotion?.Remove(this);
            exhaustMotion?.Remove(this);
            cardMotion?.Remove(this);
            glowMotion = ringMotion = exhaustMotion = cardMotion = null;
        }

        private void Connected_Unloaded(object sender, RoutedEventArgs e) => StopConnectedMotion();
        private void ServerCard_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardMotion();

        private void UpdateCardMotion()
        {
            if (ServerCardShine == null || ConnectedServerCard == null) return;
            cardMotion?.Remove(this);
            cardMotion = null;
            if (!IsLoaded || !IsVisible) return;
            double thickness = ServerCardShine.StrokeThickness;
            double inset = thickness / 2;
            double width = ConnectedServerCard.ActualWidth - thickness;
            double height = ConnectedServerCard.ActualHeight - thickness;
            if (width <= 0 || height <= 0) return;
            double radius = Math.Min(ConnectedServerCard.CornerRadius.TopLeft - inset, Math.Min(width, height) / 2);
            radius = Math.Max(0, radius);
            double left = inset, top = inset, right = left + width, bottom = top + height;
            var figure = new PathFigure { StartPoint = new Point(left + radius, top), IsClosed = true, IsFilled = false };
            figure.Segments.Add(new LineSegment(new Point(right - radius, top), true));
            AddCorner(figure, right, top + radius, radius);
            figure.Segments.Add(new LineSegment(new Point(right, bottom - radius), true));
            AddCorner(figure, right - radius, bottom, radius);
            figure.Segments.Add(new LineSegment(new Point(left + radius, bottom), true));
            AddCorner(figure, left, bottom - radius, radius);
            figure.Segments.Add(new LineSegment(new Point(left, top + radius), true));
            AddCorner(figure, left + radius, top, radius);
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            ServerCardShine.Data = geometry;

            // WPF dash lengths/offsets use stroke-width units. One exact perimeter
            // per 1.6s keeps speed constant through straight sections and arcs.
            double perimeter = 2 * (width + height - 4 * radius) + 2 * Math.PI * radius;
            double brightLength = perimeter * 0.14; // reference pathLength=100, dasharray=14 86
            ServerCardShine.StrokeDashArray = new DoubleCollection { brightLength / thickness, (perimeter - brightLength) / thickness };
            var animation = new DoubleAnimation(0, -perimeter / thickness, TimeSpan.FromSeconds(1.6));
            Storyboard.SetTargetName(animation, nameof(ServerCardShine));
            Storyboard.SetTargetProperty(animation, new PropertyPath("StrokeDashOffset"));
            cardMotion = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            cardMotion.Children.Add(animation);
            cardMotion.Begin(this, true);
        }

        private static void AddCorner(PathFigure figure, double x, double y, double radius)
        {
            figure.Segments.Add(new ArcSegment(new Point(x, y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }
    }
}
