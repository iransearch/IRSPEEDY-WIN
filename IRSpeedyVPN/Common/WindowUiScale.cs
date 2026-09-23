using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IRSpeedyVPN.Common
{
    // Window dimensions in XAML remain design DIPs. Apply the shared presentation
    // scale once, after XAML initialization and before the window is displayed.
    public static class WindowUiScale
    {
        public static readonly DependencyProperty FactorProperty = DependencyProperty.RegisterAttached(
            "Factor", typeof(double), typeof(WindowUiScale), new PropertyMetadata(1d, FactorChanged),
            value => (double)value > 0 && !double.IsInfinity((double)value) && !double.IsNaN((double)value));

        public static double GetFactor(DependencyObject obj) => (double)obj.GetValue(FactorProperty);
        public static void SetFactor(DependencyObject obj, double value) => obj.SetValue(FactorProperty, value);

        private static void FactorChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (!(obj is Window window)) return;
            window.Initialized -= Initialize;
            window.Initialized += Initialize;
        }

        private static void Initialize(object sender, EventArgs args)
        {
            var window = (Window)sender;
            window.Initialized -= Initialize;
            double scale = GetFactor(window);
            if (!double.IsNaN(window.Width)) window.Width *= scale;
            if (!double.IsNaN(window.Height)) window.Height *= scale;

            // Design windows already have a fixed-size canvas inside a Viewbox.
            // Resizing that viewport scales everything; a second transform would
            // apply the factor twice. Legacy dialogs need a layout transform.
            if (window.Content is FrameworkElement content && !(content is Viewbox))
            {
                var transforms = new TransformGroup();
                if (content.LayoutTransform != null && !content.LayoutTransform.Value.IsIdentity)
                    transforms.Children.Add(content.LayoutTransform.Clone());
                transforms.Children.Add(new ScaleTransform(scale, scale));
                content.LayoutTransform = transforms;
            }
        }
    }
}
