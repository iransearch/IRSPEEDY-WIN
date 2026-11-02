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
        // 44-DIP ring + 24-DIP padding + two border pixels needs 70 DIPs.
        private const double CardHeight = 70, EntranceGap = 10, ExpandedHeight = CardHeight + EntranceGap;
        private const double RingSize = 44, RingCenter = RingSize / 2, RingRadius = 19, RingStroke = 5;
        private const double LOW_TRAFFIC_THRESHOLD = 0.15;
        private readonly Border bar;
        private readonly TextBlock remainingValue, usedValue, totalValue, percentage;
        private readonly Grid ring;
        private readonly Path arc;
        private readonly Ellipse connectionDot;
        private readonly TranslateTransform translation = new TranslateTransform();
        private bool shownOnce, targetVisible, previousConnected, pendingEntrance;
        private long animationGeneration;
        private static Brush Color(string color) => (Brush)new BrushConverter().ConvertFromString(color);
        private static TextBlock Text(string value, double size, Brush color) => new TextBlock
            { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.NoWrap };

        public ResidentialTrafficBar()
        {
            Height = 0; Visibility = Visibility.Collapsed; ClipToBounds = true;
            FontFamily = new FontFamily("Segoe UI"); FlowDirection = FlowDirection.LeftToRight;
            Focusable = false; UseLayoutRounding = true; SnapsToDevicePixels = true;
            var body = new Grid { Height = ExpandedHeight, RenderTransform = translation };
            bar = new Border { Height = CardHeight, MinHeight = 68, Margin = new Thickness(0, EntranceGap, 0, 0), Padding = new Thickness(14, 12, 14, 12),
                CornerRadius = new CornerRadius(12), Background = Color("#202838"),
                BorderBrush = Color("#2D3850"), BorderThickness = new Thickness(1) };
            body.Children.Add(bar); Content = body;
            // Physical LTR order: remaining ring, LEFT, USED, TOTAL.
            // These physical columns never inherit the window's RTL mirroring.
            var layout = new Grid { FlowDirection = FlowDirection.LeftToRight };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.Child = layout;
            ring = new TrafficRing { Width = RingSize, Height = RingSize, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            ring.Children.Add(new Ellipse { Margin = new Thickness(.5), Fill = Brushes.Transparent, Style = null,
                Stroke = Color("#1FFFFFFF"), StrokeThickness = RingStroke });
            arc = new Path { Fill = Brushes.Transparent, Style = null, Stroke = Color("#F2C53D"),
                StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.Children.Add(arc);
            percentage = Text("", 11, Brushes.White); percentage.FontWeight = FontWeights.SemiBold;
            percentage.HorizontalAlignment = HorizontalAlignment.Center; ring.Children.Add(percentage);
            Grid.SetColumn(ring, 0); layout.Children.Add(ring);
            var stats = new Grid { FlowDirection = FlowDirection.LeftToRight };
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(stats, 1); layout.Children.Add(stats);
            connectionDot = new Ellipse { Width = 7, Height = 7, Fill = Color("#3FCF8E"),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            AddStat(stats, 0, Stat("LEFT", 20, FontWeights.Bold, out remainingValue, connectionDot));
            AddStat(stats, 1, Stat("USED", 15, FontWeights.SemiBold, out usedValue));
            AddStat(stats, 2, Stat("TOTAL", 15, FontWeights.SemiBold, out totalValue));
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
        private static StackPanel Stat(string label, double size, FontWeight weight, out TextBlock value, FrameworkElement marker = null)
        {
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
            if (marker != null) heading.Children.Add(marker);
            var title = Text(label, 11, Color("#A3ADC7")); title.FontWeight = FontWeights.Medium;
            heading.Children.Add(title); column.Children.Add(heading);
            value = Text("", size, Brushes.White); value.FontWeight = weight;
            value.TextTrimming = TextTrimming.CharacterEllipsis; value.Margin = new Thickness(0, 2, 0, 0);
            column.Children.Add(value); return column;
        }
        private static void AddStat(Grid stats, int index, FrameworkElement content)
        {
            var cell = new Border { BorderBrush = Color("#2D3850"),
                BorderThickness = index == 0 ? new Thickness(0) : new Thickness(1, 0, 0, 0),
                Padding = index == 0 ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 12, 0), Child = content };
            Grid.SetColumn(cell, index); stats.Children.Add(cell);
        }
        private static string RemainingLabel(ResidentialTraffic traffic) => traffic.Exhausted ? "0 MB" : traffic.RemainingText;
        private static string RemainingColor(ResidentialTraffic traffic) => traffic.Exhausted ? "#EF5D6A"
            : traffic.Total > 0 && traffic.Ratio <= LOW_TRAFFIC_THRESHOLD ? "#FB923C" : "#FFFFFF";
        private static string RemainingPercentage(ResidentialTraffic traffic)
        {
            if (traffic.Total == 0) return "∞";
            double percent = Math.Round(traffic.Ratio * 100);
            return traffic.Ratio > 0 && percent == 0 ? "<1%" : percent.ToString(CultureInfo.InvariantCulture) + "%";
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
                remainingValue.Text = RemainingLabel(traffic); usedValue.Text = traffic.UsedText; totalValue.Text = traffic.TotalText;
                string remainingColor = RemainingColor(traffic);
                remainingValue.Foreground = Color(remainingColor);
                arc.Stroke = Color(remainingColor == "#FFFFFF" ? "#F2C53D" : remainingColor);
                percentage.Text = RemainingPercentage(traffic);
                percentage.Foreground = traffic.Exhausted ? Color("#EF5D6A") : Brushes.White;
                string description = traffic.Total == 0 ? "Unlimited traffic"
                    : (percentage.Text == "<1%" ? "Less than 1" : percentage.Text.TrimEnd('%')) + " percent of traffic left";
                AutomationProperties.SetName(ring, description);
                AutomationProperties.SetName(this, remainingValue.Text + " left, " + traffic.UsedText + " used of " + traffic.TotalText);
                arc.Data = MakeArc(traffic.Ratio);
            }
            bar.BorderBrush = Color(connected ? "#8C3FCF8E" : "#2D3850");
            connectionDot.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
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
            var height = Animation(from, show ? ExpandedHeight : 0);
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
            animationGeneration++; Stop(); Height = show ? ExpandedHeight : 0; Opacity = show ? 1 : 0;
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
                context.BeginFigure(new Point(RingCenter, RingCenter - RingRadius), false, false);
                if (ratio >= 1)
                {
                    context.ArcTo(new Point(RingCenter, RingCenter + RingRadius), new Size(RingRadius, RingRadius), 0, false, SweepDirection.Clockwise, true, false);
                    context.ArcTo(new Point(RingCenter, RingCenter - RingRadius), new Size(RingRadius, RingRadius), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    double angle = ratio * Math.PI * 2;
                    context.ArcTo(new Point(RingCenter + RingRadius * Math.Sin(angle), RingCenter - RingRadius * Math.Cos(angle)),
                        new Size(RingRadius, RingRadius), 0, ratio > .5, SweepDirection.Clockwise, true, false);
                }
            }
            geometry.Freeze(); return geometry;
        }
    }
}

