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
            DetachMotionWindow();
            _motionWindow = Window.GetWindow(this);
            if (_motionWindow != null) { _motionWindow.StateChanged += MotionWindow_StateChanged; _motionWindow.Closed += MotionWindow_Closed; }
            UpdateVisibleStarGlows();
        }

        private void Picker_Unloaded(object sender, RoutedEventArgs e)
        {
            CompleteRowTransitions();
            _sweepAnimator.Suspend();
            foreach (var element in _starGlows) StopStarGlow(element);
            DetachMotionWindow();
        }

        private void DetachMotionWindow()
        {
            if (_motionWindow != null) { _motionWindow.StateChanged -= MotionWindow_StateChanged; _motionWindow.Closed -= MotionWindow_Closed; }
            _motionWindow = null;
        }

        private void MotionWindow_Closed(object sender, EventArgs e) => CompleteRowTransitions();

        private void MotionWindow_StateChanged(object sender, EventArgs e)
        {
            if (_motionWindow?.WindowState == WindowState.Minimized) CompleteRowTransitions();
            UpdateVisibleStarGlows();
        }
        private void Picker_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) CompleteRowTransitions();
            UpdateVisibleStarGlows();
        }

        private void UpdateVisibleStarGlows()
        {
            foreach (var element in _starGlows) UpdateStarGlow(element);
        }
    }
}
