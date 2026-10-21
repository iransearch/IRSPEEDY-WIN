using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace IRSpeedyVPN.Components.ServerListControl
{
    // One UI-owned timeline and ONE painted band for the entire ItemsControl.
    internal sealed class ServerListSweepAnimator
    {
        private readonly ServerListViewModel _model;
        private readonly ItemsControl _items;
        private readonly ScrollViewer _scroller;
        private readonly ServerListCometOverlay _overlay;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Action<int, double> _reveal;
        private ServerSortPass _pass;
        private ServerCometTimeline _timeline;
        private Button _row;
        private FrameworkElement[] _groups;
        private Rect[] _bounds;
        private Geometry _rowClip;
        private double _started, _rowLeft;
        private bool _subscribed;
        private DispatcherOperation _prepare;
        private Application _application;
        public ServerListSweepAnimator(ServerListViewModel model, ItemsControl items, ScrollViewer scroller, ServerListCometOverlay overlay)
        {
            _model = model; _items = items; _scroller = scroller; _overlay = overlay; _reveal = Reveal;
            model.WorkAvailable += QueuePrepare; model.Resetting += Reset;
            scroller.ScrollChanged += (s, e) => RefreshGeometry();
            overlay.SizeChanged += (s, e) => { if (_pass != null) _model.CompleteImmediately(); };
        }
        public void Resume()
        {
            if (_application != null) return;
            _application = Application.Current;
            if (_application != null) _application.Exit += ApplicationExit;
        }
        public void Suspend()
        {
            _model.CompleteImmediately();
            if (_application != null) _application.Exit -= ApplicationExit;
            _application = null;
        }
        private void ApplicationExit(object sender, ExitEventArgs e) => Suspend();
        private void QueuePrepare()
        {
            if (_pass != null || _prepare?.Status == DispatcherOperationStatus.Pending) return;
            // Measurement/re-planning runs outside the frame callback.
            _prepare = _items.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Prepare));
        }
        private void Prepare()
        {
            _prepare = null;
            if (_pass != null) return;
            if (!_items.IsLoaded || !_items.IsVisible || !SystemParameters.ClientAreaAnimation
                || Window.GetWindow(_items)?.WindowState == WindowState.Minimized)
            { _model.CompleteImmediately(); return; }
            while ((_pass = _model.TryBeginPass()) != null)
            {
                var container = _items.ItemContainerGenerator.ContainerFromItem(_pass.Slot);
                _row = FindRow(container, _pass.Slot);
                if (_row == null || !_row.IsLoaded || !IsInViewport(_row))
                { _model.Finish(_pass); _pass = null; continue; }
                _groups = new FrameworkElement[5]; _bounds = new Rect[5]; FindGroups(_row, _groups);
                bool valid = _row.ActualWidth > 0;
                foreach (var element in _groups) valid &= element != null;
                if (!valid) { _model.Finish(_pass); _pass = null; continue; }
                var rowRect = Bounds(_row); _rowLeft = rowRect.Left;
                _rowClip = MakeClip(_row, rowRect);
                var centers = new double[5];
                for (int i = 0; i < 5; i++)
                { _bounds[i] = Bounds(_groups[i]); centers[i] = _bounds[i].Left + _bounds[i].Width / 2 - rowRect.Left; }
                _timeline = new ServerCometTimeline(rowRect.Width, centers);
                _overlay.BandRect = new Rect(0, -32, ServerCometTiming.BAND_WIDTH_DIP, rowRect.Height + 64);
                var skew = new SkewTransform(ServerCometTiming.SKEW_DEG, 0, 0, rowRect.Height / 2);
                skew.Freeze(); _overlay.BandSkew = skew; _overlay.BandClip = _rowClip;
                _overlay.BandShift.Y = rowRect.Top;
                _overlay.BandShift.X = rowRect.Left + ServerCometTiming.BandLeft(0, rowRect.Width);
                _overlay.ShowBand = true; StartClock(); _started = _clock.Elapsed.TotalMilliseconds;
                _overlay.InvalidateVisual(); return;
            }
            StopIfIdle();
        }
        private void StartClock()
        {
            _clock.Start();
            if (_subscribed) return;
            CompositionTarget.Rendering += Rendering; _subscribed = true;
        }
        private void Rendering(object sender, EventArgs e)
        {
            // Only prepared layout, immutable presentation values, transforms and
            // opacity. No backend, sort, image decoding, I/O, waits or Invoke.
            double now = _clock.Elapsed.TotalMilliseconds;
            if (!SystemParameters.ClientAreaAnimation) { _model.CompleteImmediately(); return; }
            if (_pass != null)
            {
                if (!_row.IsLoaded || !ReferenceEquals(_row.DataContext, _pass.Slot)) FinishPass();
                else
                {
                    double elapsed = now - _started;
                    _timeline.Advance(elapsed, _reveal);
                    _overlay.BandShift.X = _rowLeft + ServerCometTiming.BandLeft(elapsed, _timeline.Width);
                    if (elapsed >= ServerCometTiming.SORT_PASS_MS) FinishPass();
                }
            }
            for (int i = _overlay.Flashes.Count - 1; i >= 0; i--)
            {
                var flash = _overlay.Flashes[i];
                double t = Math.Max(0, (now - flash.Started) / ServerCometTiming.FLASH_MS);
                var current = flash.Slot.GetPart(flash.Part);
                if (t >= 1 || !flash.Element.IsLoaded || !ReferenceEquals(flash.OwnerRow.DataContext, flash.Slot)
                    || current?.Identity != flash.Snapshot.Identity || PartChanged(flash.Snapshot, current, flash.Part))
                { flash.Element.Opacity = 1; _overlay.Flashes.RemoveAt(i); continue; }
                flash.Element.Opacity = .2 + .8 * t;
                flash.Brightness = .18 * (1 - t) * (1 - t);
            }
            _overlay.InvalidateVisual(); StopIfIdle();
        }
        private void Reveal(int index, double crossing)
        {
            var part = (ServerRowPart)index; var before = _pass.Slot.GetPart(part);
            _model.Reveal(_pass, part);
            if (_pass.Target == null || !PartChanged(before, _pass.Target, part)) return;
            if (part == ServerRowPart.Badge && !_pass.Target.IsLastSuccessfulConnection) return;
            var element = _groups[index];
            for (int i = _overlay.Flashes.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_overlay.Flashes[i].Element, element)) _overlay.Flashes.RemoveAt(i);
            _overlay.Flashes.Add(new ServerCometFlash { Element = element, OwnerRow = _row, Slot = _pass.Slot, Part = part,
                Snapshot = _pass.Target, Started = _started + crossing, Bounds = _bounds[index], Clip = _rowClip });
        }
        private void FinishPass()
        {
            _model.Finish(_pass); _pass = null; _timeline = null; _overlay.ShowBand = false; QueuePrepare();
        }
        private void StopIfIdle()
        {
            if (_pass != null || _overlay.Flashes.Count != 0 || _prepare?.Status == DispatcherOperationStatus.Pending) return;
            if (_subscribed) CompositionTarget.Rendering -= Rendering;
            _subscribed = false; _clock.Stop();
        }
        private void Reset()
        {
            _prepare?.Abort(); _prepare = null; _pass = null; _timeline = null;
            if (_subscribed) CompositionTarget.Rendering -= Rendering;
            _subscribed = false; _clock.Reset();
            foreach (var flash in _overlay.Flashes) flash.Element.Opacity = 1;
            _overlay.Flashes.Clear(); _overlay.ShowBand = false; _overlay.BandClip = null; _overlay.InvalidateVisual();
        }
        private void RefreshGeometry()
        {
            if (_pass != null && _row.IsLoaded)
            {
                var rect = Bounds(_row); _rowLeft = rect.Left;
                _rowClip = MakeClip(_row, rect); _overlay.BandClip = _rowClip; _overlay.BandShift.Y = rect.Top;
            }
            foreach (var flash in _overlay.Flashes)
            {
                if (!flash.Element.IsLoaded) continue;
                flash.Bounds = Bounds(flash.Element);
                var row = FindParentRow(flash.Element);
                if (row != null) flash.Clip = MakeClip(row, Bounds(row));
            }
            _overlay.InvalidateVisual();
        }
        private Rect Bounds(FrameworkElement element)
        {
            double w = element.ActualWidth, h = element.ActualHeight;
            if (w == 0 && !double.IsNaN(element.Width)) w = element.Width;
            if (h == 0 && !double.IsNaN(element.Height)) h = element.Height;
            // Transform includes ALL inherited RTL mirrors. Overlay is physical LTR.
            return element.TransformToVisual(_overlay).TransformBounds(new Rect(0, 0, Math.Max(0, w), Math.Max(0, h)));
        }
        private bool IsInViewport(Button row)
        {
            var rect = row.TransformToVisual(_scroller).TransformBounds(new Rect(row.RenderSize));
            return rect.Bottom > 0 && rect.Top < _scroller.ActualHeight;
        }
        private Geometry MakeClip(Button row, Rect rect)
        {
            var border = row.Template.FindName("CardSurface", row) as Border;
            double radius = border?.CornerRadius.TopLeft ?? 14;
            var clip = new CombinedGeometry(GeometryCombineMode.Intersect,
                new RectangleGeometry(rect, radius, radius), new RectangleGeometry(Bounds(_scroller)));
            clip.Freeze(); return clip;
        }
        private static Button FindRow(DependencyObject root, ServerDisplaySlot slot)
        {
            if (root == null) return null;
            if (root is Button row && ReferenceEquals(row.DataContext, slot)) return row;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindRow(VisualTreeHelper.GetChild(root, i), slot); if (found != null) return found; }
            return null;
        }
        private static Button FindParentRow(DependencyObject element)
        {
            for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is Button row) return row;
            return null;
        }
        private static void FindGroups(DependencyObject root, FrameworkElement[] groups)
        {
            var part = ServerCometPart.GetPart(root);
            if (part != ServerRowPart.None && root is FrameworkElement element) groups[(int)part] = element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) FindGroups(VisualTreeHelper.GetChild(root, i), groups);
        }
        private static bool PartChanged(ServerItem old, ServerItem next, ServerRowPart part)
        {
            if (old == null || next == null) return old != next;
            switch (part)
            {
                case ServerRowPart.Flag: return !ReferenceEquals(old.FlagSource, next.FlagSource) || old.Initials != next.Initials;
                case ServerRowPart.Name: return old.CountryName != next.CountryName;
                case ServerRowPart.Signal: return old.SignalText != next.SignalText || !ReferenceEquals(old.SignalBrush, next.SignalBrush);
                case ServerRowPart.Selection: return old.IsSelectedCountry != next.IsSelectedCountry;
                case ServerRowPart.Badge: return old.IsLastSuccessfulConnection != next.IsLastSuccessfulConnection;
                default: return false;
            }
        }
    }
}
