using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        private Storyboard trafficMotion;
        private bool trafficPanelOpen;
        private double TrafficClosedOffset => -Math.Max(420, ActualWidth + 30);

        private void TrafficToggleButton_Click(object sender, RoutedEventArgs e) =>
            SetTrafficPanelOpen(TrafficToggleButton.IsChecked == true);

        private void TrafficBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            SetTrafficPanelOpen(false);
            TrafficToggleButton.Focus();
        }

        private void Connected_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || (!trafficPanelOpen && trafficMotion == null)) return;
            e.Handled = true;
            if (TrafficPanel.DismissReset()) return;
            SetTrafficPanelOpen(false);
            TrafficToggleButton.Focus();
        }

        private void SetTrafficPanelOpen(bool open)
        {
            if (!IsLoaded || !IsVisible) { ResetTrafficPanel(); return; }
            if (trafficPanelOpen == open && trafficMotion == null) return;
            TrafficPanel.DismissReset();

            // Only capture at an endpoint. Reversing mid-motion reuses the texture and
            // current clocks, so rapid clicks never flash the live panel or restart it.
            if (trafficMotion == null)
            {
                if (open)
                {
                    PauseConnectedMotion();
                    TrafficBlurredBackground.Source = CaptureTrafficVisual(ConnectedContent);
                    TrafficBlurredBackground.Visibility = Visibility.Visible;
                    TrafficPanel.RefreshTraffic();
                }
                TrafficPanelContainer.Visibility = Visibility.Visible;
                TrafficPanelContainer.UpdateLayout();
                PrepareTrafficFold();
            }

            var duration = TimeSpan.FromMilliseconds(open ? 520 : 400);
            var next = new Storyboard();
            AddTrafficAnimation(next, TrafficPanelTranslate, TranslateTransform.XProperty, open ? 0 : TrafficClosedOffset, duration);
            AddTrafficAnimation(next, TrafficHinge, AxisAngleRotation3D.AngleProperty, open ? 0 : 64, duration);
            AddTrafficAnimation(next, TrafficBlurredBackground, UIElement.OpacityProperty, open ? 1 : 0, duration);
            AddTrafficAnimation(next, ConnectedContent, UIElement.OpacityProperty, open ? 0 : 1, duration);
            AddTrafficAnimation(next, TrafficBackdrop, UIElement.OpacityProperty, open ? 1 : 0, duration);
            AddTrafficAnimation(next, TrafficArrowRotation, RotateTransform.AngleProperty, open ? 180 : 0, duration);
            StopTrafficMotion();
            trafficPanelOpen = open;
            UpdateTrafficToggle(open);
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanelLayer.IsHitTestVisible = false;
            TrafficPanelContainer.Visibility = Visibility.Hidden;
            TrafficFoldViewport.Visibility = Visibility.Visible;
            TrafficBackdrop.Visibility = Visibility.Visible;
            ConnectedContent.IsHitTestVisible = false;

            // This small disclosure remains animated even when Windows disables general
            // client-area animations; the prior instant branch obscured its direction.
            trafficMotion = next;
            next.Completed += (sender, args) =>
            {
                if (!ReferenceEquals(trafficMotion, next)) return;
                StopTrafficMotion();
                ApplyTrafficState(open);
                if (!open) ResumeConnectedMotion();
            };
            next.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        }

        private static BitmapSource CaptureTrafficVisual(FrameworkElement visual)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            var bounds = new Rect(0, 0, Math.Max(1, visual.ActualWidth), Math.Max(1, visual.ActualHeight));
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Draw in local coordinates: Render(visual) directly can include its layout
            // offset/margin and crop or shift the snapshot at the live/texture handoff.
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(visual)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds);
            bitmap.Render(drawing);
            bitmap.Freeze();
            return bitmap;
        }

        private void PrepareTrafficFold()
        {
            double width = Math.Max(1, TrafficPanelContainer.ActualWidth);
            double height = Math.Max(1, TrafficPanelContainer.ActualHeight);
            var texture = new ImageBrush(CaptureTrafficVisual(TrafficPanelContainer)) { Stretch = Stretch.Fill };
            texture.Freeze();
            var material = new DiffuseMaterial(texture);
            material.Freeze();
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(0, 0, 0), new Point3D(width, 0, 0), new Point3D(width, height, 0), new Point3D(0, height, 0) },
                TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };
            mesh.Freeze();
            TrafficFoldModel.Geometry = mesh;
            TrafficFoldModel.Material = TrafficFoldModel.BackMaterial = material;
            TrafficFoldCamera.Position = new Point3D(width / 2, height / 2, width / (2 * Math.Tan(Math.PI / 12)));
            TrafficHingeTransform.CenterY = height / 2;
        }

        private static void AddTrafficAnimation(Storyboard storyboard, DependencyObject target,
            DependencyProperty property, double value, TimeSpan duration)
        {
            var animation = new DoubleAnimation((double)target.GetValue(property), value, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }

        private void StopTrafficMotion()
        {
            var current = trafficMotion;
            trafficMotion = null;
            current?.Remove(this);
        }

        private void UpdateTrafficToggle(bool open)
        {
            TrafficToggleButton.IsChecked = open;
            TrafficToggleButton.ToolTip = open ? "بستن آمار مصرف" : "آمار مصرف برنامه‌ها";
            AutomationProperties.SetName(TrafficToggleButton, TrafficToggleButton.ToolTip.ToString());
            btnDisConnect.IsCancel = !open;
        }

        private void ApplyTrafficState(bool open)
        {
            TrafficPanelTranslate.X = open ? 0 : TrafficClosedOffset;
            TrafficHinge.Angle = open ? 0 : 64;
            TrafficArrowRotation.Angle = open ? 180 : 0;
            TrafficBlurredBackground.Opacity = TrafficBackdrop.Opacity = open ? 1 : 0;
            TrafficBlurredBackground.Visibility = TrafficBackdrop.Visibility = open ? Visibility.Visible : Visibility.Hidden;
            TrafficFoldViewport.Visibility = Visibility.Hidden;
            TrafficPanelContainer.Visibility = open ? Visibility.Visible : Visibility.Hidden;
            TrafficPanelLayer.IsHitTestVisible = open;
            ConnectedContent.IsHitTestVisible = !open;
            ConnectedContent.Opacity = open ? 0 : 1;
            TrafficPanel.SetUpdatesEnabled(open);
            TrafficFoldModel.Material = TrafficFoldModel.BackMaterial = null;
            if (!open) TrafficBlurredBackground.Source = null;
        }

        private void ResetTrafficPanel()
        {
            if (TrafficPanelContainer == null || TrafficToggleButton == null || btnDisConnect == null) return;
            StopTrafficMotion();
            trafficPanelOpen = false;
            UpdateTrafficToggle(false);
            TrafficPanel.DismissReset();
            ApplyTrafficState(false);
            ResumeConnectedMotion();
        }
    }
}
