using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public partial class ServerCountryPicker
    {
        private readonly HashSet<FrameworkElement> _probeShines = new HashSet<FrameworkElement>();
        private void ProbeShine_Changed(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            _probeShines.Add(element);
            UpdateProbeShine(element);
        }
        private void ProbeShine_Unloaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            _probeShines.Remove(element);
            ProbeShift(element)?.BeginAnimation(TranslateTransform.XProperty, null);
        }
        private void ProbeShine_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
            => UpdateProbeShine((FrameworkElement)sender);
        private static TranslateTransform ProbeShift(FrameworkElement element)
        {
            var group = element.RenderTransform as TransformGroup;
            if (group == null || group.Children.Count != 2) return null;
            var shift = group.Children[1] as TranslateTransform;
            if (shift == null) return null;
            // DataTemplate transforms may be frozen/shared. Even removing an
            // animation with BeginAnimation(..., null) requires a mutable target.
            // Clone the whole graph so each row owns its animated child.
            if (group.IsFrozen || shift.IsFrozen)
            {
                group = group.Clone();
                element.RenderTransform = group;
                shift = (TranslateTransform)group.Children[1];
            }
            return shift;
        }
        private void UpdateProbeShine(FrameworkElement element)
        {
            var shift = ProbeShift(element);
            if (shift == null) return;
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            if (!element.IsLoaded || !element.IsVisible || !IsVisible || !SystemParameters.ClientAreaAnimation ||
                _motionWindow == null || _motionWindow.WindowState == WindowState.Minimized) return;
            var sheen = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.2), RepeatBehavior = RepeatBehavior.Forever };
            sheen.KeyFrames.Add(new DiscreteDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sheen.KeyFrames.Add(new EasingDoubleKeyFrame(50, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.76)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            sheen.KeyFrames.Add(new DiscreteDoubleKeyFrame(50, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
            shift.BeginAnimation(TranslateTransform.XProperty, sheen);
        }

        private void UpdateVisibleProbeShines()
        {
            foreach (var element in _probeShines) UpdateProbeShine(element);
        }
        private void StopProbeShines()
        {
            foreach (var element in _probeShines) ProbeShift(element)?.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }
}
