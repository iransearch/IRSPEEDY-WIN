using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Common
{
    internal static class HeaderRefreshMotion
    {
        internal static void Attach(FrameworkElement icon)
        {
            // Own the mutable transform; resource geometries may be frozen.
            var rotation = new RotateTransform();
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = rotation;
            Window window = null;
            bool running = false;
            Action update = () =>
            {
                bool active = icon.IsLoaded && icon.IsVisible && window != null && window.WindowState != WindowState.Minimized;
                if (active == running) return;
                running = active;
                rotation.BeginAnimation(RotateTransform.AngleProperty, active
                    ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever }
                    : null);
            };
            EventHandler stateChanged = (s, e) => update();
            icon.Loaded += (s, e) => { window = Window.GetWindow(icon); if (window != null) window.StateChanged += stateChanged; update(); };
            icon.IsVisibleChanged += (s, e) => update();
            icon.Unloaded += (s, e) =>
            {
                if (window != null) window.StateChanged -= stateChanged;
                window = null; update();
            };
        }
    }
}
