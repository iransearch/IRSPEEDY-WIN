using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRSpeedyVPN.UserControls
{
    public partial class UCLoginLoading : UserControl
    {
        public LoginProgressStep[] Steps { get; } = {
            new LoginProgressStep("تأیید حساب", true),
            new LoginProgressStep("بررسی اشتراک", false),
            new LoginProgressStep("دریافت سرورها", false) };
        private Storyboard motion;
        public UCLoginLoading() { InitializeComponent(); DataContext = this; }
        public void SetStage(int stage)
        {
            for (int i = 0; i < Steps.Length; i++) Steps[i].SetState(i < stage ? 2 : i == stage ? 1 : 0);
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                StepsList.ItemsSource = Steps;
                motion = ((Storyboard)Resources["LoaderMotion"]).Clone();
                motion.Begin(this, true);
            }
            else StopMotion();
        }
        private void StopMotion() { motion?.Remove(this); motion = null; if (StepsList != null) StepsList.ItemsSource = null; }
        private void Control_Unloaded(object sender, RoutedEventArgs e) => StopMotion();
    }
    public sealed class LoginProgressStep : INotifyPropertyChanged
    {
        private int state;
        public LoginProgressStep(string label, bool first) { Label = label; HasLine = !first; }
        public string Label { get; }
        public bool HasLine { get; }
        public bool IsDone => state == 2;
        public bool IsActive => state == 1;
        public bool IsPending => state == 0;
        public bool LineDone => HasLine && IsDone;
        public bool LineActive => HasLine && IsActive;
        public FontWeight LabelWeight => IsActive ? FontWeights.Bold : FontWeights.Medium;
        public Brush LabelBrush => (Brush)new BrushConverter().ConvertFromString(IsDone ? "#159A63" : IsActive ? "#141B33" : "#9CA3B4");
        public event PropertyChangedEventHandler PropertyChanged;
        public void SetState(int value) { state = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
}
