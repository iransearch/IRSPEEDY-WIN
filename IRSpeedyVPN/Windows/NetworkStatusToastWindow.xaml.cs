using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IRSpeedyVPN.Windows
{
    public partial class NetworkStatusToastWindow : Window
    {
        private readonly Window anchorWindow;
        private readonly DispatcherTimer autoCloseTimer;
        private bool closing;

        internal NetworkStatusToastWindow(Window anchorWindow)
        {
            this.anchorWindow = anchorWindow;
            InitializeComponent();

            autoCloseTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(9)
            };
            autoCloseTimer.Tick += AutoCloseTimer_Tick;
            Loaded += NetworkStatusToastWindow_Loaded;
            Closed += NetworkStatusToastWindow_Closed;
        }

        private void NetworkStatusToastWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionToast();
            BeginShowAnimation();
            autoCloseTimer.Start();
        }

        private void PositionToast()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(anchorWindow);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(anchorWindow);
                var scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                var scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                Left = (screen.WorkingArea.Right / scaleX) - Width - 18;
                Top = (screen.WorkingArea.Bottom / scaleY) - Height - 18;
                return;
            }
            catch
            {
                // Fall back to WPF's primary working area.
            }

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 18;
            Top = workArea.Bottom - Height - 18;
        }

        private void BeginShowAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            ToastCard.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = ease
                });

            ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(42, 0, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = ease
                });
        }

        internal void CloseAnimated()
        {
            if (closing)
                return;

            if (!IsLoaded)
            {
                Close();
                return;
            }

            closing = true;
            autoCloseTimer.Stop();

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var opacity = new DoubleAnimation(ToastCard.Opacity, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = ease
            };
            opacity.Completed += (s, e) => Close();
            ToastCard.BeginAnimation(OpacityProperty, opacity);

            ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(0, 28, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = ease
                });
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            CloseAnimated();
        }

        private void Close_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CloseAnimated();
        }

        private void NetworkStatusToastWindow_Closed(object sender, EventArgs e)
        {
            autoCloseTimer.Stop();
            autoCloseTimer.Tick -= AutoCloseTimer_Tick;
        }
    }
}