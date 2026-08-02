using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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

        public long? GoogleSpeed
        {
            get => ParseValue(txtGoogle.Text);
            set => SetValue(txtGoogle, value);
        }

        public long? YoutubeSpeed
        {
            get => ParseValue(txtYoutube.Text);
            set => SetValue(txtYoutube, value);
        }

        public long? InstaSpeed
        {
            get => ParseValue(txtInstagram.Text);
            set => SetValue(txtInstagram, value);
        }

        public long? TelegramSpeed
        {
            get => ParseValue(txtTelegram.Text);
            set => SetValue(txtTelegram, value);
        }

        private void btnClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private static void SetValue(TextBlock target, long? value)
        {
            if (target == null) return;
            if (value.HasValue)
            {
                target.Text = $"{value.Value}ms";
                target.Foreground = Brushes.Green;
            }
            else
            {
                target.Text = "Error";
                target.Foreground = Brushes.Red;
            }
        }

        private static long? ParseValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 2);
            }
            if (long.TryParse(text, out var val))
                return val;
            return null;
        }
    }
}
