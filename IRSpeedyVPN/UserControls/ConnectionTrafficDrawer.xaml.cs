using IRSpeedyVPN.Common;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IRSpeedyVPN.UserControls
{
    public partial class ConnectionTrafficDrawer : Window
    {
        private const double DesignWidth = 440;
        private readonly Window connectionWindow;
        private readonly FrameworkElement connectionAnchor;
        private HwndSource ownerSource, drawerSource;
        private bool placing, placementQueued, disposed, prepared, preparing;
        private bool motionAttached, motionPrimed, motionOpening;
        private double motionFrom, motionTo;
        private int motionDurationMs;
        private long motionStartTicks;
        private DrawerState state = DrawerState.Closed;

        private enum DrawerState
        {
            Closed,
            Opening,
            Open,
            Closing
        }

        internal ConnectionTrafficPanel Panel => TrafficPanel;
        internal bool IsDisposed => disposed;
        internal bool IsOpeningOrOpen => state == DrawerState.Opening || state == DrawerState.Open;
        internal bool IsTransitioning => state == DrawerState.Opening || state == DrawerState.Closing;
        internal event EventHandler DrawerClosed;
        internal event EventHandler OpenStateChanged;

        internal ConnectionTrafficDrawer(Window owner, FrameworkElement anchor)
        {
            connectionWindow = owner ?? throw new ArgumentNullException(nameof(owner));
            connectionAnchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            InitializeComponent();
            Owner = owner;
            owner.LocationChanged += OwnerGeometryChanged;
            owner.SizeChanged += OwnerSizeChanged;
            owner.StateChanged += OwnerStateChanged;
            owner.IsVisibleChanged += OwnerVisibilityChanged;
            owner.PreviewKeyDown += Drawer_PreviewKeyDown;
            owner.Closed += OwnerClosed;
            SourceInitialized += DrawerSourceInitialized;
            Closed += Drawer_Closed;
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            ownerSource = HwndSource.FromHwnd(ownerHandle);
            ownerSource?.AddHook(WindowMessage);
            Log("created");
        }

        internal void Prepare()
        {
            if (disposed || prepared || preparing) return;
            if (!connectionWindow.IsVisible || !connectionAnchor.IsVisible
                || connectionWindow.WindowState == WindowState.Minimized) return;

            preparing = true;
            var clock = Stopwatch.StartNew();
            Log("preload-start");
            try
            {
                TrafficPanel.SetUpdatesEnabled(false);
                TrafficPanel.RefreshTraffic();
                DrawerTranslation.X = DesignWidth;
                DrawerSurface.IsHitTestVisible = false;
                DrawerSurface.IsEnabled = false;

                if (!PlaceBesideConnection())
                {
                    Log("preload-placement-deferred");
                    return;
                }

                // Force the first HWND creation, templates, DataGrid generation and layout
                // before the user clicks. The zero-opacity show is never user-visible.
                Opacity = 0;
                if (!IsVisible) Show();
                TrafficPanel.ApplyTemplate();
                UpdateLayout();
                TrafficPanel.UpdateLayout();
                Hide();
                Opacity = 1;
                prepared = true;
                Log("preload-ready elapsedMs=" + clock.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                try { if (IsVisible) Hide(); } catch { }
                Opacity = 1;
                Log("preload-error exception=" + ex.GetType().Name + " elapsedMs=" + clock.ElapsedMilliseconds);
            }
            finally
            {
                preparing = false;
            }
        }

        internal void SetOpen(bool open)
        {
            Log("click target=" + (open ? "open" : "closed"));
            if (disposed) return;
            if (open && (!connectionWindow.IsVisible || !connectionAnchor.IsVisible
                || connectionWindow.WindowState == WindowState.Minimized))
            {
                Log("open-rejected ownerUnavailable=True");
                return;
            }

            // Do not queue or reverse repeated clicks. One transition owns the drawer
            // until it reaches its endpoint; the toggle is disabled by the host meanwhile.
            if (IsTransitioning)
            {
                Log("click-ignored transition=True");
                return;
            }
            if ((open && state == DrawerState.Open) || (!open && state == DrawerState.Closed)) return;

            if (open && !prepared) Prepare();
            TrafficPanel.DismissReset();
            TrafficPanel.SetUpdatesEnabled(false);

            double from = DrawerTranslation.X;
            StopMotion();
            DrawerTranslation.X = from;
            SetState(open ? DrawerState.Opening : DrawerState.Closing);

            if (open && !IsVisible)
            {
                // The first rendered frame is intentionally fully clipped. Motion starts
                // on the following CompositionTarget frame, so Show() can never reveal
                // the completed panel in one jump.
                from = DesignWidth;
                DrawerTranslation.X = DesignWidth;
                if (!PlaceBesideConnection()) { HideDrawer("placement-failed"); return; }
                Show();
            }
            if (!IsVisible) { HideDrawer("not-visible"); return; }

            double target = open ? 0 : DesignWidth;
            double fraction = Math.Min(1, Math.Abs(target - from) / DesignWidth);
            if (fraction < 0.001) { FinishMotion(open); return; }

            DrawerSurface.IsHitTestVisible = false;
            DrawerSurface.IsEnabled = false;
            StartSlide(open, from, target, (int)Math.Round((open ? 460 : 360) * fraction));
        }

        private void StartSlide(bool open, double from, double target, int durationMs)
        {
            StopMotion();
            motionOpening = open;
            motionFrom = from;
            motionTo = target;
            motionDurationMs = Math.Max(1, durationMs);
            motionPrimed = false;
            motionAttached = true;
            DrawerTranslation.X = from;
            CompositionTarget.Rendering += MotionFrame;
            Log("animation-armed from=" + Math.Round(from, 1) + " target=" + target
                + " durationMs=" + motionDurationMs);
        }

        private void MotionFrame(object sender, EventArgs e)
        {
            if (!motionAttached || disposed) { StopMotion(); return; }

            // Frame zero establishes the hidden start position after Show(). This is
            // what makes the drawer visibly emerge from the connection window edge.
            if (!motionPrimed)
            {
                motionPrimed = true;
                motionStartTicks = Stopwatch.GetTimestamp();
                DrawerTranslation.X = motionFrom;
                Log("animation-first-frame x=" + Math.Round(motionFrom, 1));
                return;
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - motionStartTicks) * 1000.0 / Stopwatch.Frequency;
            double progress = Math.Min(1, elapsedMs / motionDurationMs);
            // Cubic ease-out: fast response at the edge, soft landing at the endpoint.
            double eased = 1 - Math.Pow(1 - progress, 3);
            DrawerTranslation.X = motionFrom + (motionTo - motionFrom) * eased;

            if (progress < 1) return;
            bool open = motionOpening;
            DrawerTranslation.X = motionTo;
            StopMotion();
            FinishMotion(open);
        }

        private void FinishMotion(bool open)
        {
            DrawerTranslation.X = open ? 0 : DesignWidth;
            DrawerSurface.IsHitTestVisible = open;
            DrawerSurface.IsEnabled = open;
            if (open)
            {
                SetState(DrawerState.Open);
                TrafficPanel.SetUpdatesEnabled(true);
                Log("animation-complete target=open");
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (!disposed && state == DrawerState.Open && IsVisible)
                        Log("render-ready width=" + Math.Round(ActualWidth, 1)
                            + " height=" + Math.Round(ActualHeight, 1));
                }));
            }
            else
            {
                Log("animation-complete target=closed");
                HideDrawer("animation-complete");
            }
        }

        private void HideDrawer(string reason)
        {
            StopMotion();
            SetState(DrawerState.Closed);
            DrawerTranslation.X = DesignWidth;
            DrawerSurface.IsHitTestVisible = false;
            DrawerSurface.IsEnabled = false;
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanel.DismissReset();
            if (IsVisible) Hide();
            Log("hidden reason=" + reason);
            DrawerClosed?.Invoke(this, EventArgs.Empty);
        }

        internal void CloseImmediately()
        {
            if (disposed) return;
            Log("close-immediate");
            StopMotion();
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanel.DismissReset();
            try { Close(); }
            catch (InvalidOperationException) { DisposeWithoutWindowClose(); }
        }

        private void StopMotion()
        {
            if (motionAttached)
                CompositionTarget.Rendering -= MotionFrame;
            motionAttached = false;
            motionPrimed = false;
        }

        private void SetState(DrawerState value)
        {
            if (state == value) return;
            var previous = state;
            state = value;
            Log("state from=" + previous + " to=" + value);
            OpenStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Drawer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || !IsVisible || disposed || e.Key != Key.Escape) return;
            e.Handled = true;
            if (!TrafficPanel.DismissReset()) SetOpen(false);
        }

        private void OwnerGeometryChanged(object sender, EventArgs e) => Reposition();
        private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => Reposition();
        private void OwnerStateChanged(object sender, EventArgs e)
        {
            if (connectionWindow.WindowState == WindowState.Minimized) CloseImmediately();
            else Reposition();
        }
        private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!connectionWindow.IsVisible) CloseImmediately();
        }
        private void OwnerClosed(object sender, EventArgs e) => CloseImmediately();

        private void Reposition()
        {
            if (disposed || placing || !IsVisible) return;
            if (!PlaceBesideConnection()) HideDrawer("reposition-failed");
        }

        private bool PlaceBesideConnection()
        {
            if (placing || disposed) return false;
            placing = true;
            try
            {
                var ownerHandle = new WindowInteropHelper(connectionWindow).Handle;
                if (ownerHandle == IntPtr.Zero || !GetWindowRect(ownerHandle, out var ownerRect)
                    || connectionAnchor.ActualWidth <= 0 || connectionAnchor.ActualHeight <= 0) return false;
                var monitor = MonitorFromWindow(ownerHandle, 2);
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(monitor, ref info)) return false;
                var work = info.Work;
                if (ownerRect.Left > work.Right) return false;
                Point anchorTop = connectionAnchor.PointToScreen(new Point(0, 0));
                Point anchorBottom = connectionAnchor.PointToScreen(
                    new Point(connectionAnchor.ActualWidth, connectionAnchor.ActualHeight));
                double scale = Math.Abs(anchorBottom.X - anchorTop.X) / connectionAnchor.ActualWidth;
                double designHeight = Math.Max(220, connectionAnchor.ActualHeight - 96);
                if (scale <= 0 || double.IsInfinity(scale) || double.IsNaN(scale)) return false;
                double fittedScale = Math.Min(scale, (work.Bottom - work.Top) / designHeight);

                // Never move or resize the connection window. If the owner is too close
                // to the monitor edge, scale the drawer only; the connected layout stays fixed.
                fittedScale = Math.Min(fittedScale, Math.Max(0, ownerRect.Left - work.Left) / DesignWidth);
                if (fittedScale < scale * 0.55) return false;
                int width = (int)Math.Floor(DesignWidth * fittedScale);
                int height = (int)Math.Floor(designHeight * fittedScale);
                int top = (int)Math.Round(anchorTop.Y + 12 * scale);
                top = Math.Max(work.Top, Math.Min(top, work.Bottom - height));
                DrawerSurface.Height = designHeight;
                var handle = new WindowInteropHelper(this).EnsureHandle();
                return SetWindowPos(handle, IntPtr.Zero, ownerRect.Left - width, top,
                    width, height, NoZOrder | NoActivate);
            }
            finally { placing = false; }
        }

        private void DrawerSourceInitialized(object sender, EventArgs e)
        {
            drawerSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            drawerSource?.AddHook(WindowMessage);
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == 0x02E0 || message == 0x007E || message == 0x001A)
                && !disposed && !placementQueued)
            {
                placementQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    placementQueued = false;
                    Reposition();
                }));
            }
            return IntPtr.Zero;
        }

        private void Drawer_Closed(object sender, EventArgs e)
        {
            if (disposed) return;
            DisposeWithoutWindowClose();
        }

        private void DisposeWithoutWindowClose()
        {
            if (disposed) return;
            disposed = true;
            SetState(DrawerState.Closed);
            StopMotion();
            TrafficPanel.SetUpdatesEnabled(false);
            connectionWindow.LocationChanged -= OwnerGeometryChanged;
            connectionWindow.SizeChanged -= OwnerSizeChanged;
            connectionWindow.StateChanged -= OwnerStateChanged;
            connectionWindow.IsVisibleChanged -= OwnerVisibilityChanged;
            connectionWindow.PreviewKeyDown -= Drawer_PreviewKeyDown;
            connectionWindow.Closed -= OwnerClosed;
            ownerSource?.RemoveHook(WindowMessage);
            drawerSource?.RemoveHook(WindowMessage);
            ownerSource = drawerSource = null;
            Log("disposed");
            DrawerClosed?.Invoke(this, EventArgs.Empty);
        }

        private void Log(string fields)
        {
            ConnectionDiagnostics.Write("stat-panel", "event=" + fields + " state=" + state
                + " visible=" + IsVisible + " prepared=" + prepared);
        }

        private const uint NoZOrder = 0x0004, NoActivate = 0x0010;
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
    }
}
