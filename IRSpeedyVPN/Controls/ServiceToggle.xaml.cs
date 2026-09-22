using System.Windows;
using System.Windows.Controls;
namespace IRSpeedyVPN.Controls
{
    public partial class ServiceToggle : UserControl
    {
        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(ServiceToggle), new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed));
        public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
        // Compatibility with the existing settings normalization and persistence code.
        public bool? IsChecked { get => IsOn; set => SetCurrentValue(IsOnProperty, value == true); }
        public static readonly RoutedEvent CheckedEvent = EventManager.RegisterRoutedEvent(nameof(Checked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ServiceToggle));
        public static readonly RoutedEvent UncheckedEvent = EventManager.RegisterRoutedEvent(nameof(Unchecked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ServiceToggle));
        public event RoutedEventHandler Checked { add => AddHandler(CheckedEvent, value); remove => RemoveHandler(CheckedEvent, value); }
        public event RoutedEventHandler Unchecked { add => AddHandler(UncheckedEvent, value); remove => RemoveHandler(UncheckedEvent, value); }
        public ServiceToggle() { InitializeComponent(); }
        private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ServiceToggle)d;
            control.RaiseEvent(new RoutedEventArgs((bool)e.NewValue ? CheckedEvent : UncheckedEvent, control));
        }
        private void Toggle_Click(object sender, RoutedEventArgs e) { SetCurrentValue(IsOnProperty, !IsOn); }
    }
}
