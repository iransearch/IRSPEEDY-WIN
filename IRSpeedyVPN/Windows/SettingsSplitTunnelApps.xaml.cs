using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.SplitTunneling;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SettingsSplitTunnelApps : Window
    {
        private List<InstalledApplication> apps = new List<InstalledApplication>();
        private SplitTunnelSettings settings;
        private CancellationTokenSource loading;
        private bool ready;
        private bool closed;
        public SettingsSplitTunnelApps() { InitializeComponent(); }
        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private bool Connected => (AppServices.GlobalInfo?.CurrentService as TunnelPlusService)?.IsTunnelConnected == true;
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Connected) { Close(); return; }
            try { settings = SplitTunnelStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "تقسیم تونل");
                Close(); return;
            }
            ModeBox.SelectedIndex = (int)settings.Mode;
            apps = settings.Apps.Select(a => new InstalledApplication { Rule = a, Selected = true }).ToList();
            foreach (var app in apps) app.PropertyChanged += SelectionChanged;
            await RefreshAppsAsync();
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAppsAsync(true);
        private async Task RefreshAppsAsync(bool refresh = false)
        {
            if (settings == null || closed) return;
            loading?.Cancel();
            var request = new CancellationTokenSource();
            loading = request;
            RefreshButton.IsEnabled = false;
            EmptyText.Text = "در حال خواندن برنامه‌ها…";
            EmptyText.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                var found = await AppDiscoveryService.ReadAsync(request.Token, refresh);
                if (closed || request.IsCancellationRequested) return;
                var previous = apps.GroupBy(a => a.Rule.Identity, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var app in apps) app.PropertyChanged -= SelectionChanged;
                apps = found.Select(a => new InstalledApplication { Rule = a,
                    Selected = previous.ContainsKey(a.Identity) && previous[a.Identity].Selected }).ToList();
                var ids = new HashSet<string>(apps.Select(a => a.Rule.Identity), StringComparer.OrdinalIgnoreCase);
                apps.AddRange(previous.Values.Where(a => !ids.Contains(a.Rule.Identity)));
                apps = apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var app in apps) app.PropertyChanged += SelectionChanged;
                ready = true;
                RenderList();
                // The list is already usable; icons are optional and loaded afterwards.
                foreach (var app in apps.ToArray())
                {
                    if (closed || request.IsCancellationRequested) break;
                    var icon = await Task.Run(() => AppIconService.Read(app.Path));
                    if (!closed && !request.IsCancellationRequested) app.Icon = icon;
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (!closed && !request.IsCancellationRequested)
                {
                    ready = true;
                    RenderList();
                    EmptyText.Text = "خواندن کامل فهرست ممکن نشد؛ تازه‌سازی یا افزودن دستی را امتحان کنید.";
                    EmptyText.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                if (loading == request) { loading = null; if (!closed) RefreshButton.IsEnabled = true; }
                request.Dispose();
            }
        }
        private List<InstalledApplication> Filtered()
        {
            var query = SearchBox.Text.Trim();
            return apps.Where(a => (a.Name ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || a.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        private void RenderList()
        {
            if (!ready) return;
            var filtered = Filtered();
            AppList.ItemsSource = filtered;
            EmptyText.Text = "برنامه‌ای پیدا نشد؛ فایل اجرایی را دستی اضافه کنید.";
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCount();
        }
        private void UpdateCount() => SelectedCountText.Text = PersianDigits.Format(apps.Count(a => a.Selected) + " برنامه انتخاب شده");
        private void SelectionChanged(object sender, PropertyChangedEventArgs e) { if (e.PropertyName == "Selected") UpdateCount(); }
        private void Search_Changed(object sender, TextChangedEventArgs e) { RenderList(); AppScroll?.ScrollToTop(); }
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            var filtered = Filtered();
            bool select = filtered.Any(a => !a.Selected);
            foreach (var app in filtered) app.Selected = select;
        }
        private void AddExecutable_Click(object sender, RoutedEventArgs e)
        {
            if (!ready || Connected) return;
            var picker = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Multiselect = true, CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            foreach (var path in picker.FileNames)
            {
                var app = AppDiscoveryService.FromExecutable(path);
                if (app != null) Add(app);
            }
        }
        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!ready || Connected) return;
            using (var picker = new System.Windows.Forms.FolderBrowserDialog
            { Description = "همهٔ فایل‌های اجرایی پوشه و زیرپوشه‌ها انتخاب می‌شوند", ShowNewFolderButton = false })
            {
                if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try { Add(AppDiscoveryService.FromFolder(picker.SelectedPath)); }
                catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "انتخاب پوشه"); }
            }
        }
        private void Add(SplitTunnelApp rule)
        {
            var existing = apps.FirstOrDefault(a => string.Equals(a.Rule.Identity, rule.Identity, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Selected = true;
            else
            {
                var app = new InstalledApplication { Rule = rule, Selected = true };
                app.PropertyChanged += SelectionChanged;
                apps.Add(app);
            }
            SearchBox.Clear();
            RenderList();
        }
        private void Mode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ModeHintText == null) return;
            ModeHintText.Text = ModeBox.SelectedIndex == 1
                ? "برنامه‌های انتخاب‌شده از مسیر VPN استفاده می‌کنند؛ سایر برنامه‌های شناسایی‌شده مستقیم هستند."
                : "برنامه‌های انتخاب‌شده مستقیم متصل می‌شوند؛ بقیه تابع قوانین VPN هستند.";
        }
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (!ready || Connected) return;
            settings.Mode = (SplitTunnelMode)Math.Max(0, ModeBox.SelectedIndex);
            settings.Apps = apps.Where(a => a.Selected).Select(a => a.Rule).ToList();
            try { SplitTunnelStore.Save(settings); DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ذخیره تقسیم تونل"); }
        }
        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            loading?.Cancel();
            foreach (var app in apps) app.PropertyChanged -= SelectionChanged;
            base.OnClosed(e);
        }
    }
}
