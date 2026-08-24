using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace IRSpeedyVPN.Components.ServerListControl
{
    #region Selection kind

    internal enum SelectionKind { None, Smart, Country }

    #endregion

    #region Curved clip

    public sealed class BorderClipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return null;
            if (!(values[0] is double) || !(values[1] is double)) return null;

            double w = (double)values[0];
            double h = (double)values[1];
            if (w < double.Epsilon || h < double.Epsilon) return DependencyProperty.UnsetValue;

            CornerRadius cr = values[2] is CornerRadius c ? c : new CornerRadius(0);
            double tl = Math.Min(cr.TopLeft, Math.Min(w / 2, h / 2));
            double tr = Math.Min(cr.TopRight, Math.Min(w / 2, h / 2));
            double br = Math.Min(cr.BottomRight, Math.Min(w / 2, h / 2));
            double bl = Math.Min(cr.BottomLeft, Math.Min(w / 2, h / 2));

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(tl, 0), true, true);
                ctx.LineTo(new Point(w - tr, 0), true, false);
                ctx.ArcTo(new Point(w, tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(w, h - br), true, false);
                ctx.ArcTo(new Point(w - br, h), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(bl, h), true, false);
                ctx.ArcTo(new Point(0, h - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(0, tl), true, false);
                ctx.ArcTo(new Point(tl, 0), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    #region Signal helper

    internal static class Sig
    {
        private static readonly int[] BarHeights = { 5, 8, 11, 14, 17 };

        private const string Gray = "#94A3B8";
        private const string Red = "#EF4444";
        private const string Green = "#10B981";
        private const string Blue = "#3B82F6";
        private const string Yellow = "#EAB308";
        private const string Orange = "#F97316";

        public static Geometry MakeBars(int n)
        {
            if (n <= 0) return Geometry.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < n && i < BarHeights.Length; i++)
            {
                int x = i * 5;
                int top = 17 - BarHeights[i];
                sb.Append($"M{x},{top} H{x + 3} V17 H{x} Z ");
            }
            return Geometry.Parse(sb.ToString().Trim());
        }

        public static bool IsStale(Url u)
        {
            if (u == null || u.latencychkTime == default(DateTime)) return false;
            return (DateTime.Now - u.latencychkTime).TotalMinutes > 15;
        }

        public static void Evaluate(long latency, bool stale, out Geometry bars, out Brush color, out string text)
        {
            int level;
            string hex;

            if (stale) { level = 0; hex = Gray; text = "—"; }
            else if (latency == 0) { level = 0; hex = Gray; text = "—"; }
            else if (latency == -1) { level = 5; hex = Red; text = "—"; }
            else if (latency > 2000) { level = 1; hex = Orange; text = latency + " ms"; }
            else if (latency > 1000) { level = 2; hex = Yellow; text = latency + " ms"; }
            else if (latency > 500) { level = 3; hex = Blue; text = latency + " ms"; }
            else { level = 5; hex = Green; text = latency + " ms"; }

            bars = MakeBars(level);
            color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public static void FromLatency(long latency, out Geometry bars, out Brush color, out string text)
            => Evaluate(latency, false, out bars, out color, out text);
    }

    #endregion

    #region Models

    internal abstract class PickerItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; On(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void On([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    internal class SmartItem : PickerItem
    {
        public string Display => "انتخاب هوشمند سریعترین سرور";
    }

    /// <summary>
    /// One visible numbered country row = one IVPNService/API service record. When an
    /// API response contains two German records they stay distinct as "آلمان 1" and
    /// "آلمان 2". Every URL inside that record belongs to this row's Smart pool.
    /// </summary>
    internal class GroupItem : PickerItem
    {
        public IVPNService Service { get; set; }
        public string CountryName { get; set; }
        public string CountryCode { get; set; }

        private bool _isSelectedCountry;
        public bool IsSelectedCountry
        {
            get => _isSelectedCountry;
            set { if (_isSelectedCountry == value) return; _isSelectedCountry = value; On(); }
        }

        private bool _isSelectable;
        public bool IsSelectable
        {
            get => _isSelectable;
            private set { if (_isSelectable == value) return; _isSelectable = value; On(); }
        }

        private Geometry _signalBars = Geometry.Empty;
        public Geometry SignalBars { get => _signalBars; private set { _signalBars = value; On(); } }

        private Brush _signalColor = Brushes.Transparent;
        public Brush SignalColor { get => _signalColor; private set { _signalColor = value; On(); } }

        private string _signalText = "—";
        public string SignalText { get => _signalText; private set { _signalText = value; On(); } }

        public List<Url> GetUrls()
        {
            return (Service?.GetServerUrls() ?? new List<Url>())
                .Where(u => u != null)
                .ToList();
        }

        public string[] GetPoolUrls()
        {
            return GetUrls()
                .Select(u => u.url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public void RefreshSignals()
        {
            var urls = GetUrls();
            var positive = urls
                .Where(u => u.latency > 0 && !Sig.IsStale(u))
                .Select(u => u.latency)
                .ToArray();

            if (positive.Length > 0)
            {
                IsSelectable = true;
                Sig.FromLatency(positive.Min(), out var bars, out var color, out var text);
                SignalBars = bars;
                SignalColor = color;
                SignalText = text;
                return;
            }

            IsSelectable = false;
            var allFresh = urls.Count > 0 && urls.All(u =>
                u.latencychkTime != default(DateTime) && !Sig.IsStale(u));

            // Gray = not tested/stale. Red = every URL in this row was tested recently
            // and none returned a positive result. Only rows with a positive result can
            // be selected; blue/yellow/orange are valid just like green.
            Sig.FromLatency(allFresh ? -1 : 0, out var emptyBars, out var emptyColor, out var emptyText);
            SignalBars = emptyBars;
            SignalColor = emptyColor;
            SignalText = emptyText;
        }
    }

    #endregion

    /// <summary>
    /// Picker with no per-server expansion. Duplicate countries remain numbered rows.
    /// Each row shows the minimum positive latency across all URLs in its own service
    /// record. Selecting it sends ALL URLs in that row to the existing Smart Fast Xray
    /// leastLoad balancer; failed URL-test results never filter the runtime pool.
    /// </summary>
    public partial class ServerCountryPicker : UserControl
    {
        private List<GroupItem> _groups = new List<GroupItem>();
        private SmartItem _smart;
        private bool _urlTest;

        private IVPNService _selectedService;
        private GroupItem _selectedGroup;
        private SelectionKind _kind = SelectionKind.None;

        public event Action<IVPNService> ServerSelected;

        public IVPNService SelectedService
        {
            get => _selectedService;
            set
            {
                if (value == null)
                {
                    Apply(null, null, _urlTest ? SelectionKind.Smart : SelectionKind.None);
                    return;
                }

                var group = _groups.FirstOrDefault(g => ReferenceEquals(g.Service, value));
                if (group == null)
                {
                    Apply(value, null, SelectionKind.None);
                    return;
                }

                // Any matched row is a country selection, so it always owns a live pool.
                // Rebuilding it unconditionally matters on startup and on service
                // reloads: the connection service is fresh there, so IsSmartFast is
                // still false and a guarded rebuild would leave the row with no pool.
                // Without a pool the connection skips the smart balancer config, and
                // the VOD and AI balancers that live in it never reach the core.
                PrepareCountryPool(group, value);

                Apply(value, group, SelectionKind.Country);
            }
        }

        public ServerCountryPicker()
        {
            InitializeComponent();

            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnWindowPreviewMouseDown), true);
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!popup.IsOpen) return;

            var source = e.OriginalSource as DependencyObject;
            if (source == null) return;

            if (IsSelfOrDescendant(source, root) || IsSelfOrDescendant(source, popup.Child))
                return;

            ClosePopup();
        }

        private static bool IsSelfOrDescendant(DependencyObject child, DependencyObject ancestor)
        {
            for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
                if (ReferenceEquals(d, ancestor)) return true;
            return false;
        }

        public void Load(IEnumerable<IVPNService> services, bool urlTestSupported)
        {
            _urlTest = urlTestSupported;
            var arr = services?.ToArray() ?? new IVPNService[0];
            var previousServiceId = _selectedGroup?.Service?.ID;

            _groups = arr
                .OrderBy(s => s.Country)
                .ThenBy(s => s.ID)
                .Select(service =>
                {
                    var group = new GroupItem
                    {
                        Service = service,
                        CountryCode = service.CountryCode,
                        CountryName = string.IsNullOrWhiteSpace(service.Country)
                            ? (service.CountryCode ?? "")
                            : service.Country
                    };
                    group.RefreshSignals();
                    return group;
                })
                .ToList();

            _smart = _urlTest ? new SmartItem() : null;

            var items = new List<object>();
            if (_smart != null) items.Add(_smart);
            items.AddRange(_groups);
            icCountries.ItemsSource = items;

            if (_selectedService == null && _urlTest)
            {
                Apply(null, null, SelectionKind.Smart);
                return;
            }

            var selected = _selectedService == null
                ? null
                : _groups.FirstOrDefault(g => ReferenceEquals(g.Service, _selectedService));

            if (selected == null && previousServiceId.HasValue)
                selected = _groups.FirstOrDefault(g => g.Service != null && g.Service.ID == previousServiceId.Value);

            if (selected != null)
            {
                var service = _selectedService ?? selected.Service;
                PrepareCountryPool(selected, service);
                Apply(service, selected, SelectionKind.Country);
            }
            else
                Apply(null, null, _urlTest ? SelectionKind.Smart : SelectionKind.None);
        }

        /// <summary>Refresh the numbered row that owns this service after URL testing.</summary>
        public void RefreshGroup(IVPNService service)
        {
            var group = _groups.FirstOrDefault(g => ReferenceEquals(g.Service, service));
            group?.RefreshSignals();
        }

        private bool PrepareCountryPool(GroupItem group, IVPNService preferredService = null)
        {
            if (group == null || !group.IsSelectable)
                return false;

            var connectionService = preferredService ?? group.Service;
            var smart = connectionService as ISmartFastConnection;
            if (smart == null)
                return false;

            var poolUrls = group.GetPoolUrls();
            if (poolUrls.Length == 0)
                return false;

            // Intentionally send EVERY URL in this numbered row to Xray. URL tests only
            // decide row availability and the minimum latency shown in the picker.
            smart.SetSmartFastUrls(poolUrls);

            // Marker used by UCServerList to distinguish a row-scoped Smart connection
            // from global Smart/Fast, which keeps SelectedServerUrl null.
            connectionService.SelectedServerUrl = group.GetUrls().FirstOrDefault();
            return true;
        }

        private void Apply(IVPNService service, GroupItem group, SelectionKind kind)
        {
            _selectedService = service;
            _selectedGroup = group;
            _kind = kind;
            ApplySelection();
        }

        private void ApplySelection()
        {
            if (_smart != null) _smart.IsSelected = false;
            foreach (var group in _groups)
                group.IsSelectedCountry = false;

            if (_kind == SelectionKind.Smart)
            {
                if (_smart != null) _smart.IsSelected = true;
            }
            else if (_kind == SelectionKind.Country && _selectedGroup != null)
            {
                _selectedGroup.IsSelectedCountry = true;
            }

            UpdateFace();
        }

        private void UpdateFace()
        {
            if (_kind == SelectionKind.Smart || (_selectedService == null && _urlTest))
            {
                faceBolt.Visibility = Visibility.Visible;
                faceText.Text = _smart?.Display ?? "";
                return;
            }

            faceBolt.Visibility = Visibility.Collapsed;
            faceText.Text = _selectedGroup?.CountryName ?? _selectedService?.Country ?? "";
        }

        #region Popup open / close

        private void BtnToggle_Checked(object sender, RoutedEventArgs e) => popup.IsOpen = true;
        private void BtnToggle_Unchecked(object sender, RoutedEventArgs e) => popup.IsOpen = false;
        private void Popup_Closed(object sender, EventArgs e) => btnToggle.IsChecked = false;

        private void ClosePopup()
        {
            popup.IsOpen = false;
            btnToggle.IsChecked = false;
        }

        #endregion

        #region Click routing

        private void OnListClick(object sender, MouseButtonEventArgs e)
        {
            PickerItem item = null;
            for (var dep = e.OriginalSource as DependencyObject; dep != null; dep = VisualTreeHelper.GetParent(dep))
            {
                if (dep is FrameworkElement element && element.DataContext is PickerItem pickerItem)
                {
                    item = pickerItem;
                    break;
                }
            }

            if (item is SmartItem)
            {
                Apply(null, null, SelectionKind.Smart);
                ServerSelected?.Invoke(null);
                ClosePopup();
                e.Handled = true;
                return;
            }

            if (item is GroupItem group)
            {
                if (!group.IsSelectable)
                {
                    e.Handled = true;
                    return;
                }

                var connectionService = group.Service;
                if (!PrepareCountryPool(group, connectionService))
                {
                    e.Handled = true;
                    return;
                }

                Apply(connectionService, group, SelectionKind.Country);
                ServerSelected?.Invoke(connectionService);
                ClosePopup();
                e.Handled = true;
            }
        }

        #endregion
    }
}