using System;
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
        private Storyboard motion;
        private HwndSource ownerSource, drawerSource;
        private bool openingOrOpen, placing, placementQueued, disposed;
        private double cacheScale = 1;

        internal ConnectionTrafficPanel Panel => TrafficPanel;
        internal bool IsDisposed => disposed;
        internal bool IsOpeningOrOpen => openingOrOpen;
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
        }

        internal void SetOpen(bool open)
        {
            if (disposed || (openingOrOpen == open && (motion != null || IsVisible == open))) return;
            if (open && (!connectionWindow.IsVisible || !connectionAnchor.IsVisible
                || connectionWindow.WindowState == WindowState.Minimized)) return;

            TrafficPanel.DismissReset();
            TrafficPanel.SetUpdatesEnabled(false);
            double from = DrawerTranslation.X;
            StopMotion();
            DrawerTranslation.X = from;
            SetOpenState(open);
            if (open && !IsVisible)
            {
                DrawerTranslation.X = DesignWidth;
                if (!PlaceBesideConnection(true)) { HideDrawer(); return; }
                TrafficPanel.RefreshTraffic();
                Show();
                if (!PlaceBesideConnection(false)) { HideDrawer(); return; }
                from = DesignWidth;
            }
            if (!IsVisible) { HideDrawer(); return; }

            double target = open ? 0 : DesignWidth;
            double fraction = Math.Min(1, Math.Abs(target - from) / DesignWidth);
            if (fraction < 0.001) { FinishMotion(open); return; }
            DrawerSurface.IsHitTestVisible = false;
            DrawerSurface.IsEnabled = false;
            DrawerSurface.CacheMode = new BitmapCache { RenderAtScale = cacheScale };
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(target,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds((open ? 680 : 520) * fraction)),
                new KeySpline(0.22, 1, 0.36, 1)));
            Storyboard.SetTarget(animation, DrawerTranslation);
            Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.XProperty));
            var next = new Storyboard();
            next.Children.Add(animation);
            next.Completed += (sender, args) =>
            {
                if (disposed || !ReferenceEquals(motion, next)) return;
                StopMotion();
                FinishMotion(open);
            };
            motion = next;
            next.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        }

        private void FinishMotion(bool open)
        {
            DrawerTranslation.X = open ? 0 : DesignWidth;
            DrawerSurface.CacheMode = null;
            DrawerSurface.IsHitTestVisible = open;
            DrawerSurface.IsEnabled = open;
            if (open) TrafficPanel.SetUpdatesEnabled(true);
            else HideDrawer();
        }

        private void HideDrawer()
        {
            StopMotion();
            SetOpenState(false);
            DrawerTranslation.X = DesignWidth;
            DrawerSurface.CacheMode = null;
            DrawerSurface.IsHitTestVisible = false;
            DrawerSurface.IsEnabled = false;
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanel.DismissReset();
            if (IsVisible) Hide();
            DrawerClosed?.Invoke(this, EventArgs.Empty);
        }

        internal void CloseImmediately()
        {
            if (disposed) return;
            StopMotion();
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanel.DismissReset();
            Close();
        }

        private void StopMotion()
        {
            var previous = motion;
            motion = null;
            previous?.Remove(this);
        }

        private void SetOpenState(bool value)
        {
            if (openingOrOpen == value) return;
            openingOrOpen = value;
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
            // On a drag towards the monitor edge, keep the drawer adjacent and
            // readable. Never clamp it on top of the connection page.
            if (!PlaceBesideConnection(false)) HideDrawer();
        }

        private bool PlaceBesideConnection(bool allowOwnerNudge)
        {
            if (placing || disposed) return false;
            placing = true;
            try
            {
                var ownerHandle = new WindowInteropHelper(connectionWindow).Handle;
                if (ownerHandle == IntPtr.Zero || !GetWindowRect(ownerHandle, out var ownerRect)
                    || connectionAnchor.ActualWidth <= 0 || connectionAnchor.ActualHeight <= 0) return false;
                var monitor = MonitorFromWindow(ownerHandle, 2); // nearest monitor, including negative origins
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
                int ownerWidth = ownerRect.Right - ownerRect.Left;
                if (allowOwnerNudge)
                {
                    // Normal centered windows never move. Only a monitor edge
                    // requires enough room for the drawer to remain wholly left.
                    fittedScale = Math.Min(fittedScale,
                        Math.Max(0, work.Right - work.Left - ownerWidth) / DesignWidth);
                    if (fittedScale < scale * 0.55) return false;
                    int requiredWidth = (int)Math.Ceiling(DesignWidth * fittedScale);
                    int requiredLeft = work.Left + requiredWidth;
                    if (ownerRect.Left < requiredLeft)
                    {
                        if (!SetWindowPos(ownerHandle, IntPtr.Zero, requiredLeft, ownerRect.Top,
                            0, 0, NoSize | NoZOrder | NoActivate)) return false;
                        if (!GetWindowRect(ownerHandle, out ownerRect)) return false;
                        anchorTop = connectionAnchor.PointToScreen(new Point(0, 0));
                    }
                }
                fittedScale = Math.Min(fittedScale, Math.Max(0, ownerRect.Left - work.Left) / DesignWidth);
                // Closing is preferable to an unreadably narrow pane or an
                // overlap when the user drags the main window against the edge.
                if (fittedScale < scale * 0.55) return false;
                int width = (int)Math.Floor(DesignWidth * fittedScale);
                int height = (int)Math.Floor(designHeight * fittedScale);
                int top = (int)Math.Round(anchorTop.Y + 12 * scale);
                top = Math.Max(work.Top, Math.Min(top, work.Bottom - height));
                DrawerSurface.Height = designHeight;
                cacheScale = Math.Max(1, fittedScale);
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
            // Recompute after WPF has applied DPI/display/work-area changes.
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
            disposed = true;
            SetOpenState(false);
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
            DrawerClosed?.Invoke(this, EventArgs.Empty);
        }

        private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
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
