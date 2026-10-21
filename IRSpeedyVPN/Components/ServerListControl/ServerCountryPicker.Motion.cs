using System;
using System.Windows;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public partial class ServerCountryPicker
    {
        private Window _motionWindow;

        private void Picker_Loaded(object sender, RoutedEventArgs e)
        {
            CompleteRowTransitions();
            _sweepAnimator.Resume();
            SystemParameters.StaticPropertyChanged += MotionPreferenceChanged;
            DetachMotionWindow();
            _motionWindow = Window.GetWindow(this);
            if (_motionWindow != null) { _motionWindow.StateChanged += MotionWindow_StateChanged; _motionWindow.Closed += MotionWindow_Closed; }
            UpdateVisibleStarGlows();
            UpdateVisibleProbeShines();
        }

        private void Picker_Unloaded(object sender, RoutedEventArgs e)
        {
            CompleteRowTransitions();
            _sweepAnimator.Suspend();
            SystemParameters.StaticPropertyChanged -= MotionPreferenceChanged;
            StopProbeShines();
            foreach (var element in _starGlows) StopStarGlow(element);
            DetachMotionWindow();
        }

        private void DetachMotionWindow()
        {
            if (_motionWindow != null) { _motionWindow.StateChanged -= MotionWindow_StateChanged; _motionWindow.Closed -= MotionWindow_Closed; }
            _motionWindow = null;
        }

        private void MotionWindow_Closed(object sender, EventArgs e)
        {
            CompleteRowTransitions(); StopProbeShines();
        }
        private void MotionPreferenceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
            if (!Dispatcher.CheckAccess())
            { Dispatcher.BeginInvoke(new Action(() => MotionPreferenceChanged(sender, e))); return; }
            if (!SystemParameters.ClientAreaAnimation) CompleteRowTransitions();
            UpdateVisibleProbeShines(); UpdateVisibleStarGlows();
        }

        private void MotionWindow_StateChanged(object sender, EventArgs e)
        {
            if (_motionWindow?.WindowState == WindowState.Minimized) CompleteRowTransitions();
            UpdateVisibleStarGlows();
            UpdateVisibleProbeShines();
        }
        private void Picker_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) CompleteRowTransitions();
            UpdateVisibleStarGlows();
            UpdateVisibleProbeShines();
        }

        private void UpdateVisibleStarGlows()
        {
            foreach (var element in _starGlows) UpdateStarGlow(element);
        }
    }
}
