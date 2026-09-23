using IRSpeedyVPN.Services.SplitTunneling;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Common
{
    internal sealed class InstalledApplication : INotifyPropertyChanged
    {
        public SplitTunnelApp Rule { get; set; }
        public string Name => Rule.Name;
        public string Path => Rule.Path;
        public string ExecutableName => Missing ? "پیدا نشد — " + System.IO.Path.GetFileName(Path)
            : Rule.Kind == AppMatchKind.Folder ? "همهٔ فایل‌های اجرایی این پوشه" : System.IO.Path.GetFileName(Path);
        public bool IsCustom => Rule.Source == "Manual";
        private string AppFile => System.IO.Path.GetFileName(Path).ToLowerInvariant();
        public string IconKey
        {
            get
            {
                if (IsCustom) return "File";
                switch (AppFile)
                {
                    case "chrome.exe": case "firefox.exe": case "msedge.exe": case "brave.exe": case "opera.exe": return "Globe";
                    case "telegram.exe": case "discord.exe": case "whatsapp.exe": return "Chat";
                    case "steam.exe": case "epicgameslauncher.exe": return "Game";
                    case "spotify.exe": return "Music";
                    case "outlook.exe": case "thunderbird.exe": return "Mail";
                    case "zoom.exe": case "teams.exe": case "ms-teams.exe": return "Video";
                    default: return "File";
                }
            }
        }
        public string Category => IsCustom ? "افزوده‌شده به‌صورت دستی"
            : IconKey == "Globe" ? "مرورگر اینترنت" : IconKey == "Chat" ? "پیام‌رسان"
            : IconKey == "Game" ? "بازی" : IconKey == "Music" ? "موسیقی"
            : IconKey == "Mail" ? "ایمیل" : IconKey == "Video" ? "تماس تصویری" : "برنامه ویندوز";
        public Brush AccentBrush => PresentationBrush(false);
        public Brush TintBrush => PresentationBrush(true);
        private Brush accentBrush, tintBrush;
        private Brush PresentationBrush(bool tint)
        {
            if (accentBrush == null)
            {
                var color = (Color)ColorConverter.ConvertFromString(IconKey == "Globe" ? "#1E3A8A"
                    : IconKey == "Chat" ? (AppFile == "discord.exe" ? "#5B5FEF" : "#0EA5B7")
                    : IconKey == "Game" ? "#475569" : IconKey == "Music" ? "#16A34A"
                    : IconKey == "Mail" ? "#2563EB" : IconKey == "Video" ? "#0369A1" : "#64748B");
                accentBrush = new SolidColorBrush(color); accentBrush.Freeze();
                color.A = 26; tintBrush = new SolidColorBrush(color); tintBrush.Freeze();
            }
            return tint ? tintBrush : accentBrush;
        }
        public bool Missing => Rule.Kind == AppMatchKind.Folder ? !Directory.Exists(Path) : !File.Exists(Path);
        private ImageSource icon;
        public ImageSource Icon { get => icon; set { icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); } }
        private bool selected;
        public bool Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal static class AppIconService
    {
        public static ImageSource Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is System.Security.SecurityException || ex is System.Runtime.InteropServices.ExternalException) { return null; }
        }
    }
}
