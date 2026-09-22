using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace IRSpeedyVPN.Common
{
    internal static class WindowDrag
    {
        public static void Begin(Window window, MouseButtonEventArgs e)
        {
            if (e.Handled || e.ChangedButton != MouseButton.Left ||
                Mouse.LeftButton != MouseButtonState.Pressed || !window.IsVisible) return;

            // A Path/TextBlock may be the original source inside a header button.
            // Walk its ancestors instead of checking only the original source type.
            for (var node = e.OriginalSource as DependencyObject;
                 node != null && node != window; node = Parent(node))
            {
                if (node is ButtonBase || node is MenuItem || node is TextBoxBase ||
                    node is PasswordBox || node is Label ||
                    node is FrameworkElement element && element.Cursor == Cursors.Hand) return;
            }

            // DragMove runs a nested native loop and returns after mouse release.
            // Consume the routed event BEFORE entering it so a parent cannot drag again.
            e.Handled = true;
            try { window.DragMove(); }
            catch (InvalidOperationException) when (Mouse.LeftButton != MouseButtonState.Pressed || !window.IsVisible)
            {
                // Mouse release/window hiding can race the check immediately above.
            }
        }

        private static DependencyObject Parent(DependencyObject node)
        {
            if (node is Visual || node is Visual3D) return VisualTreeHelper.GetParent(node);
            if (node is FrameworkContentElement content) return content.Parent;
            return LogicalTreeHelper.GetParent(node);
        }
    }
}
