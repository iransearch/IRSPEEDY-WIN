using IRSpeedyVPN.Services.Traffic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace IRSpeedyVPN.UserControls
{
    public partial class ConnectionTrafficPanel : UserControl
    {
        private readonly ObservableCollection<TrafficRowView> liveRows = new ObservableCollection<TrafficRowView>();
        private readonly Dictionary<string, TrafficRowView> rowIndex = new Dictionary<string, TrafficRowView>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer refreshTimer;
        private bool resetting;
        private long revision = -1;

        public ConnectionTrafficPanel()
        {
            InitializeComponent();
            TrafficTable.ItemsSource = liveRows;
            refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            refreshTimer.Tick += (sender, args) => RefreshTraffic();
            RefreshTraffic();
        }

        internal void SetUpdatesEnabled(bool enabled)
        {
            if (enabled) { RefreshTraffic(); refreshTimer.Start(); }
            else refreshTimer.Stop();
        }

        private void Panel_Unloaded(object sender, RoutedEventArgs e) => refreshTimer.Stop();

        internal void RefreshTraffic()
        {
            var snapshot = TrafficUsageService.Instance.Snapshot; // already detached; no IO/lock
            if (revision == snapshot.Revision) return;
            revision = snapshot.Revision;
            var present = new HashSet<string>(snapshot.Rows.Select(row => row.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in rowIndex.Keys.Where(name => !present.Contains(name)).ToArray())
            {
                liveRows.Remove(rowIndex[name]);
                rowIndex.Remove(name);
            }
            foreach (var usage in snapshot.Rows)
            {
                if (!rowIndex.TryGetValue(usage.Name, out var row))
                {
                    row = new TrafficRowView(usage.Name);
                    rowIndex.Add(usage.Name, row);
                    liveRows.Add(row);
                }
                row.Update(usage);
            }
            // Preserve ItemsSource, selection, scroll and the user's sorting choice.
            SetSummary(snapshot.Rows.Sum(row => (decimal)row.Download), snapshot.Rows.Sum(row => (decimal)row.Upload));
            TrafficCount.Text = PersianCount(liveRows.Count) + " برنامه";
            TrafficStatus.Text = snapshot.Status;
            TrafficStatus.ToolTip = snapshot.Status;
            TrafficStatusDot.Fill = (Brush)FindResource(snapshot.Connected ? "ConnectedGreenBrush" : "IconStrokeBrush");
            TrafficSubtitle.Text = "مصرف ثبت‌شده از آخرین ریست";
            TrafficSubtitle.ToolTip = "شروع ثبت: " + snapshot.SinceUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            EmptyTrafficText.Visibility = liveRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string PersianCount(int count)
        {
            const string digits = "۰۱۲۳۴۵۶۷۸۹";
            return new string(count.ToString(CultureInfo.InvariantCulture).Select(c => digits[c - '0']).ToArray());
        }

        private void SetSummary(decimal down, decimal up)
        {
            var download = TrafficAmount.FromBytes(down);
            var upload = TrafficAmount.FromBytes(up);
            var total = TrafficAmount.FromBytes(down + up);
            DownloadSummary.Text = download.ValueText; DownloadSummaryUnit.Text = download.Unit;
            UploadSummary.Text = upload.ValueText; UploadSummaryUnit.Text = upload.Unit;
            TotalSummary.Text = total.ValueText; TotalSummaryUnit.Text = total.Unit;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetError.Text = "";
            ResetConfirmation.Visibility = Visibility.Visible;
            CancelResetButton.Focus();
        }

        internal bool DismissReset()
        {
            if (ResetConfirmation.Visibility != Visibility.Visible) return false;
            ResetConfirmation.Visibility = Visibility.Collapsed;
            return true;
        }

        private void CancelReset_Click(object sender, RoutedEventArgs e)
        { DismissReset(); ResetTrafficButton.Focus(); }

        private async void ConfirmReset_Click(object sender, RoutedEventArgs e)
        {
            if (resetting) return;
            resetting = true;
            ConfirmResetButton.IsEnabled = false;
            ResetError.Text = "";
            try
            {
                await TrafficUsageService.Instance.ResetAsync();
                revision = -1;
                RefreshTraffic();
                DismissReset();
                if (IsVisible) ResetTrafficButton.Focus();
            }
            catch
            {
                ResetError.Text = "ریست انجام نشد؛ آمار قبلی حفظ شده است. دوباره تلاش کنید.";
            }
            finally { resetting = false; ConfirmResetButton.IsEnabled = true; }
        }

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class TrafficRowView : INotifyPropertyChanged
        {
            public TrafficRowView(string name) { Name = name; }
            public string Name { get; }
            public decimal DownloadBytes { get; private set; }
            public decimal UploadBytes { get; private set; }
            public decimal TotalBytes => DownloadBytes + UploadBytes;
            public TrafficAmount Download => TrafficAmount.FromBytes(DownloadBytes);
            public TrafficAmount Upload => TrafficAmount.FromBytes(UploadBytes);
            public TrafficAmount Total => TrafficAmount.FromBytes(TotalBytes);
            public event PropertyChangedEventHandler PropertyChanged;
            public void Update(TrafficUsage row)
            {
                if (DownloadBytes == row.Download && UploadBytes == row.Upload) return;
                DownloadBytes = row.Download;
                UploadBytes = row.Upload;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
            }
        }

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class TrafficAmount
        {
            public string ValueText { get; private set; }
            public string Unit { get; private set; }
            public static TrafficAmount FromBytes(decimal bytes)
            {
                var units = new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
                int index = 0;
                while (bytes >= 1024m && index < units.Length - 1) { bytes /= 1024m; index++; }
                return new TrafficAmount { ValueText = bytes.ToString(index == 0 ? "0" : "0.##", CultureInfo.InvariantCulture), Unit = units[index] };
            }
        }
    }
}
