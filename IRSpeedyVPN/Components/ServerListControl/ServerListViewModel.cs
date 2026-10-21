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
            Identity = LastSuccessfulServer.Key(group.Service);
            Service = group.Service;
            CountryName = group.CountryName;
            SortKey = group.SortKey;
            FlagSource = group.FlagSource;
            Initials = group.Initials;
            SignalText = group.SignalText;
            SignalToolTip = group.SignalToolTip;
            // Keep the live theme brush so a theme change still reaches cached rows.
            SignalBrush = group.SignalBrush;
            IsSelectable = group.IsSelectable;
            IsChecking = group.IsChecking;
            IsSelectedCountry = group.IsSelectedCountry;
            IsLastSuccessfulConnection = group.IsLastSuccessfulConnection;
        }

        internal bool LooksLike(ServerItem other)
        {
            return other != null && Identity == other.Identity && CountryName == other.CountryName
                && ReferenceEquals(FlagSource, other.FlagSource) && Initials == other.Initials
                && SignalText == other.SignalText && SignalToolTip == other.SignalToolTip
                && ReferenceEquals(SignalBrush, other.SignalBrush) && IsSelectable == other.IsSelectable
                && IsChecking == other.IsChecking && IsSelectedCountry == other.IsSelectedCountry
                && IsLastSuccessfulConnection == other.IsLastSuccessfulConnection;
        }
    }

    public enum ServerRowPart { None = -1, Signal, Badge, Name, Flag, Selection }
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
        // Vacancies retain the card's dimensions/background/disabled appearance.
        public bool IsSelectable => _selectable;
        public Cursor RowCursor => IsVacant ? Cursors.Arrow : DisplayedServer?.RowCursor ?? Cursors.Arrow;
        public event PropertyChangedEventHandler PropertyChanged;
        private static readonly string[] PartNames = { nameof(SignalServer), nameof(BadgeServer), nameof(NameServer), nameof(FlagServer), nameof(SelectionServer) };
        internal ServerItem GetPart(ServerRowPart part) => _parts[(int)part];
        internal void Reveal(ServerRowPart part, ServerItem item)
        {
            _parts[(int)part] = item;
            if (part == ServerRowPart.Name)
            {
                DisplayedServer = item;
                if (item != null) _selectable = item.IsSelectable;
                Changed(nameof(DisplayedServer)); Changed(nameof(IsSelectable)); Changed(nameof(RowCursor));
            }
            Changed(PartNames[(int)part]); Changed(nameof(IsVacant));
        }
        internal void SetInstant(ServerItem item)
        {
            var old = DisplayedServer;
            bool same = old == null ? item == null : old.LooksLike(item) && ReferenceEquals(old.Service, item?.Service);
            bool complete = _parts.All(value => ReferenceEquals(value, old));
            DisplayedServer = item;
            for (int i = 0; i < _parts.Length; i++) _parts[i] = item;
            if (item != null) _selectable = item.IsSelectable;
            if (same && complete) return;
            foreach (var name in PartNames) Changed(name);
            Changed(nameof(DisplayedServer)); Changed(nameof(IsVacant)); Changed(nameof(IsSelectable)); Changed(nameof(RowCursor));
        }
        internal bool Contains(string identity) => identity != null && _parts.Any(item => item?.Identity == identity);
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    internal sealed class ServerSortPass
    {
        public readonly int Index, Generation;
        public readonly ServerDisplaySlot Slot;
        public readonly ServerItem From, Target;
        internal bool Reserved;
        internal int RevealedMask;
        internal ServerSortPass(int index, int generation, ServerDisplaySlot slot, ServerItem target)
        { Index = index; Generation = generation; Slot = slot; From = slot.DisplayedServer; Target = target; }
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
        public void Publish(IEnumerable<ServerItem> source, bool animate, bool allowReorder = true)
        {
            var latest = source.ToArray();
            var byId = latest.ToDictionary(item => item.Identity);
            for (int i = 0; i < latest.Length; i++)
                if (i < Servers.Count) Servers[i] = latest[i]; else Servers.Add(latest[i]);
            while (Servers.Count > latest.Length) Servers.RemoveAt(Servers.Count - 1);
            if (!_initialized || latest.Length == 0 || (!animate && allowReorder)) { CompleteImmediately(); _initialized = true; return; }
            foreach (var slot in Slots)
            {
                if (ReferenceEquals(slot, _active?.Slot) || slot.IsVacant) continue;
                ServerItem current;
                slot.SetInstant(byId.TryGetValue(slot.DisplayedServer.Identity, out current) ? current : null);
            }
            if (allowReorder) _workRequested = true;
            if (_workRequested && _active == null) WorkAvailable?.Invoke();
        }
        public void CompleteImmediately()
        {
            ++_generation; _active = null; _workRequested = false;
            Resetting?.Invoke(); Resize(Servers.Count);
            for (int i = 0; i < Slots.Count; i++) Slots[i].SetInstant(Servers[i]);
        }
        public ServerSortPass TryBeginPass()
        {
            if (_active != null || !_workRequested) return null;
            Resize(Math.Max(Slots.Count, Servers.Count));
            // Re-plan EVERY boundary, including vacant sources, against latest truth.
            for (int i = 0; i < Slots.Count; i++)
            {
                var target = i < Servers.Count ? Servers[i] : null;
                if (Slots[i].DisplayedServer?.Identity == target?.Identity)
                { Slots[i].SetInstant(target); continue; }
                return _active = new ServerSortPass(i, _generation, Slots[i], target);
            }
            _workRequested = false; Resize(Servers.Count); return null;
        }
        public void Reveal(ServerSortPass pass, ServerRowPart part)
        {
            if (!ReferenceEquals(_active, pass) || pass.Generation != _generation || (pass.RevealedMask & (1 << (int)part)) != 0) return;
            pass.RevealedMask |= 1 << (int)part;
            if (!pass.Reserved)
            {
                // Conceal the source BEFORE any target group is revealed. Progress
                // publications cannot refill it with an obsolete identity.
                foreach (var slot in Slots)
                    if (!ReferenceEquals(slot, pass.Slot) && slot.Contains(pass.Target?.Identity)) slot.SetInstant(null);
                pass.Reserved = true;
            }
            pass.Slot.Reveal(part, pass.Target);
        }
        public void Finish(ServerSortPass pass)
        {
            if (!ReferenceEquals(_active, pass) || pass.Generation != _generation) return;
            for (int i = 0; i < 5; i++) Reveal(pass, (ServerRowPart)i);
            _active = null;
        }
        private void Resize(int count)
        {
            while (Slots.Count < count) Slots.Add(new ServerDisplaySlot());
            while (Slots.Count > count) Slots.RemoveAt(Slots.Count - 1);
        }
    }
}
