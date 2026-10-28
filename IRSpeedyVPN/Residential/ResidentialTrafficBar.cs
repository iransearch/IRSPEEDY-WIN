using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IRSpeedyVPN.Residential
{
    public sealed class ResidentialTrafficBar : UserControl
    {
        private const double CardHeight = 100, EntranceGap = 10, ExpandedHeight = CardHeight + EntranceGap;
        private const double RingSize = 58, RingCenter = RingSize / 2, RingRadius = 25, RingStroke = 5;
        private readonly Border bar, badge, info, usageTrack, usageFill;
        private readonly Run remainingValue, usedValue;
        private readonly Grid ring;
        private readonly Path arc, arcGlow;
        private readonly TextBlock percentage;
        private double usedFraction;
        private readonly TranslateTransform translation = new TranslateTransform();
        private bool shownOnce, targetVisible, previousConnected, pendingEntrance;
        private long animationGeneration;
        private static Brush Color(string color) => (Brush)new BrushConverter().ConvertFromString(color);
        private static TextBlock Text(string value, double size, Brush color) => new TextBlock
            { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };

        public ResidentialTrafficBar()
        {
            Height = 0; Visibility = Visibility.Collapsed; ClipToBounds = true;
            Focusable = false; UseLayoutRounding = true; SnapsToDevicePixels = true;
            var body = new Grid { Height = ExpandedHeight, RenderTransform = translation };
            bar = new Border { Height = CardHeight, Margin = new Thickness(0, EntranceGap, 0, 0), Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(18), Background = CardBackground(),
                BorderBrush = Color("#6A98FF"), BorderThickness = new Thickness(1) };
            body.Children.Add(bar); Content = body;
            // Match the supplied reference: ring left, labels middle, usage/info right.
            // These physical columns never inherit the window's RTL mirroring.
            var layout = new Grid { FlowDirection = FlowDirection.LeftToRight };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bar.Child = layout;
            ring = new TrafficRing { Width = RingSize, Height = RingSize, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            ring.Children.Add(new Ellipse { Margin = new Thickness(1.5), Fill = Brushes.Transparent, Style = null,
                Stroke = Color("#3C568E"), StrokeThickness = RingStroke });
            arcGlow = new Path { Fill = Brushes.Transparent, Style = null, Stroke = Color("#F2C53D"),
                StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect { Color = Colors.Gold, BlurRadius = 10, ShadowDepth = 0, Opacity = .5 } };
            ring.Children.Add(arcGlow);
            arc = new Path { Fill = Brushes.Transparent, Style = null, Stroke = YellowGradient(),
                StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.Children.Add(arc);
            ring.Children.Add(new Path { Width = 20, Height = 23, Stretch = Stretch.Uniform, Style = null,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = Brushes.Transparent, Stroke = Color("#A0BAFF"), StrokeThickness = 1.6,
                Data = Geometry.Parse("M1,5 C1,0 19,0 19,5 C19,10 1,10 1,5 M1,5 V18 C1,24 19,24 19,18 V5 M1,12 C1,17 19,17 19,12") });
            Grid.SetColumn(ring, 0); Grid.SetRowSpan(ring, 2); layout.Children.Add(ring);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(labels, 1); layout.Children.Add(labels);
            var remaining = Text("", 22, Brushes.White); remaining.FontWeight = FontWeights.Bold;
            remaining.FlowDirection = FlowDirection.LeftToRight; remaining.TextTrimming = TextTrimming.CharacterEllipsis;
            remaining.Inlines.Add(remainingValue = new Run());
            remaining.Inlines.Add(new Run(" باقی‌مانده") { FontSize = 11.5, FontWeight = FontWeights.Normal,
                Foreground = Color("#A4B7ED"), FlowDirection = FlowDirection.RightToLeft });
            labels.Children.Add(remaining);
            var used = Text("", 11.5, Color("#95AAE5")); used.FlowDirection = FlowDirection.LeftToRight;
            used.TextTrimming = TextTrimming.CharacterEllipsis; used.Margin = new Thickness(0, 4, 0, 0);
            used.Inlines.Add(usedValue = new Run());
            used.Inlines.Add(new Run(" مصرف شده") { FlowDirection = FlowDirection.RightToLeft }); labels.Children.Add(used);
            var side = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(side, 2); layout.Children.Add(side);
            info = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                BorderBrush = Color("#93ABE9"), BorderThickness = new Thickness(1.4), HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = "حلقه: ترافیک باقی‌مانده · نوار و درصد: ترافیک مصرف‌شده" };
            var infoText = Text("i", 13, Color("#A8BBF5")); infoText.FontWeight = FontWeights.Bold;
            infoText.HorizontalAlignment = HorizontalAlignment.Center; info.Child = infoText; side.Children.Add(info);
            badge = new Border { CornerRadius = new CornerRadius(20), Background = Color("#243FCF8E"),
                Padding = new Thickness(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right };
            var status = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft };
            status.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = Color("#3FCF8E"),
                Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            var statusText = Text("در حال استفاده", 10, Color("#3FCF8E")); statusText.FontWeight = FontWeights.SemiBold;
            status.Children.Add(statusText); badge.Child = status; side.Children.Add(badge);
            percentage = Text("", 15, Color("#FFE46A")); percentage.FontWeight = FontWeights.Bold;
            percentage.Margin = new Thickness(0, 5, 0, 0); percentage.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(percentage);
            usageTrack = new Border { Height = 6, Margin = new Thickness(0, 9, 0, 0), Background = Color("#405E7FCA"),
                CornerRadius = new CornerRadius(3), ClipToBounds = true };
            usageFill = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3),
                Background = YellowGradient() };
            usageTrack.Child = usageFill; Grid.SetColumn(usageTrack, 1); Grid.SetColumnSpan(usageTrack, 2); Grid.SetRow(usageTrack, 1);
            usageTrack.SizeChanged += (sender, args) => UpdateUsageFill(false);
            layout.Children.Add(usageTrack);
            Loaded += (s, e) =>
            {
                SystemParameters.StaticPropertyChanged += ParametersChanged;
                UpdateUsageFill(false);
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
                remainingValue.Text = traffic.RemainingText; usedValue.Text = traffic.UsageText;
                usedFraction = traffic.Total == 0 ? 0 : 1 - traffic.Ratio;
                percentage.Text = traffic.Total == 0 ? "∞" : Math.Round(usedFraction * 100).ToString(CultureInfo.InvariantCulture) + "%";
                string description = traffic.Total == 0 ? "Unlimited traffic" : Math.Round(traffic.Ratio * 100).ToString(CultureInfo.InvariantCulture) + " percent of traffic left";
                AutomationProperties.SetName(ring, description); AutomationProperties.SetName(this, description + "; " + traffic.RemainingText + "; " + traffic.UsageText);
                arc.Data = arcGlow.Data = MakeArc(traffic.Ratio);
                UpdateUsageFill(shownOnce && IsLoaded && SystemParameters.ClientAreaAnimation);
            }
            bar.BorderBrush = Color(connected ? "#8C3FCF8E" : "#6A98FF");
            badge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            info.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
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
        private static Brush CardBackground()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#193A85"), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#101F50"), .5));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1B3B91"), 1));
            brush.Freeze(); return brush;
        }
        private static Brush YellowGradient()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFCA37"), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFE969"), .58));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFCF36"), 1));
            brush.Freeze(); return brush;
        }
        private void UpdateUsageFill(bool animate)
        {
            double width = Math.Max(0, usageTrack.ActualWidth) * usedFraction;
            double from = usageFill.ActualWidth;
            usageFill.BeginAnimation(WidthProperty, null); usageFill.Width = width;
            if (animate) usageFill.BeginAnimation(WidthProperty, Animation(from, width));
        }
        private void Stop()
        {
            BeginAnimation(HeightProperty, null); BeginAnimation(OpacityProperty, null);
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            usageFill.BeginAnimation(WidthProperty, null);
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

