using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRSpeedyVPN.Windows
{
    public partial class SettingsSplitTunnelApps : Window
    {
        private const int PageSize = 7;
        private const string SelectionKey = "SplitTunnelPreviewApplications";
        private List<InstalledApplication> apps = new List<InstalledApplication>();
        private HashSet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int page;
        private bool ready;
        public SettingsSplitTunnelApps() { InitializeComponent(); }
        private void Header_DragMove(object sender, MouseButtonEventArgs e) => Common.WindowDrag.Begin(this, e);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var raw = RegHelper.GetSettingValue(SelectionKey);
                if (!string.IsNullOrWhiteSpace(raw)) saved.UnionWith(JsonConvert.DeserializeObject<string[]>(raw) ?? new string[0]);
            }
            catch (JsonException) { }
            try
            {
                apps = await Task.Run(() => InstalledApplications.Read());
                if (!IsLoaded) return;
                foreach (var app in apps)
                {
                    app.Selected = saved.Contains(app.Path);
                    app.PropertyChanged += SelectionChanged;
                }
                ready = true;
                RenderPage();
            }
            catch { EmptyText.Text = "خواندن برنامه‌ها ممکن نشد؛ پنجره را دوباره باز کنید."; }
        }
        private List<InstalledApplication> Filtered()
        {
            var query = SearchBox.Text.Trim();
            return apps.Where(a => a.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || a.ExecutableName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        private void RenderPage()
        {
            if (!ready) return;
            var filtered = Filtered();
            int pages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
            page = Math.Max(0, Math.Min(page, pages - 1));
            AppList.ItemsSource = filtered.Skip(page * PageSize).Take(PageSize).ToList();
            EmptyText.Text = "برنامه‌ای با مسیر اجرایی ثبت‌شده پیدا نشد.";
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PreviousButton.IsEnabled = page > 0;
            NextButton.IsEnabled = page + 1 < pages;
            PageText.Text = PersianDigits.Format($"{page + 1} / {pages}");
            UpdateCount();
        }
        private void UpdateCount() => SelectedCount.Text = PersianDigits.Format(apps.Count(a => a.Selected) + " برنامه انتخاب شده");
        private void SelectionChanged(object sender, PropertyChangedEventArgs e) => UpdateCount();
        private void Search_Changed(object sender, TextChangedEventArgs e) { page = 0; RenderPage(); }
        private void Previous_Click(object sender, RoutedEventArgs e) { page--; RenderPage(); }
        private void Next_Click(object sender, RoutedEventArgs e) { page++; RenderPage(); }
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            var filtered = Filtered();
            bool select = filtered.Any(a => !a.Selected);
            foreach (var app in filtered) app.Selected = select;
        }
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            // Retain saved paths not currently discoverable (e.g. an unmounted drive).
            foreach (var app in apps) { if (app.Selected) saved.Add(app.Path); else saved.Remove(app.Path); }
            RegHelper.SetSettingValue(SelectionKey, JsonConvert.SerializeObject(saved.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)));
            Close();
        }
        protected override void OnClosed(EventArgs e)
        {
            foreach (var app in apps) app.PropertyChanged -= SelectionChanged;
            base.OnClosed(e);
        }
    }
}
