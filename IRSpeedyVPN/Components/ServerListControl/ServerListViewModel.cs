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
        public bool ChangesIdentity => From?.Identity != To?.Identity;
        internal ServerRowTransition(ServerItem from, ServerItem to)
        { From = from; To = to; }
    }

    public sealed class ServerDisplaySlot : INotifyPropertyChanged
    {
        private Action _completed;
        public ServerItem DisplayedServer { get; private set; }
        public ServerRowTransition Transition { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        // The view owns WPF clocks; the VM owns the data and completion policy.
        public event Action<ServerDisplaySlot, ServerRowTransition> TransitionRequested;

        internal void Begin(ServerItem target, Action completed)
        {
            _completed = completed;
            Transition = new ServerRowTransition(DisplayedServer, target);
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
        private bool _initialized, _pending, _advancing, _advanceScheduled;
        private int _generation, _nextIndex, _scheduleTicket;
        private ServerDisplaySlot _activeSlot;
        private readonly Action<Action> _schedulePresentation;

        public ServerListViewModel(Action<Action> schedulePresentation)
        {
            _schedulePresentation = schedulePresentation ?? throw new ArgumentNullException(nameof(schedulePresentation));
        }

        public void Publish(IEnumerable<ServerItem> servers, bool animate)
        {
            var latest = ServerListRanking.Order(servers, item => item.IsLastSuccessfulConnection,
                item => item.SortKey, item => item.CountryName, item => item.Service?.ID ?? int.MaxValue).ToArray();
            for (int i = 0; i < latest.Length; i++)
            {
                if (i < Servers.Count) Servers[i] = latest[i];
                else Servers.Add(latest[i]);
            }
            while (Servers.Count > latest.Length) Servers.RemoveAt(Servers.Count - 1);
            _pending = true;
            if (!animate || !_initialized) CompleteImmediately();
            else if (!IsTransitioning)
            {
                ++_generation;
                _nextIndex = 0;
                _pending = false;
                IsTransitioning = true;
                ScheduleAdvance();
            }
            _initialized = true;
        }

        public void CompleteImmediately()
        {
            ++_generation; // invalidates callbacks from clocks that were canceled
            ++_scheduleTicket;
            _advanceScheduled = false;
            IsTransitioning = false;
            _pending = false;
            _activeSlot = null;
            _nextIndex = 0;
            Resize(Servers.Count);
            for (int i = 0; i < Slots.Count; i++) Slots[i].SetInstant(Servers[i]);
        }

        private void Resize(int count)
        {
            while (Slots.Count < count) Slots.Add(new ServerDisplaySlot());
            while (Slots.Count > count) Slots.RemoveAt(Slots.Count - 1);
        }

        private void ScheduleAdvance()
        {
            if (_advancing || _advanceScheduled || !IsTransitioning || _activeSlot != null) return;
            _advanceScheduled = true;
            int ticket = ++_scheduleTicket;
            _schedulePresentation(() =>
            {
                if (ticket != _scheduleTicket) return;
                _advanceScheduled = false;
                AdvanceRows();
            });
        }

        private void AdvanceRows()
        {
            // Invisible/unrealized rows may acknowledge synchronously. Drain them
            // iteratively rather than recursing through thousands of callbacks.
            if (_advancing) return;
            _advancing = true;
            try
            {
                while (IsTransitioning && _activeSlot == null)
                {
                    Resize(Math.Max(Slots.Count, Servers.Count));
                    if (_nextIndex >= Slots.Count)
                    {
                        if (_pending)
                        {
                            // Revisit earlier slots changed by newer results; never
                            // replay a queue of obsolete list snapshots.
                            _pending = false;
                            _nextIndex = 0;
                            continue;
                        }
                        IsTransitioning = false;
                        Resize(Servers.Count);
                        break;
                    }

                    int index = _nextIndex++;
                    var slot = Slots[index];
                    // Waiting rows read the latest data only when their turn starts.
                    var target = index < Servers.Count ? Servers[index] : null;
                    var old = slot.DisplayedServer;
                    if (old == null ? target == null : old.LooksLike(target))
                    {
                        slot.SetInstant(target);
                        continue;
                    }

                    int generation = _generation;
                    _activeSlot = slot;
                    slot.Begin(target, () =>
                    {
                        if (generation != _generation || !ReferenceEquals(_activeSlot, slot)) return;
                        _activeSlot = null;
                        // Finish() has already removed the old row's clock/overlay.
                        // Only now can the next row start its own reveal.
                        ScheduleAdvance();
                    });
                }
            }
            finally { _advancing = false; }
        }
    }
}
