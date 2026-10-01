using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    /// <summary>
    /// Interaction logic for PingResult.xaml
    /// </summary>
    public partial class PingResult : Window
    {
        public PingResult()
        {
            InitializeComponent();
        }

        public bool GoogleConfirmed
        {
            get => txtGoogle.Text == "تأیید شد";
            set => SetValue(txtGoogle, value);
        }

        public bool YoutubeConfirmed
        {
            get => txtYoutube.Text == "تأیید شد";
            set => SetValue(txtYoutube, value);
        }

        public bool InstagramConfirmed
        {
            get => txtInstagram.Text == "تأیید شد";
            set => SetValue(txtInstagram, value);
        }

        public bool TelegramConfirmed
        {
            get => txtTelegram.Text == "تأیید شد";
            set => SetValue(txtTelegram, value);
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private static void SetValue(TextBlock target, bool confirmed)
        {
            if (target == null) return;
            if (confirmed)
            {
                target.Text = "تأیید شد";
                target.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ConnectedGreenBrush");
            }
            else
            {
                target.Text = "ناموفق";
                target.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Theme.DangerBrush");
            }
        }
    }
}
