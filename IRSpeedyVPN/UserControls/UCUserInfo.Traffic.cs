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
        // Approved HTML: perspective:1300px; translateX(-100% - 28px) rotateY(-68deg).
        private const double TrafficPerspective = 1300;
        private const double TrafficShadowPadding = 48;
        private Storyboard trafficMotion;
        private bool trafficPanelOpen;
        private double TrafficClosedOffset => -(TrafficPanelContainer.ActualWidth + 28);

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

            if (trafficMotion == null)
            {
                if (open)
                {
                    PauseConnectedMotion();
                    // Identity handoff, then transform/blur only the bitmap. The original
                    // illustration keeps its layout and paused clock phase throughout.
                    TrafficBlurredBackground.Source = CaptureTrafficVisual(ConnectedContent);
                    TrafficBlurredBackground.Visibility = Visibility.Visible;
                    TrafficBlurredBackground.Opacity = 1;
                    ConnectedContent.Opacity = 0;
                    TrafficPanel.RefreshTraffic();
                }
                TrafficPanelContainer.Visibility = Visibility.Visible;
                TrafficPanelContainer.UpdateLayout();
                PrepareTrafficFold();
                if (open) TrafficPanelTranslate.OffsetX = TrafficClosedOffset;
            }

            // These are the independent transitions from the approved preview, including
            // CSS 'ease' for alpha. CubicEase is NOT cubic-bezier(.22,1,.36,1).
            var next = new Storyboard();
            AddTrafficAnimation(next, TrafficPanelTranslate, TranslateTransform3D.OffsetXProperty, TrafficClosedOffset, 0, open, 620);
            AddTrafficAnimation(next, TrafficHinge, AxisAngleRotation3D.AngleProperty, -68, 0, open, 620);
            AddTrafficAnimation(next, TrafficFoldViewport, UIElement.OpacityProperty, 0, 1, open, 480, true);
            AddTrafficAnimation(next, TrafficCreaseBrush, Brush.OpacityProperty, 0.55, 0, open, 600, true);
            AddTrafficAnimation(next, TrafficCreaseScale, ScaleTransform3D.ScaleXProperty, 1.7, 0.2, open, 620);
            AddTrafficAnimation(next, TrafficBackgroundScale, ScaleTransform.ScaleXProperty, 1, 0.975, open, 620);
            AddTrafficAnimation(next, TrafficBackgroundScale, ScaleTransform.ScaleYProperty, 1, 0.975, open, 620);
            AddTrafficAnimation(next, TrafficBackgroundTranslate, TranslateTransform.XProperty, 0, 10, open, 620);
            AddTrafficAnimation(next, TrafficBlurredBackground, UIElement.OpacityProperty, 1, 0.6, open, 520, true);
            AddTrafficAnimation(next, TrafficBackgroundBlur, System.Windows.Media.Effects.BlurEffect.RadiusProperty, 0, 6, open, 620);
            AddTrafficAnimation(next, TrafficBackdrop, UIElement.OpacityProperty, 0, 1, open, 620, true);
            AddTrafficAnimation(next, TrafficArrowRotation, RotateTransform.AngleProperty, 0, 180, open, 620);

            // Capture all current animated values before replacing clocks. Reversal never
            // re-captures the panel/background or briefly paints their endpoint states.
            StopTrafficMotion();
            trafficPanelOpen = open;
            UpdateTrafficToggle(open);
            TrafficPanel.SetUpdatesEnabled(false);
            TrafficPanelLayer.IsHitTestVisible = false;
            TrafficPanelContainer.Visibility = Visibility.Hidden;
            TrafficFoldViewport.Visibility = Visibility.Visible;
            TrafficBackdrop.Visibility = Visibility.Visible;
            ConnectedContent.IsHitTestVisible = false;
            ConnectedContent.IsEnabled = false;
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

        private static BitmapSource CaptureTrafficVisual(FrameworkElement visual, double padding = 0)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            var source = new Rect(-padding, -padding,
                Math.Max(1, visual.ActualWidth) + 2 * padding, Math.Max(1, visual.ActualHeight) + 2 * padding);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(source.Width * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(source.Height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(visual)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = source, Stretch = Stretch.Fill },
                    null, new Rect(0, 0, source.Width, source.Height));
            bitmap.Render(drawing);
            bitmap.Freeze();
            return bitmap;
        }

        private void PrepareTrafficFold()
        {
            double sceneWidth = Math.Max(1, TrafficPanelLayer.ActualWidth);
            double sceneHeight = Math.Max(1, TrafficPanelLayer.ActualHeight);
            var origin = TrafficPanelContainer.TranslatePoint(new Point(0, 0), TrafficPanelLayer);
            double width = Math.Max(1, TrafficPanelContainer.ActualWidth);
            double height = Math.Max(1, TrafficPanelContainer.ActualHeight);
            double left = origin.X, top = sceneHeight - origin.Y, bottom = top - height;
            var texture = new ImageBrush(CaptureTrafficVisual(TrafficPanelContainer, TrafficShadowPadding)) { Stretch = Stretch.Fill };
            texture.Freeze();
            // Emissive material reproduces the captured UI colors without scene lighting.
            var material = new EmissiveMaterial(texture);
            material.Freeze();
            TrafficFoldModel.Geometry = TrafficQuad(left - TrafficShadowPadding, bottom - TrafficShadowPadding,
                left + width + TrafficShadowPadding, top + TrafficShadowPadding);
            TrafficFoldModel.Material = TrafficFoldModel.BackMaterial = material;
            double creaseLeft = left + width * 0.48;
            TrafficCreaseModel.Geometry = TrafficQuad(creaseLeft, bottom, creaseLeft + 34, top, 0.01);
            TrafficCreaseScale.CenterX = creaseLeft + 17;
            TrafficCreaseModel.BackMaterial = TrafficCreaseModel.Material;

            // WPF's FieldOfView is horizontal. Using the whole scene and a fixed 1300px
            // distance matches CSS parent perspective-origin:center and avoids crop/zoom.
            TrafficFoldCamera.Position = new Point3D(sceneWidth / 2, sceneHeight / 2, TrafficPerspective);
            TrafficFoldCamera.FieldOfView = 2 * Math.Atan(sceneWidth / (2 * TrafficPerspective)) * 180 / Math.PI;
            TrafficHingeTransform.CenterX = left;
            TrafficHingeTransform.CenterY = bottom + height / 2;
        }

        private static MeshGeometry3D TrafficQuad(double left, double bottom, double right, double top, double z = 0)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(left, bottom, z), new Point3D(right, bottom, z), new Point3D(right, top, z), new Point3D(left, top, z) },
                TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };
            mesh.Freeze();
            return mesh;
        }

        private void AddTrafficAnimation(Storyboard storyboard, DependencyObject target,
            DependencyProperty property, double closed, double opened, bool open, int milliseconds, bool cssEase = false)
        {
            double from = (double)target.GetValue(property);
            double value = open ? opened : closed;
            // CSS shortens a reversing transition by its remaining value fraction.
            // A quick second click must not leave a nearly closed overlay running 620ms.
            double fraction = trafficMotion == null ? 1 : Math.Min(1, Math.Abs((value - from) / (opened - closed)));
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * fraction)),
                cssEase ? new KeySpline(0.25, 0.1, 0.25, 1) : new KeySpline(0.22, 1, 0.36, 1)));
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
            TrafficPanelTranslate.OffsetX = open ? 0 : TrafficClosedOffset;
            TrafficHinge.Angle = open ? 0 : -68;
            TrafficArrowRotation.Angle = open ? 180 : 0;
            TrafficFoldViewport.Opacity = open ? 1 : 0;
            TrafficCreaseBrush.Opacity = open ? 0 : 0.55;
            TrafficCreaseScale.ScaleX = open ? 0.2 : 1.7;
            TrafficBackgroundScale.ScaleX = TrafficBackgroundScale.ScaleY = open ? 0.975 : 1;
            TrafficBackgroundTranslate.X = open ? 10 : 0;
            TrafficBackgroundBlur.Radius = open ? 6 : 0;
            TrafficBlurredBackground.Opacity = open ? 0.6 : 1;
            TrafficBackdrop.Opacity = open ? 1 : 0;
            TrafficBlurredBackground.Visibility = TrafficBackdrop.Visibility = open ? Visibility.Visible : Visibility.Hidden;
            TrafficFoldViewport.Visibility = Visibility.Hidden;
            TrafficPanelContainer.Visibility = open ? Visibility.Visible : Visibility.Hidden;
            TrafficPanelLayer.IsHitTestVisible = open;
            ConnectedContent.IsHitTestVisible = !open;
            ConnectedContent.IsEnabled = !open;
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
