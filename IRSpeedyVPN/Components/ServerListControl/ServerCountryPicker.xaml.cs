using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Common;
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
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Components.ServerListControl
{
    #region Selection kind

    internal enum SelectionKind { None, Smart, Country }

    #endregion

    #region Signal helper

    /// <summary>
    /// Latency -> the "38 ms" style green text used by the DESIGN-SPEC row (§3
    /// "Server list", "Latency: bold green"). Rows without a fresh positive
    /// result are not selectable and show a muted placeholder instead.
    /// </summary>
    internal static class Sig
    {
        public static bool IsStale(Url u)
        {
            if (u == null || u.latencychkTime == default(DateTime)) return false;
            return (DateTime.Now - u.latencychkTime).TotalMinutes > 15;
        }

        public static void Evaluate(long latency, bool stale, out string text)
        {
            if (stale) text = "—";
            else if (latency == 0) text = "—";
            else if (latency == -1) text = "—";
            else text = latency.ToString(CultureInfo.InvariantCulture) + " ms";
        }

        public static void FromLatency(long latency, out string text)
            => Evaluate(latency, false, out text);
    }

    #endregion

    #region Flags

    /// <summary>
    /// Maps the ISO country codes returned by the backend (IVPNService.CountryCode,
    /// e.g. "DE"/"NL"/"TR"/"FR"/"GB") to the flat, geometric flag art shipped in the
    /// design handoff, with a bundled ISO flag set for the other countries.
    /// Unknown/non-country codes keep the existing initials fallback.
    /// </summary>
    internal static class FlagCatalog
    {
        private static readonly Dictionary<string, string> CodeToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DE", "germany" },
            { "NL", "netherlands" },
            { "TR", "turkey" },
            { "FR", "france" },
            { "GB", "uk" },
            { "UK", "uk" },
            { "US", "usa" }, { "CA", "canada" }, { "AE", "uae" },
            { "SG", "singapore" }, { "JP", "japan" },
        };

        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource TryGet(string countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode)) return null;
            var code = countryCode.Trim().ToUpperInvariant();
            if (!CodeToFile.TryGetValue(code, out var file))
            {
                if (code.Length != 2 || code.Any(c => c < 'A' || c > 'Z')) return null;
                file = "Iso/" + code.ToLowerInvariant();
            }

            if (Cache.TryGetValue(file, out var cached)) return cached;

            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Irspeedy/Flags/{file}.png", UriKind.Absolute);
                var img = new BitmapImage(uri);
                img.Freeze();
                Cache[file] = img;
                return img;
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource TryGetListFlag(string countryCode)
        {
            var code = (countryCode ?? "").Trim().ToUpperInvariant();
            if (code == "UK") code = "GB";
            return Application.Current?.TryFindResource("ServerListFlag_" + code) as ImageSource
                ?? TryGet(countryCode);
        }

        public static string Initials(string countryName)
        {
            if (string.IsNullOrWhiteSpace(countryName)) return "?";
            return countryName.Trim().Substring(0, 1).ToUpperInvariant();
        }
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

        public ImageSource FlagSource { get; private set; }
        public bool HasFlag => FlagSource != null;
        public string Initials { get; private set; }

        public Cursor RowCursor => IsSelectable ? Cursors.Hand : Cursors.Arrow;

        public void ResolveFlag()
        {
            FlagSource = FlagCatalog.TryGetListFlag(CountryCode);
            Initials = FlagCatalog.Initials(CountryName);
            On(nameof(FlagSource));
            On(nameof(HasFlag));
            On(nameof(Initials));
        }

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
            private set { if (_isSelectable == value) return; _isSelectable = value; On(); On(nameof(RowCursor)); }
        }

        private string _signalText = "—";
        public string SignalText { get => _signalText; private set { _signalText = value; On(); } }

        private Brush _signalBrush = Brushes.Gray;
        public Brush SignalBrush { get => _signalBrush; private set { _signalBrush = value; On(); } }
        private void SetSignalColor(long latency)
        {
            var color = latency <= 0 ? "#9CA3B4" : latency <= 45 ? "#17A366" : latency <= 75 ? "#F59E0B" : "#DC2626";
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            SignalBrush = brush;
        }

        // Best (lowest) positive latency in this row, used to order the list from
        // fastest to slowest. Rows with no positive result sink to the bottom.
        public long SortKey { get; private set; } = long.MaxValue;

        public void ShowProgress(long latency)
        {
            if (latency <= 0) return;
            IsSelectable = true;
            Sig.FromLatency(latency, out var text);
            SignalText = text;
            SetSignalColor(latency);
            // SortKey remains the last completed result until RefreshSignals().
        }

        public List<Url> GetUrls()
        {
            return (Service?.GetServerUrls() ?? new List<Url>())
                .Where(u => u != null)
                .ToList();
        }

        public string[] GetPoolUrls()
        {
            return GetUrls()
                // hy2 first, so the balancer's fallback outbound is one that comes up
                // quickly while its own probes are still warming up.
                .OrderByHysteriaFirst()
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
                var best = positive.Min();
                SortKey = best;
                Sig.FromLatency(best, out var text);
                SignalText = text;
                SetSignalColor(best);
                return;
            }

            IsSelectable = false;
            SortKey = long.MaxValue;
            var allFresh = urls.Count > 0 && urls.All(u =>
                u.latencychkTime != default(DateTime) && !Sig.IsStale(u));

            // "—" = not tested/stale, or every URL in this row was tested recently and
            // none returned a positive result. Only rows with a positive result can be
            // selected.
            Sig.FromLatency(allFresh ? -1 : 0, out var emptyText);
            SignalText = emptyText;
            SetSignalColor(0);
        }
    }

    #endregion

    /// <summary>
    /// Inline server list per DESIGN-SPEC §3: a fixed "Smart Location" card on top and
    /// scrollable numbered country rows underneath -- no dropdown any more. Duplicate
    /// countries remain numbered rows. Each row shows the minimum positive latency
    /// across all URLs in its own service record. Selecting it sends ALL URLs in that
    /// row to the existing Smart Fast Xray leastLoad balancer; failed URL-test results
    /// never filter the runtime pool.
    /// </summary>
    public partial class ServerCountryPicker : UserControl
    {
        private List<GroupItem> _groups = new List<GroupItem>();
        private List<GroupItem> _allGroups = new List<GroupItem>();
        private SmartItem _smart;
        private bool _urlTest;
        private string _filter = "";

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

                var group = _allGroups.FirstOrDefault(g => ReferenceEquals(g.Service, value));
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
        }

        public void Load(IEnumerable<IVPNService> services, bool urlTestSupported)
        {
            _urlTest = urlTestSupported;
            var arr = services?.ToArray() ?? new IVPNService[0];
            var previousServiceId = _selectedGroup?.Service?.ID;

            _allGroups = arr
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
                    group.ResolveFlag();
                    group.RefreshSignals();
                    return group;
                })
                .ToList();

            _smart = _urlTest ? new SmartItem() : null;
            SmartCardHost.Visibility = _urlTest ? Visibility.Visible : Visibility.Collapsed;

            ApplyFilter();

            // Order by any results already cached from a previous session.
            ResortGroups();

            if (_selectedService == null && _urlTest)
            {
                Apply(null, null, SelectionKind.Smart);
                return;
            }

            var selected = _selectedService == null
                ? null
                : _allGroups.FirstOrDefault(g => ReferenceEquals(g.Service, _selectedService));

            if (selected == null && previousServiceId.HasValue)
                selected = _allGroups.FirstOrDefault(g => g.Service != null && g.Service.ID == previousServiceId.Value);

            if (selected != null)
            {
                var service = _selectedService ?? selected.Service;
                PrepareCountryPool(selected, service);
                Apply(service, selected, SelectionKind.Country);
            }
            else
                Apply(null, null, _urlTest ? SelectionKind.Smart : SelectionKind.None);
        }

        /// <summary>Free-text filter over the country rows (used by the search field).</summary>
        public void SetFilter(string text)
        {
            _filter = text ?? "";
            ApplyFilter();
            ServerScroller.ScrollToTop();
        }

        private void ApplyFilter()
        {
            _groups = string.IsNullOrWhiteSpace(_filter)
                ? _allGroups
                : _allGroups.Where(g => g.CountryName != null &&
                    g.CountryName.IndexOf(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();

            icCountries.ItemsSource = _groups;
            EmptyResults.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Display progress in place without changing ordering or selection.</summary>
        public void ShowGroupProgress(IVPNService service, long latency)
        {
            var group = _allGroups.FirstOrDefault(g => ReferenceEquals(g.Service, service));
            group?.ShowProgress(latency);
        }

        /// <summary>Refresh the numbered row that owns this service after URL testing.</summary>
        public void RefreshGroup(IVPNService service)
        {
            var group = _allGroups.FirstOrDefault(g => ReferenceEquals(g.Service, service));
            if (group == null)
                return;

            group.RefreshSignals();
            ResortGroups();
        }

        /// <summary>
        /// Reorders the rows from fastest to slowest as their tests come in, keeping
        /// the current filter and selection intact.
        /// </summary>
        private void ResortGroups()
        {
            var ordered = _allGroups
                .OrderBy(g => g.SortKey)
                .ThenBy(g => g.CountryName, StringComparer.CurrentCulture)
                .ThenBy(g => g.Service?.ID ?? int.MaxValue)
                .ToList();

            if (ordered.SequenceEqual(_allGroups))
                return;

            _allGroups = ordered;
            ApplyFilter();
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
            foreach (var group in _allGroups)
                group.IsSelectedCountry = false;

            if (_kind == SelectionKind.Smart)
            {
                if (_smart != null) _smart.IsSelected = true;
            }
            else if (_kind == SelectionKind.Country && _selectedGroup != null)
            {
                _selectedGroup.IsSelectedCountry = true;
            }

            smartCheck.Visibility = (_kind == SelectionKind.Smart) ? Visibility.Visible : Visibility.Collapsed;
        }

        #region Click routing

        private void SmartCard_Click(object sender, RoutedEventArgs e)
        {
            if (_smart == null) return;
            Apply(null, null, SelectionKind.Smart);
            ServerSelected?.Invoke(null);
        }

        private void Row_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is GroupItem group))
                return;

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
            e.Handled = true;
        }

        #endregion
    }
}
