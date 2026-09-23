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
