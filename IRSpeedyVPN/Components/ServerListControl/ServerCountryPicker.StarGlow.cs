using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public partial class ServerCountryPicker
    {
        private readonly HashSet<FrameworkElement> _starGlows = new HashSet<FrameworkElement>();
        private static readonly DoubleAnimationUsingKeyFrames StarOpacityPulse = CreateStarPulse(.65, 1);
        private static readonly DoubleAnimationUsingKeyFrames StarRadiusPulse = CreateStarPulse(6, 11);

        private void StarGlow_Loaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            _starGlows.Add(element);
            UpdateStarGlow(element);
        }

        private void StarGlow_Unloaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            _starGlows.Remove(element);
            StopStarGlow(element);
        }

        private void StarGlow_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
            => UpdateStarGlow((FrameworkElement)sender);

        private static DropShadowEffect MutableStarGlow(FrameworkElement element)
        {
            var glow = element.Effect as DropShadowEffect;
            if (glow == null) return null;
            // A cached row template can share a frozen effect with BOTH sweep
            // presenters. Each visible star must own its mutable animation target.
            if (glow.IsFrozen)
            {
                glow = glow.Clone();
                element.Effect = glow;
            }
            return glow;
        }

        private static void StopStarGlow(FrameworkElement element)
        {
            var glow = element.Effect as DropShadowEffect;
            // IsVisibleChanged can arrive before XAML assigns the local Effect.
            // A frozen effect cannot have animation clocks and needs no mutation.
            if (glow == null || glow.IsFrozen) return;
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        }

        private void UpdateStarGlow(FrameworkElement element)
        {
            StopStarGlow(element);
            if (!element.IsLoaded || !element.IsVisible || !IsVisible || !SystemParameters.ClientAreaAnimation
                || LightSweepRowTransition.IsTransitionSurface(element)
                || _motionWindow == null || _motionWindow.WindowState == WindowState.Minimized) return;

            // Loaded guarantees that the local XAML Effect has been assigned.
            // Animate the actual effect directly, never a nullable property path
            // from a Style trigger executing during template construction.
            var glow = MutableStarGlow(element);
            if (glow == null) return;
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, StarOpacityPulse);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, StarRadiusPulse);
        }

        private static DoubleAnimationUsingKeyFrames CreateStarPulse(double idle, double peak)
        {
            var pulse = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(4),
                RepeatBehavior = RepeatBehavior.Forever
            };
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(idle, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.3)), new KeySpline(.2, 0, .2, 1)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(idle, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.8)), new KeySpline(.2, 0, .2, 1)));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(idle, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4))));
            pulse.Freeze();
            return pulse;
        }
    }
}
