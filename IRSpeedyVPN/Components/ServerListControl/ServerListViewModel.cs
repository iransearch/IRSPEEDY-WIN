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

    public sealed class ServerRowTransition
    {
        public ServerItem From { get; }
        public ServerItem To { get; }
        public int StaggerIndex { get; }
        public bool ChangesIdentity => From?.Identity != To?.Identity;
        internal ServerRowTransition(ServerItem from, ServerItem to, int staggerIndex)
        { From = from; To = to; StaggerIndex = staggerIndex; }
    }

    public sealed class ServerDisplaySlot : INotifyPropertyChanged
    {
        private Action _completed;
        public ServerItem DisplayedServer { get; private set; }
        public ServerRowTransition Transition { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        // The view owns WPF clocks; the VM owns the data and completion policy.
        public event Action<ServerDisplaySlot, ServerRowTransition> TransitionRequested;

        internal void Begin(ServerItem target, int staggerIndex, Action completed)
        {
            _completed = completed;
            Transition = new ServerRowTransition(DisplayedServer, target, staggerIndex);
            var handler = TransitionRequested;
            if (handler == null) Finish(Transition); // unrealized/offscreen row
            else handler(this, Transition);
        }

        public void Reveal(ServerRowTransition transition)
        {
            if (transition == null || !ReferenceEquals(Transition, transition)) return;
            DisplayedServer = transition.To;
            Changed(nameof(DisplayedServer));
        }

        public void Finish(ServerRowTransition transition)
        {
            if (transition == null || !ReferenceEquals(Transition, transition)) return;
            Reveal(transition);
            Transition = null;
            var completed = _completed;
            _completed = null;
            Changed(nameof(Transition));
            completed?.Invoke();
        }

        internal void SetInstant(ServerItem target)
        {
            _completed = null;
            Transition = null;
            DisplayedServer = target;
            Changed(nameof(Transition));
            Changed(nameof(DisplayedServer));
        }
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal sealed class ServerListViewModel
    {
        // Always contains the latest sorted truth, even while Slots show old data.
        public ObservableCollection<ServerItem> Servers { get; } = new ObservableCollection<ServerItem>();
        public ObservableCollection<ServerDisplaySlot> Slots { get; } = new ObservableCollection<ServerDisplaySlot>();
        public bool IsTransitioning { get; private set; }
        private bool _initialized, _pending;
        private int _generation, _remaining, _firstVisibleIndex;

        public void Publish(IEnumerable<ServerItem> servers, bool animate, int firstVisibleIndex)
        {
            var latest = ServerListRanking.Order(servers, item => item.IsLastSuccessfulConnection,
                item => item.SortKey, item => item.CountryName, item => item.Service?.ID ?? int.MaxValue).ToArray();
            for (int i = 0; i < latest.Length; i++)
            {
                if (i < Servers.Count) Servers[i] = latest[i];
                else Servers.Add(latest[i]);
            }
            while (Servers.Count > latest.Length) Servers.RemoveAt(Servers.Count - 1);
            _firstVisibleIndex = firstVisibleIndex;
            _pending = true;
            if (!animate || !_initialized) CompleteImmediately();
            else if (!IsTransitioning) StartLatestWave();
            _initialized = true;
        }

        public void CompleteImmediately()
        {
            ++_generation; // invalidates callbacks from clocks that were canceled
            IsTransitioning = false;
            _pending = false;
            Resize(Servers.Count);
            for (int i = 0; i < Slots.Count; i++) Slots[i].SetInstant(Servers[i]);
        }

        private void Resize(int count)
        {
            while (Slots.Count < count) Slots.Add(new ServerDisplaySlot());
            while (Slots.Count > count) Slots.RemoveAt(Slots.Count - 1);
        }

        private void StartLatestWave()
        {
            _pending = false;
            int generation = ++_generation;
            var targets = Servers.ToArray();
            Resize(Math.Max(Slots.Count, targets.Length));
            var changed = new List<int>();
            for (int i = 0; i < Slots.Count; i++)
            {
                var target = i < targets.Length ? targets[i] : null;
                var old = Slots[i].DisplayedServer;
                if (old == null ? target != null : !old.LooksLike(target)) changed.Add(i);
                else Slots[i].SetInstant(target); // also refresh the live service reference
            }
            IsTransitioning = changed.Count > 0;
            _remaining = changed.Count;
            if (!IsTransitioning) { Resize(targets.Length); return; }
            foreach (int index in changed)
            {
                var target = index < targets.Length ? targets[index] : null;
                Slots[index].Begin(target, Math.Max(0, index - _firstVisibleIndex), () =>
                {
                    if (generation != _generation || --_remaining != 0) return;
                    IsTransitioning = false;
                    if (_pending) StartLatestWave(); // one latest snapshot, never a queue of stale sorts
                    else Resize(Servers.Count);
                });
            }
        }
    }
}
