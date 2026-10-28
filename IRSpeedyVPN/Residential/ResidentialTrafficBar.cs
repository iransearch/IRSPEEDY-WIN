using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IRSpeedyVPN.Residential
{
    public sealed class ResidentialTrafficBar : UserControl
    {
        private readonly Border bar, badge;
        private readonly Grid ring;
        private readonly Path arc;
        private readonly TextBlock remaining, used;
        private readonly TranslateTransform translation = new TranslateTransform();
        private bool shownOnce, targetVisible, previousConnected, pendingEntrance;
        private long animationGeneration;
        private static Brush Color(string color) => (Brush)new BrushConverter().ConvertFromString(color);
        private static TextBlock Text(string value, double size, Brush color) => new TextBlock
            { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };

        public ResidentialTrafficBar()
        {
            Height = 0; Visibility = Visibility.Collapsed; ClipToBounds = true;
            IsHitTestVisible = false; Focusable = false;
            var body = new Grid { Height = 74, RenderTransform = translation };
            bar = new Border { Height = 64, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(12), Background = Color("#202838"), BorderBrush = Color("#2D3850"), BorderThickness = new Thickness(1) };
            body.Children.Add(bar); Content = body;
            var layout = new Grid { FlowDirection = FlowDirection.RightToLeft };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = layout;
            ring = new TrafficRing { Width = 44, Height = 44, FlowDirection = FlowDirection.LeftToRight };
            ring.Children.Add(new Ellipse { Stroke = Color("#1FFFFFFF"), StrokeThickness = 5, Margin = new Thickness(2.5) });
            arc = new Path { Stroke = Color("#F2C53D"), StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.Children.Add(arc); layout.Children.Add(ring);
            var labels = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(labels, 1); layout.Children.Add(labels);
            var line = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
            remaining = Text("", 18, Brushes.White); remaining.FontWeight = FontWeights.Bold; remaining.FlowDirection = FlowDirection.LeftToRight;
            line.Children.Add(remaining);
            var caption = Text("باقی‌مانده", 12, Color("#A3ADC7")); caption.Margin = new Thickness(6, 0, 0, 0); line.Children.Add(caption);
            labels.Children.Add(line);
            var usage = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
            used = Text("", 12, Color("#A3ADC7")); used.FlowDirection = FlowDirection.LeftToRight; usage.Children.Add(used);
            var suffix = Text("مصرف شده", 12, Color("#A3ADC7")); suffix.Margin = new Thickness(5, 0, 0, 0); usage.Children.Add(suffix); labels.Children.Add(usage);
            badge = new Border { CornerRadius = new CornerRadius(30), Background = Color("#1F3FCF8E"), Padding = new Thickness(7, 4, 7, 4), VerticalAlignment = VerticalAlignment.Center };
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Color("#3FCF8E"), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            var statusText = Text("در حال استفاده", 11, Color("#3FCF8E")); statusText.FontWeight = FontWeights.SemiBold; status.Children.Add(statusText);
            badge.Child = status; Grid.SetColumn(badge, 2); layout.Children.Add(badge);
            Loaded += (s, e) =>
            {
                SystemParameters.StaticPropertyChanged += ParametersChanged;
                if (pendingEntrance && targetVisible && SystemParameters.ClientAreaAnimation)
                {
                    pendingEntrance = false;
                    BeginAnimation(OpacityProperty, Animation(0, 1));
                    translation.BeginAnimation(TranslateTransform.YProperty, Animation(-6, 0));
                }
            };
            Unloaded += (s, e) => { SystemParameters.StaticPropertyChanged -= ParametersChanged; SetInstant(targetVisible); };
        }
        private void ParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation)
                Dispatcher.Invoke(() => SetInstant(targetVisible));
        }
        internal void Update(ResidentialTraffic traffic, bool connected, bool activeTab)
        {
            bool show = traffic != null && activeTab;
            if (traffic != null)
            {
                remaining.Text = traffic.RemainingText; used.Text = traffic.UsageText;
                string description = Math.Round(traffic.Ratio * 100).ToString(CultureInfo.InvariantCulture) + " percent of traffic left";
                AutomationProperties.SetName(ring, description); AutomationProperties.SetName(this, description);
                arc.Data = MakeArc(traffic.Ratio);
            }
            bar.BorderBrush = Color(connected ? "#8C3FCF8E" : "#2D3850");
            badge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            bool changed = show != targetVisible || (show && previousConnected != connected);
            targetVisible = show; previousConnected = connected;
            if (!changed) return;
            pendingEntrance = show && shownOnce && !IsLoaded;
            bool animate = shownOnce && SystemParameters.ClientAreaAnimation && IsLoaded;
            if (show) shownOnce = true;
            if (!animate) { SetInstant(show); return; }
            long version = ++animationGeneration;
            double from = ActualHeight, opacity = Opacity;
            Stop(); Visibility = Visibility.Visible;
            var height = Animation(from, show ? 74 : 0);
            height.Completed += (s, e) => { if (version == animationGeneration) SetInstant(targetVisible); };
            BeginAnimation(HeightProperty, height);
            BeginAnimation(OpacityProperty, Animation(show && from == 0 ? 0 : opacity, show ? 1 : 0));
            translation.BeginAnimation(TranslateTransform.YProperty, Animation(show ? -6 : translation.Y, 0));
        }
        private void Stop()
        {
            BeginAnimation(HeightProperty, null); BeginAnimation(OpacityProperty, null);
            translation.BeginAnimation(TranslateTransform.YProperty, null);
        }
        private void SetInstant(bool show)
        {
            animationGeneration++; Stop(); Height = show ? 74 : 0; Opacity = show ? 1 : 0;
            translation.Y = 0; Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        private static DoubleAnimation Animation(double from, double to) => new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        private sealed class TrafficRing : Grid
        {
            protected override AutomationPeer OnCreateAutomationPeer() => new RingPeer(this);
        }
        private sealed class RingPeer : FrameworkElementAutomationPeer
        {
            internal RingPeer(FrameworkElement owner) : base(owner) { }
            protected override string GetClassNameCore() => "TrafficRemainingRing";
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        }
        private static Geometry MakeArc(double ratio)
        {
            var geometry = new StreamGeometry();
            if (ratio <= 0) return geometry;
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(22, 2.5), false, false);
                if (ratio >= 1)
                {
                    context.ArcTo(new Point(22, 41.5), new Size(19.5, 19.5), 0, false, SweepDirection.Clockwise, true, false);
                    context.ArcTo(new Point(22, 2.5), new Size(19.5, 19.5), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    double angle = ratio * Math.PI * 2;
                    context.ArcTo(new Point(22 + 19.5 * Math.Sin(angle), 22 - 19.5 * Math.Cos(angle)),
                        new Size(19.5, 19.5), 0, ratio > .5, SweepDirection.Clockwise, true, false);
                }
            }
            geometry.Freeze(); return geometry;
        }
    }
}
