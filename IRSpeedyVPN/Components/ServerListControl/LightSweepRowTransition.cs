using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IRSpeedyVPN.Components.ServerListControl
{
    [TemplatePart(Name = "PART_Surface", Type = typeof(Grid))]
    [TemplatePart(Name = "PART_Old", Type = typeof(ContentPresenter))]
    [TemplatePart(Name = "PART_New", Type = typeof(ContentPresenter))]
    [TemplatePart(Name = "PART_Band", Type = typeof(Rectangle))]
    public sealed class LightSweepRowTransition : Control
    {
        public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(nameof(Slot), typeof(ServerDisplaySlot),
            typeof(LightSweepRowTransition), new PropertyMetadata(null, SlotChanged));
        public static readonly DependencyProperty RowTemplateProperty = DependencyProperty.Register(nameof(RowTemplate), typeof(DataTemplate), typeof(LightSweepRowTransition));
        public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(TimeSpan),
            typeof(LightSweepRowTransition), new PropertyMetadata(TimeSpan.FromMilliseconds(520)), PositiveTime);
        public static readonly DependencyProperty BandWidthProperty = DependencyProperty.Register(nameof(BandWidth), typeof(double),
            typeof(LightSweepRowTransition), new PropertyMetadata(100d), value => (double)value > 0 && !double.IsInfinity((double)value));
        public static readonly DependencyProperty BandColorProperty = DependencyProperty.Register(nameof(BandColor), typeof(Color),
            typeof(LightSweepRowTransition), new PropertyMetadata(Color.FromArgb(95, 192, 243, 255)));
        public static readonly DependencyProperty EasingProperty = DependencyProperty.Register(nameof(Easing), typeof(IEasingFunction),
            typeof(LightSweepRowTransition), new PropertyMetadata(null));
        private static readonly DependencyProperty RevealMarkerProperty = DependencyProperty.Register("RevealMarker", typeof(bool),
            typeof(LightSweepRowTransition), new PropertyMetadata(false, RevealMarkerChanged));
        public static readonly RoutedEvent ActivityChangedEvent = EventManager.RegisterRoutedEvent("ActivityChanged",
            RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LightSweepRowTransition));

        public ServerDisplaySlot Slot { get => (ServerDisplaySlot)GetValue(SlotProperty); set => SetValue(SlotProperty, value); }
        public DataTemplate RowTemplate { get => (DataTemplate)GetValue(RowTemplateProperty); set => SetValue(RowTemplateProperty, value); }
        public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
        public double BandWidth { get => (double)GetValue(BandWidthProperty); set => SetValue(BandWidthProperty, value); }
        public Color BandColor { get => (Color)GetValue(BandColorProperty); set => SetValue(BandColorProperty, value); }
        public IEasingFunction Easing { get => (IEasingFunction)GetValue(EasingProperty); set => SetValue(EasingProperty, value); }

        private Grid _surface;
        private ContentPresenter _old, _next;
        private Rectangle _band;
        private TranslateTransform _shift;
        private readonly RectangleGeometry _oldClip = new RectangleGeometry();
        private readonly RectangleGeometry _nextClip = new RectangleGeometry();
        private readonly TranslateTransform _oldCut = new TranslateTransform();
        private readonly TranslateTransform _nextCut = new TranslateTransform();
        private Storyboard _storyboard;
        private ServerDisplaySlot _attachedSlot;
        private ServerRowTransition _active;
        private ServerDisplaySlot _activeSlot;
        public bool IsAnimatingSurface { get; private set; }

        public LightSweepRowTransition()
        {
            FlowDirection = FlowDirection.LeftToRight; // physical coordinates, independent of RTL parent
            Loaded += (s, e) => Attach();
            Unloaded += (s, e) => Detach();
            IsVisibleChanged += (s, e) => { if (!IsVisible) FinishActive(); };
            SizeChanged += (s, e) => { FinishActive(); SetRoundedClip(); };
        }

        private static bool PositiveTime(object value) => (TimeSpan)value > TimeSpan.Zero;
        private static void SlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var row = (LightSweepRowTransition)d;
            row.Detach();
            if (row.IsLoaded) row.Attach();
            row.ShowFinal();
        }

        public override void OnApplyTemplate()
        {
            Detach();
            base.OnApplyTemplate();
            _surface = GetTemplateChild("PART_Surface") as Grid;
            _old = GetTemplateChild("PART_Old") as ContentPresenter;
            _next = GetTemplateChild("PART_New") as ContentPresenter;
            _band = GetTemplateChild("PART_Band") as Rectangle;
            if (_band?.Effect != null && _band.Effect.CanFreeze) _band.Effect.Freeze();
            SetRoundedClip();
            ShowFinal();
            if (IsLoaded) Attach();
        }

        private void Attach()
        {
            if (ReferenceEquals(_attachedSlot, Slot)) return;
            _attachedSlot = Slot;
            if (_attachedSlot != null)
            {
                _attachedSlot.TransitionRequested += Start;
                _attachedSlot.PropertyChanged += SlotPropertyChanged;
            }
            ShowFinal();
        }

        private void Detach()
        {
            var slot = _attachedSlot;
            if (slot == null) return;
            // Detach before completing: completion can synchronously start a newer wave.
            slot.TransitionRequested -= Start;
            slot.PropertyChanged -= SlotPropertyChanged;
            _attachedSlot = null;
            FinishActive();
        }

        private void SlotPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Slot?.Transition == null) { StopClock(); ShowFinal(); }
        }

        private bool CanAnimate()
        {
            if (!IsLoaded || !IsVisible || ActualWidth <= 0 || _old == null || _next == null || _band == null
                || !SystemParameters.ClientAreaAnimation || Window.GetWindow(this)?.WindowState == WindowState.Minimized)
                return false;
            // ItemsControl is deliberately not virtualized; avoid clocks for clipped rows.
            DependencyObject ancestor = VisualTreeHelper.GetParent(this);
            while (ancestor != null && !(ancestor is ScrollViewer)) ancestor = VisualTreeHelper.GetParent(ancestor);
            var scroller = ancestor as ScrollViewer;
            if (scroller == null) return true;
            var bounds = TransformToAncestor(scroller).TransformBounds(new Rect(RenderSize));
            return bounds.Bottom > 0 && bounds.Top < scroller.ActualHeight;
        }

        private void Start(ServerDisplaySlot slot, ServerRowTransition transition)
        {
            if (!CanAnimate()) { slot.Finish(transition); return; }
            StopClock();
            _active = transition;
            _activeSlot = slot;
            SetMotionActive(true);
            _old.Content = transition.From;
            _old.Visibility = transition.From == null ? Visibility.Hidden : Visibility.Visible;
            _next.Content = transition.To;
            // A null item must not instantiate the row template's empty flag/radio artwork.
            _next.Visibility = transition.To == null ? Visibility.Hidden : Visibility.Visible;
            _old.CacheMode = new BitmapCache();
            _next.CacheMode = new BitmapCache();
            var duration = transition.ChangesIdentity ? Duration : TimeSpan.FromMilliseconds(150);
            var easing = Easing ?? new CubicEase { EasingMode = EasingMode.EaseInOut };
            var plan = new LightSweepAnimationPlan(ActualWidth, BandWidth, Padding.Left, duration, easing.Ease);
            var storyboard = new Storyboard { Duration = duration, FillBehavior = FillBehavior.HoldEnd };
            if (transition.ChangesIdentity)
            {
                // Fixed rectangles with animated transforms: no managed per-frame
                // callback, rectangle allocation or layout work as pings arrive.
                _oldClip.Rect = new Rect(0, 0, plan.Travel, ActualHeight);
                _nextClip.Rect = new Rect(-plan.Travel, 0, plan.Travel, ActualHeight);
                _oldCut.X = plan.CutFrom;
                _nextCut.X = plan.CutFrom;
                _oldClip.Transform = _oldCut;
                _nextClip.Transform = _nextCut;
                _old.Clip = _oldClip;
                _next.Clip = _nextClip;
                _band.Width = BandWidth;
                _band.Height = ActualHeight + 32;
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                brush.GradientStops.Add(new GradientStop(BandColor, .43));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(BandColor.A, 255, 255, 255), .5));
                brush.GradientStops.Add(new GradientStop(BandColor, .57));
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                brush.Freeze();
                _band.Fill = brush;
                _shift = new TranslateTransform(plan.BandFrom, 0);
                var transforms = new TransformGroup();
                var skew = new SkewTransform(-14, 0, 0, _band.Height / 2);
                skew.Freeze();
                transforms.Children.Add(skew);
                transforms.Children.Add(_shift);
                _band.RenderTransform = transforms;
                _band.CacheMode = new BitmapCache();
                _band.Visibility = Visibility.Visible;
                // Storyboard clocks snapshot their timelines. A Freezable stored
                // directly in Target is copied with that snapshot; it is no longer
                // the transform attached to the row. Start at a FrameworkElement
                // and resolve the existing, mutable transform through its property.
                AddMotion(storyboard, _band, new PropertyPath("(0).(1)[1].(2)",
                    RenderTransformProperty, TransformGroup.ChildrenProperty, TranslateTransform.XProperty),
                    plan.BandFrom, plan.BandTo, duration, easing);
                var clipPath = new PropertyPath("(0).(1).(2)", ClipProperty, Geometry.TransformProperty, TranslateTransform.XProperty);
                AddMotion(storyboard, _old, clipPath, plan.CutFrom, plan.CutTo, duration, easing);
                AddMotion(storyboard, _next, clipPath, plan.CutFrom, plan.CutTo, duration, easing);
            }
            else
            {
                AddMotion(storyboard, _old, new PropertyPath(OpacityProperty), 1, 0, duration, easing);
                AddMotion(storyboard, _next, new PropertyPath(OpacityProperty), 0, 1, duration, easing);
            }
            // This marker and the native visual timelines share one clock. Invert
            // the easing once to commit identity exactly at the physical midpoint.
            var marker = new ObjectAnimationUsingKeyFrames { Duration = duration };
            marker.KeyFrames.Add(new DiscreteObjectKeyFrame(false, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            marker.KeyFrames.Add(new DiscreteObjectKeyFrame(true, KeyTime.FromTimeSpan(plan.RevealTime)));
            Storyboard.SetTarget(marker, this);
            Storyboard.SetTargetProperty(marker, new PropertyPath(RevealMarkerProperty));
            storyboard.Children.Add(marker);
            storyboard.Completed += (s, e) =>
            {
                if (ReferenceEquals(_active, transition)) slot.Finish(transition);
            };
            _storyboard = storyboard;
            storyboard.Begin(this, true);
        }

        private static void AddMotion(Storyboard storyboard, FrameworkElement target, PropertyPath property,
            double from, double to, TimeSpan duration, IEasingFunction easing)
        {
            var animation = new DoubleAnimation(from, to, duration) { EasingFunction = easing };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private static void RevealMarkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var row = (LightSweepRowTransition)d;
            if ((bool)e.NewValue && row._active != null) row._activeSlot?.Reveal(row._active);
        }

        private void SetMotionActive(bool active)
        {
            if (IsAnimatingSurface == active) return;
            IsAnimatingSurface = active;
            RaiseEvent(new RoutedEventArgs(ActivityChangedEvent, this));
        }

        internal static bool IsTransitionSurface(DependencyObject element)
        {
            for (var ancestor = VisualTreeHelper.GetParent(element); ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is LightSweepRowTransition row) return row.IsAnimatingSurface;
            return false;
        }

        private void FinishActive()
        {
            var transition = _active;
            var slot = _activeSlot;
            StopClock();
            if (transition != null) slot?.Finish(transition);
            if (_active == null) ShowFinal();
        }

        private void StopClock()
        {
            _active = null;
            _activeSlot = null;
            _storyboard?.Remove(this);
            _storyboard = null;
            SetValue(RevealMarkerProperty, false);
        }

        private void ShowFinal()
        {
            if (_old == null) return;
            _old.Content = Slot?.DisplayedServer;
            _old.Visibility = Slot?.DisplayedServer == null ? Visibility.Hidden : Visibility.Visible;
            _old.Clip = null;
            _old.Opacity = 1;
            _old.CacheMode = null;
            if (_next != null)
            {
                _next.Content = null;
                _next.Clip = null;
                _next.Opacity = 1;
                _next.CacheMode = null;
                _next.Visibility = Visibility.Collapsed;
            }
            if (_band != null)
            {
                _band.Visibility = Visibility.Collapsed;
                _band.CacheMode = null;
            }
            SetMotionActive(false);
        }

        private void SetRoundedClip()
        {
            if (_surface == null) return;
            var clip = new RectangleGeometry(new Rect(RenderSize), 14, 14);
            clip.Freeze();
            _surface.Clip = clip;
        }
    }
}
