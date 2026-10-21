using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace IRSpeedyVPN.Components.ServerListControl
{
    // Mark EXISTING groups; this adds no visual/layout node.
    public static class ServerCometPart
    {
        public static readonly DependencyProperty PartProperty = DependencyProperty.RegisterAttached("Part", typeof(ServerRowPart), typeof(ServerCometPart), new PropertyMetadata(ServerRowPart.None));
        public static void SetPart(DependencyObject element, ServerRowPart value) => element.SetValue(PartProperty, value);
        public static ServerRowPart GetPart(DependencyObject element) => (ServerRowPart)element.GetValue(PartProperty);
    }
    public sealed class ServerListCometOverlay : FrameworkElement
    {
        private static readonly Brush BandBrush = CreateBandBrush();
        internal readonly TranslateTransform BandShift = new TranslateTransform();
        internal Transform BandSkew;
        internal Rect BandRect;
        internal Geometry BandClip;
        internal bool ShowBand;
        internal readonly List<ServerCometFlash> Flashes = new List<ServerCometFlash>();
        public ServerListCometOverlay() { IsHitTestVisible = false; Focusable = false; }
        protected override Size MeasureOverride(Size availableSize) => new Size();
        protected override void OnRender(DrawingContext dc)
        {
            if (ShowBand && BandClip != null)
            {
                dc.PushClip(BandClip); dc.PushTransform(BandShift); dc.PushTransform(BandSkew);
                dc.DrawRectangle(BandBrush, null, BandRect); dc.Pop(); dc.Pop(); dc.Pop();
            }
            // Content fades on its own element. Painting its layout bounds here
            // produces a solid horizontal bar behind stretched country labels.
        }
        private static Brush CreateBandBrush()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(10, 110, 210, 255), .30));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(46, 110, 210, 255), .68));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(102, 190, 240, 255), .86));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(140, 255, 255, 255), .90));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(38, 190, 240, 255), .95));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, .99));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            brush.Freeze(); return brush;
        }
    }
    internal sealed class ServerCometFlash
    {
        internal FrameworkElement Element;
        internal Button OwnerRow;
        internal ServerDisplaySlot Slot;
        internal ServerRowPart Part;
        internal ServerItem Snapshot;
        internal double Started;
    }
}
