using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;

namespace IRSpeedyVPN.Components.ServerListControl
{
    // Immutable visual snapshot: a live GroupItem must never change the OLD side
    // of a reveal when another probe result arrives.
    public sealed class ServerItem
    {
        public bool IsResidential { get; }
        public string City { get; }
        public string Isp { get; }
        public string DisplayIsp { get; }
        public string RowToolTip { get; }
        public bool IsFastest { get; }
        public bool ProbeFailed { get; }
        public bool HasSuccessfulTest => IsResidential ? SortKey > 0 && SortKey < long.MaxValue : IsSelectable;
        public double RowHeight => IsResidential ? 66 : 52;
        public double FlagSize => IsResidential ? 34 : 30;
        public System.Windows.Point FlagCenter => new System.Windows.Point(FlagSize / 2, FlagSize / 2);
        public double FlagRadius => FlagSize / 2;
        public string AutomationDescription => CountryName + ", " + City + ", " + SignalText
            + (IsSelectedCountry ? ", selected" : ", not selected");
        public string Identity { get; }
        public IVPNService Service { get; }
        public string CountryName { get; }
        public long SortKey { get; }
        public ImageSource FlagSource { get; }
        public bool HasFlag => FlagSource != null;
        public string Initials { get; }
        public string SignalText { get; }
        public string SignalToolTip { get; }
        public Brush SignalBrush { get; }
        public bool IsSelectable { get; }
        public bool IsChecking { get; }
        public bool IsSelectedCountry { get; }
        public bool IsLastSuccessfulConnection { get; }
        public Cursor RowCursor => IsSelectable ? Cursors.Hand : Cursors.Arrow;

        internal ServerItem(GroupItem group)
        {
            IsResidential = group.IsResidential; City = group.City; Isp = group.Isp;
            DisplayIsp = group.DisplayIsp; RowToolTip = group.RowToolTip; IsFastest = group.IsFastest; ProbeFailed = group.ProbeFailed;
            Identity = LastSuccessfulServer.Key(group.Service);
            Service = group.Service;
            CountryName = group.CountryName;
            SortKey = group.SortKey;
            FlagSource = group.FlagSource;
            Initials = group.Initials;
            SignalText = group.DisplaySignalText;
            SignalToolTip = group.ProbeFailed ? group.SignalToolTip + "\nبرای تلاش مجدد کلیک کنید." : group.SignalToolTip;
            // Keep the live theme brush so a theme change still reaches cached rows.
            SignalBrush = group.DisplaySignalBrush;
            IsSelectable = group.IsSelectable;
            IsChecking = group.IsChecking;
            IsSelectedCountry = group.IsSelectedCountry;
            IsLastSuccessfulConnection = group.IsLastSuccessfulConnection;
        }

        internal bool LooksLike(ServerItem other)
        {
            return other != null && IsResidential == other.IsResidential && City == other.City && Isp == other.Isp
                && IsFastest == other.IsFastest && ProbeFailed == other.ProbeFailed
                && Identity == other.Identity && CountryName == other.CountryName
                && ReferenceEquals(FlagSource, other.FlagSource) && Initials == other.Initials
                && SignalText == other.SignalText && SignalToolTip == other.SignalToolTip
                && ReferenceEquals(SignalBrush, other.SignalBrush) && IsSelectable == other.IsSelectable
                && IsChecking == other.IsChecking && IsSelectedCountry == other.IsSelectedCountry
                && IsLastSuccessfulConnection == other.IsLastSuccessfulConnection;
        }
    }

    // Numeric IDs are also used by the deferred XAML row template. Keep them
    // stable: enum member names may change during release obfuscation.
    public enum ServerRowPart { None = -1, Signal = 0, Badge = 1, Name = 2, Flag = 3, Selection = 4 }
    public sealed class ServerDisplaySlot : INotifyPropertyChanged
    {
        private readonly ServerItem[] _parts = new ServerItem[5];
        private bool _selectable;
        public ServerItem DisplayedServer { get; private set; }
        public ServerItem SignalServer => _parts[0];
        public ServerItem BadgeServer => _parts[1];
        public ServerItem NameServer => _parts[2];
        public ServerItem FlagServer => _parts[3];
        public ServerItem SelectionServer => _parts[4];
        public bool IsVacant => _parts.All(item => item == null);
        // Membership changes are applied instantly; sorting never vacates a slot.
        public bool IsSelectable => _selectable;
        public Cursor RowCursor => IsVacant ? Cursors.Arrow : DisplayedServer?.RowCursor ?? Cursors.Arrow;
        public event PropertyChangedEventHandler PropertyChanged;
        private static readonly string[] PartNames = { nameof(SignalServer), nameof(BadgeServer), nameof(NameServer), nameof(FlagServer), nameof(SelectionServer) };
        internal ServerItem GetPart(ServerRowPart part) => _parts[(int)part];
        internal void AssignPart(ServerRowPart part, ServerItem item)
        {
            _parts[(int)part] = item;
            if (part == ServerRowPart.Name)
            {
                DisplayedServer = item;
                if (item != null) _selectable = item.IsSelectable;
            }
        }
        internal void NotifyPart(ServerRowPart part)
        {
            if (part == ServerRowPart.Name)
            { Changed(nameof(DisplayedServer)); Changed(nameof(IsSelectable)); Changed(nameof(RowCursor)); }
            Changed(PartNames[(int)part]); Changed(nameof(IsVacant));
        }
        internal bool SetInstant(ServerItem item, bool notify = true)
        {
            var old = DisplayedServer;
            bool same = old == null ? item == null : old.LooksLike(item) && ReferenceEquals(old.Service, item?.Service);
            bool complete = _parts.All(value => ReferenceEquals(value, old));
            DisplayedServer = item;
            for (int i = 0; i < _parts.Length; i++) _parts[i] = item;
            if (item != null) _selectable = item.IsSelectable;
            if (same && complete) return false;
            if (notify) NotifyAll();
            return true;
        }
        internal void NotifyAll()
        {
            foreach (var name in PartNames) Changed(name);
            Changed(nameof(DisplayedServer)); Changed(nameof(IsVacant)); Changed(nameof(IsSelectable)); Changed(nameof(RowCursor));
        }
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    internal sealed class ServerSortRow
    {
        public readonly int Index;
        public readonly ServerDisplaySlot Slot;
        public readonly ServerItem From, Target;
        internal ServerSortRow(int index, ServerDisplaySlot slot, ServerItem target)
        { Index = index; Slot = slot; From = slot.DisplayedServer; Target = target; }
    }
    internal sealed class ServerSortPass
    {
        public readonly int Generation;
        public readonly ServerSortRow[] Rows;
        public int Index => Rows[0].Index;
        public ServerDisplaySlot Slot => Rows[0].Slot;
        public ServerItem From => Rows[0].From;
        public ServerItem Target => Rows[0].Target;
        internal int RevealedMask;
        internal ServerSortPass(int generation, ServerSortRow[] rows)
        { Generation = generation; Rows = rows; }
        internal bool Contains(ServerDisplaySlot slot) => Rows.Any(row => ReferenceEquals(row.Slot, slot));
    }
    internal sealed class ServerListViewModel
    {
        // Consume the application's existing order; never rank or probe here.
        public ObservableCollection<ServerItem> Servers { get; } = new ObservableCollection<ServerItem>();
        public ObservableCollection<ServerDisplaySlot> Slots { get; } = new ObservableCollection<ServerDisplaySlot>();
        public bool IsTransitioning => _active != null || _workRequested;
        public event Action WorkAvailable;
        public event Action Resetting;
        private ServerSortPass _active;
        private bool _initialized, _workRequested;
        private int _generation;
        private HashSet<string> _completedChecks = new HashSet<string>(StringComparer.Ordinal);
        public void BeginCheckCycle()
        {
            CompleteImmediately();
            _completedChecks = new HashSet<string>(StringComparer.Ordinal);
        }
        public void MarkChecking(string identity) => _completedChecks.Remove(identity);
        private bool CanMove(ServerItem item) => !item.IsChecking && _completedChecks.Contains(item.Identity);
        private static ServerItem[] UniqueServers(IEnumerable<ServerItem> source)
            => source.GroupBy(item => item.Identity, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        public void Publish(IEnumerable<ServerItem> source, bool animate, bool allowReorder = true, string completedIdentity = null)
        {
            if (completedIdentity != null) _completedChecks.Add(completedIdentity);
            // A repeated service key represents one displayed country. Preserve
            // the first canonical occurrence before building slots or dictionaries.
            var latest = UniqueServers(source);
            var byId = latest.ToDictionary(item => item.Identity, StringComparer.Ordinal);
            bool pinChanged = Servers.FirstOrDefault(item => item.IsLastSuccessfulConnection)?.Identity
                != latest.FirstOrDefault(item => item.IsLastSuccessfulConnection)?.Identity;
            bool membershipChanged = latest.Length != Servers.Count || Servers.Any(item => !byId.ContainsKey(item.Identity));
            for (int i = 0; i < latest.Length; i++)
                if (i < Servers.Count) Servers[i] = latest[i]; else Servers.Add(latest[i]);
            while (Servers.Count > latest.Length) Servers.RemoveAt(Servers.Count - 1);
            // Membership and connection-pin changes are authoritative. Reconcile
            // immediately and cancel frozen targets for the previous pinned ID.
            if (!_initialized || membershipChanged || pinChanged || latest.Length == 0 || (!animate && allowReorder))
            { CompleteImmediately(); _initialized = true; return; }
            foreach (var slot in Slots)
            {
                if (_active?.Contains(slot) == true) continue;
                slot.SetInstant(byId[slot.DisplayedServer.Identity]);
            }
            if (allowReorder) _workRequested = true;
            if (_workRequested && _active == null) WorkAvailable?.Invoke();
        }
        public void CompleteImmediately()
        {
            ++_generation; _active = null; _workRequested = false;
            Resetting?.Invoke();
            var final = PresentationOrder();
            Resize(Servers.Count);
            var changed = new List<ServerDisplaySlot>();
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].SetInstant(final[i], notify: false)) changed.Add(Slots[i]);
            // All identities are committed before any binding reads the new state.
            foreach (var slot in changed) slot.NotifyAll();
        }
        private ServerItem[] PresentationOrder()
        {
            var latest = UniqueServers(Servers);
            var byId = latest.ToDictionary(item => item.Identity, StringComparer.Ordinal);
            var current = Slots.Count == latest.Length
                && Slots.All(slot => slot.DisplayedServer != null && byId.ContainsKey(slot.DisplayedServer.Identity))
                ? Slots.Select(slot => byId[slot.DisplayedServer.Identity]).ToArray() : latest;
            // The connection star always wins. Completed successes follow the
            // application's order; pending rows keep their relative order, and
            // completed failures follow them. Progress alone is never completion.
            var pinned = latest.FirstOrDefault(item => item.IsLastSuccessfulConnection);
            var ordered = latest.Where(item => item.Identity != pinned?.Identity && CanMove(item) && item.HasSuccessfulTest);
            var waiting = current.Where(item => item.Identity != pinned?.Identity && !CanMove(item));
            var failed = latest.Where(item => item.Identity != pinned?.Identity && CanMove(item) && !item.HasSuccessfulTest);
            return (pinned != null ? new[] { pinned } : Array.Empty<ServerItem>())
                .Concat(ordered).Concat(waiting).Concat(failed).ToArray();
        }

        public ServerSortPass TryBeginPass()
        {
            if (_active != null || !_workRequested) return null;
            var target = PresentationOrder();
            var rows = new List<ServerSortRow>();
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].DisplayedServer.Identity == target[i].Identity)
                { Slots[i].SetInstant(target[i]); continue; }
                // Freeze every changed assignment in this snapshot together.
                // No displaced country is parked in an intermediate source slot.
                rows.Add(new ServerSortRow(i, Slots[i], target[i]));
            }
            if (rows.Count == 0) { _workRequested = false; return null; }
            return _active = new ServerSortPass(_generation, rows.ToArray());
        }

        public void Reveal(ServerSortPass pass, ServerRowPart part)
        {
            if (pass == null || !ReferenceEquals(_active, pass) || pass.Generation != _generation || (pass.RevealedMask & (1 << (int)part)) != 0) return;
            pass.RevealedMask |= 1 << (int)part;
            // Commit this group across all changed slots before notifying any binding.
            // No blank source and no duplicate name/flag/selection at a frame boundary.
            foreach (var row in pass.Rows) row.Slot.AssignPart(part, row.Target);
            foreach (var row in pass.Rows) row.Slot.NotifyPart(part);
        }
        public void Finish(ServerSortPass pass)
        {
            if (pass == null || !ReferenceEquals(_active, pass) || pass.Generation != _generation) return;
            for (int i = 0; i < 5; i++) Reveal(pass, (ServerRowPart)i);
            _active = null;
            // Progress/selection publications may have skipped the frozen rows.
            // Refresh by identity without starting a new sort or changing positions.
            var byId = UniqueServers(Servers).ToDictionary(item => item.Identity, StringComparer.Ordinal);
            var changed = new List<ServerDisplaySlot>();
            foreach (var slot in Slots)
                if (slot.SetInstant(byId[slot.DisplayedServer.Identity], notify: false)) changed.Add(slot);
            foreach (var slot in changed) slot.NotifyAll();
        }
        private void Resize(int count)
        {
            while (Slots.Count < count) Slots.Add(new ServerDisplaySlot());
            while (Slots.Count > count) Slots.RemoveAt(Slots.Count - 1);
        }
    }
}


