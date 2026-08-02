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

    internal enum SelectionKind { None, Smart, Country, Server }

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

    #region Signal helper — per-Url rules (latency is long)

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

        /// <summary>
        /// A Url's latency is "stale" if it was checked more than 5 minutes ago.
        /// NOTE: uses DateTime.Now. If latencychkTime is stored in UTC, swap to
        /// DateTime.UtcNow (or compare against url.latencychkTime.ToLocalTime()).
        /// </summary>
        public static bool IsStale(Url u)
        {
            if (u == null || u.latencychkTime == default) return false;
            return (DateTime.Now - u.latencychkTime).TotalMinutes > 5;
        }

        /// <summary>
        /// latency (long) + staleness → (bars, color, text).
        ///   stale or 0    → empty bars, "—"
        ///   -1            → red, full (5 bars), "—"
        ///   &gt; 2000     → orange, 1 bar
        ///   &gt; 1000     → yellow, 2 bars
        ///   &gt; 500      → blue,   3 bars
        ///   0 &lt; x ≤ 500 → green, full (5 bars)
        /// </summary>
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

        public static void FromUrl(Url u, out Geometry bars, out Brush color, out string text)
            => Evaluate(u?.latency ?? 0, IsStale(u), out bars, out color, out text);

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

    /// <summary>One IVPNService = one country header.</summary>
    internal class GroupItem : PickerItem
    {
        public IVPNService Service { get; set; }
        public string CountryName { get; set; }
        public string CountryCode { get; set; }

        /// <summary>Sub-rows: one per Url from GetServerUrls().</summary>
        public List<UrlItem> UrlItems { get; set; } = new List<UrlItem>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded == value) return; _isExpanded = value; On(); }
        }

        private bool _isSelectedCountry;
        public bool IsSelectedCountry
        {
            get => _isSelectedCountry;
            set { if (_isSelectedCountry == value) return; _isSelectedCountry = value; On(); }
        }

        private Geometry _signalBars = Geometry.Empty;
        public Geometry SignalBars { get => _signalBars; private set { _signalBars = value; On(); } }

        private Brush _signalColor = Brushes.Transparent;
        public Brush SignalColor { get => _signalColor; private set { _signalColor = value; On(); } }

        private string _signalText = "—";
        public string SignalText { get => _signalText; private set { _signalText = value; On(); } }

        /// <summary>Smallest latency that is &gt;0 and not stale; 0 if none.</summary>
        public long MinValidLatency
        {
            get
            {
                var vals = UrlItems
                    .Where(u => u.Url != null && u.Url.latency > 0 && !Sig.IsStale(u.Url))
                    .Select(u => u.Url.latency);
                return vals.Any() ? vals.Min() : 0L;
            }
        }

        public void UpdateHeaderSignal(long minValidLatency)
        {
            Sig.FromLatency(minValidLatency, out var b, out var c, out var t);
            SignalBars = b; SignalColor = c; SignalText = t;
        }

        /// <summary>Re-read every Url's latency (updated by a URL test) and redraw.</summary>
        public void RefreshSignals()
        {
            foreach (var ui in UrlItems) ui.Refresh();
            UpdateHeaderSignal(MinValidLatency);
        }
    }

    /// <summary>One Url under a service — has its own signal. Label = parent name + (position+1).</summary>
    internal class UrlItem : PickerItem
    {
        public Url Url { get; }
        public GroupItem Parent { get; }

        /// <summary>1-based position of this Url under its parent (from GetServerUrls() order).</summary>
        public int Index { get; }

        public UrlItem(Url url, GroupItem parent, int index)
        {
            Url = url;
            Parent = parent;
            Index = index;
            Refresh();
        }

        private Geometry _signalBars = Geometry.Empty;
        public Geometry SignalBars { get => _signalBars; private set { _signalBars = value; On(); } }

        private Brush _signalColor = Brushes.Transparent;
        public Brush SignalColor { get => _signalColor; private set { _signalColor = value; On(); } }

        private string _signalText = "—";
        public string SignalText { get => _signalText; private set { _signalText = value; On(); } }

        public void Refresh()
        {
            Sig.FromUrl(Url, out var b, out var c, out var t);
            SignalBars = b; SignalColor = c; SignalText = t;
        }

        /// <summary>e.g. parent "آلمان" + Index 2 → "آلمان 2".</summary>
        public string DisplayText => $"سرور {Index}";
    }

    #endregion

    /// <summary>
    /// Custom country/server picker.
    ///   - Each IVPNService = one country header.
    ///   - Sub-rows come from GetServerUrls() (one Url each, each with its own signal).
    ///   - Row label = parent country name + (position + 1).
    ///   - Header signal = the minimum *valid* latency among its Urls.
    ///   - Selecting a header  → service.SelectedServerUrl = null   (Country-level).
    ///   - Selecting a sub-row → service.SelectedServerUrl = that Url (Server-level).
    /// </summary>
    public partial class ServerCountryPicker : UserControl
    {
        private List<GroupItem> _groups = new List<GroupItem>();
        private SmartItem _smart;
        private bool _urlTest;

        private IVPNService _selectedService;
        private Url _selectedUrl;
        private SelectionKind _kind = SelectionKind.None;

        private readonly Dictionary<int, bool> _expansion = new Dictionary<int, bool>();

        /// <summary>Raised with the owning service (null = smart). SelectedServerUrl is
        /// already set on the service before this fires.</summary>
        public event Action<IVPNService> ServerSelected;

        /// <summary>Raised when the user expands a country header (priority URL tests).</summary>
        public event Action<IVPNService> GroupExpanded;

        /// <summary>
        /// The owning service. Setting from outside reads service.SelectedServerUrl to
        /// decide Country- vs Server-level highlight.
        /// </summary>
        public IVPNService SelectedService
        {
            get => _selectedService;
            set
            {
                if (value == null)
                    Apply(null, null, _urlTest ? SelectionKind.Smart : SelectionKind.None);
                else
                {
                    var url = value.SelectedServerUrl;
                    Apply(value, url, url == null ? SelectionKind.Country : SelectionKind.Server);
                }
            }
        }

        public ServerCountryPicker()
        {
            InitializeComponent();

            // StaysOpen="False" alone only closes when keyboard focus actually moves
            // (clicking a focusable control). Clicks on non-focusable areas leave the
            // popup open, so close on any mouse-down outside the face/popup too.
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnWindowPreviewMouseDown), true);
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!popup.IsOpen) return;

            var source = e.OriginalSource as DependencyObject;
            if (source == null) return;

            // clicks on the face (incl. the toggle) or inside the popup keep it open
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

        /// <summary>Build the list. Call on every service/protocol change.</summary>
        public void Load(IEnumerable<IVPNService> services, bool urlTestSupported)
        {
            _urlTest = urlTestSupported;
            var arr = services?.ToArray() ?? new IVPNService[0];

            _groups = arr
                .OrderBy(s => s.Country)
                .Select(s =>
                {
                    var urls = s.GetServerUrls() ?? new List<Url>();
                    var g = new GroupItem
                    {
                        Service = s,
                        CountryName = s.Country ?? "",
                        CountryCode = s.CountryCode,
                        IsExpanded = _expansion.TryGetValue(s.ID, out var e) && e
                    };
                    // position = order in GetServerUrls(); label uses Index = position + 1
                    g.UrlItems = urls.Select((u, i) => new UrlItem(u, g, i + 1)).ToList();
                    g.UpdateHeaderSignal(g.MinValidLatency);
                    return g;
                })
                .ToList();

            _smart = _urlTest ? new SmartItem() : null;

            var items = new List<object>();
            if (_smart != null) items.Add(_smart);
            items.AddRange(_groups);
            icCountries.ItemsSource = items;

            // restore previous selection with the right kind
            if (_selectedService == null && _urlTest)
                Apply(null, null, SelectionKind.Smart);
            else if (_selectedService != null)
            {
                var url = _selectedService.SelectedServerUrl;
                Apply(_selectedService, url, url == null ? SelectionKind.Country : SelectionKind.Server);
            }
            else
                Apply(null, null, SelectionKind.None);
        }

        /// <summary>Re-read the Url latencies for one service (after a URL test) and redraw.</summary>
        public void RefreshGroup(IVPNService service)
        {
            var g = _groups.FirstOrDefault(x => x.Service == service);
            g?.RefreshSignals();
        }

        private void Apply(IVPNService svc, Url url, SelectionKind kind)
        {
            _selectedService = svc;
            _selectedUrl = url;
            _kind = kind;
            ApplySelection();
        }

        private void ApplySelection()
        {
            // clear everything
            if (_smart != null) _smart.IsSelected = false;
            foreach (var g in _groups)
            {
                g.IsSelectedCountry = false;
                foreach (var u in g.UrlItems) u.IsSelected = false;
            }

            var group = _selectedService == null
                ? null
                : _groups.FirstOrDefault(g => g.Service == _selectedService);

            switch (_kind)
            {
                case SelectionKind.Smart:
                    if (_smart != null) _smart.IsSelected = true;
                    break;

                case SelectionKind.Country:
                    // header star only — NO sub-row dot
                    if (group != null) group.IsSelectedCountry = true;
                    break;

                case SelectionKind.Server:
                    // sub-row dot + header star (the country is active)
                    if (group != null)
                    {
                        group.IsSelectedCountry = true;
                        var ui = group.UrlItems.FirstOrDefault(u => u.Url == _selectedUrl);
                        if (ui != null) ui.IsSelected = true;
                    }
                    break;
            }

            foreach (var g in _groups)
                g.UpdateHeaderSignal(g.MinValidLatency);

            UpdateFace();
        }

        private void UpdateFace()
        {
            var group = _selectedService == null
                ? null
                : _groups.FirstOrDefault(g => g.Service == _selectedService);

            if (_kind == SelectionKind.Smart || (_selectedService == null && _urlTest))
            {
                faceBolt.Visibility = Visibility.Visible;
                faceText.Text = _smart?.Display ?? "";
            }
            else if (_kind == SelectionKind.Server && _selectedUrl != null)
            {
                // face shows the same label as the selected row: parent name + index
                faceBolt.Visibility = Visibility.Collapsed;
                var ui = group?.UrlItems.FirstOrDefault(u => u.Url == _selectedUrl);
                faceText.Text = ui != null
        ? $"{group.CountryName} - سرور {ui.Index}"
        : (group?.CountryName ?? "");
            }
            else if (group != null)
            {
                faceBolt.Visibility = Visibility.Collapsed;
                faceText.Text = group.CountryName;
            }
            else
            {
                faceBolt.Visibility = Visibility.Collapsed;
                faceText.Text = _groups.FirstOrDefault()?.CountryName ?? "";
            }
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

        #region Click routing — chevron toggles, everything else selects

        private void OnListClick(object sender, MouseButtonEventArgs e)
        {
            string tag = null;
            PickerItem item = null;

            var dep = e.OriginalSource as DependencyObject;
            while (dep != null)
            {
                if (dep is FrameworkElement fe)
                {
                    if (tag == null && fe.Tag is string t) tag = t;
                    if (item == null && fe.DataContext is PickerItem pi) item = pi;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (item == null) return;

            if (tag == "Expander" && item is GroupItem gExp)
            {
                // ONLY the chevron toggles. Popup stays open. No selection change.
                gExp.IsExpanded = !gExp.IsExpanded;
                _expansion[gExp.Service.ID] = gExp.IsExpanded;
                if (gExp.IsExpanded)
                    GroupExpanded?.Invoke(gExp.Service);
                e.Handled = true;
                return;
            }

            switch (item)
            {
                case SmartItem _:
                    Apply(null, null, SelectionKind.Smart);
                    ServerSelected?.Invoke(null);
                    ClosePopup();
                    break;

                case UrlItem ui:
                    // row pick → store the Url on its service, highlight that row
                    ui.Parent.Service.SelectedServerUrl = ui.Url;
                    Apply(ui.Parent.Service, ui.Url, SelectionKind.Server);
                    ServerSelected?.Invoke(ui.Parent.Service);
                    ClosePopup();
                    break;

                case GroupItem gBody:
                    // header pick → country-level: clear the Url, star on header, NO row dot
                    gBody.Service.SelectedServerUrl = null;
                    Apply(gBody.Service, null, SelectionKind.Country);
                    ServerSelected?.Invoke(gBody.Service);
                    ClosePopup();
                    break;
            }
        }

        #endregion
    }
}