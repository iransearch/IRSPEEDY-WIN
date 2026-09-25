using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
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
        private Task stageQueue = Task.CompletedTask;
        private readonly Stopwatch stageVisibleTime = new Stopwatch();
        private int presentationVersion;
        private int requestedStage;
        // Give every real stage a full, readable checking cycle, even from cache.
        private const int MinimumStageMs = 1600;
        private const int TickRevealMs = 600;
        private const int CompletedHoldMs = 850;

        public void SetStage(int stage)
        {
            if (stage == 0)
            {
                presentationVersion++;
                requestedStage = 0;
                ApplyStage(0);
                stageQueue = StartStageClockAsync(presentationVersion);
                return;
            }
            if (stage <= requestedStage || stage >= Steps.Length) return;
            // Serialize real progress reports so a fast/cache login still paints each step.
            for (int next = requestedStage + 1; next <= stage; next++)
                stageQueue = AdvanceStageAsync(stageQueue, next, presentationVersion);
            requestedStage = stage;
        }

        private void ApplyStage(int stage)
        {
            for (int i = 0; i < Steps.Length; i++)
                Steps[i].SetState(i < stage ? 2 : i == stage ? 1 : 0);
        }

        private async Task StartStageClockAsync(int version)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (version == presentationVersion) stageVisibleTime.Restart();
        }

        private async Task WaitForStageAsync()
        {
            int remaining = Math.Max(0, MinimumStageMs - (int)stageVisibleTime.ElapsedMilliseconds);
            if (remaining > 0) await Task.Delay(remaining);
        }

        private async Task AdvanceStageAsync(Task previous, int stage, int version)
        {
            await previous;
            if (version != presentationVersion) return;
            await WaitForStageAsync();
            if (version != presentationVersion) return;
            // Finish just one tick, then start the next spinner after its reveal.
            Steps[stage - 1].SetState(2);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(TickRevealMs);
            if (version != presentationVersion) return;
            ApplyStage(stage);
            await StartStageClockAsync(version);
        }

        public async Task FinishStagesAsync(bool succeeded)
        {
            int version = presentationVersion;
            await stageQueue;
            if (version != presentationVersion) return;
            await WaitForStageAsync();
            if (version != presentationVersion) return;
            // Only a confirmed successful login may complete the server stage.
            if (succeeded && requestedStage == Steps.Length - 1)
            {
                Steps[Steps.Length - 1].SetState(2);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(CompletedHoldMs);
            }
        }

        public void HoldCompletedFrame()
        {
            motion?.Pause(this);
        }

        public async Task FadeOutAsync()
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            await Task.Delay(450);
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                StepsList.ItemsSource = Steps;
                motion = ((Storyboard)Resources["LoaderMotion"]).Clone();
                motion.Begin(this, true);
            }
            else StopMotion();
        }
        private void StopMotion() { presentationVersion++; motion?.Remove(this); motion = null; if (StepsList != null) StepsList.ItemsSource = null; }
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
        public void SetState(int value) { if (state == value) return; state = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
}
