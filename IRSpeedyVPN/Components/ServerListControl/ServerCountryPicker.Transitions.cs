using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public partial class ServerCountryPicker
    {
        private readonly ServerListViewModel _serverList;
        private readonly ServerListSweepAnimator _sweepAnimator;
        private bool _checkCycleSetup;
        public void BeginCheckCycle()
        {
            CompleteRowTransitions();
            _checkCycleSetup = true;
        }
        public void EndCheckCycleSetup()
        {
            _checkCycleSetup = false;
            RefreshDisplayedRows();
        }
        private readonly List<GroupItem> _observedGroups = new List<GroupItem>();
        private DispatcherOperation _rowsUpdate;

        private void ObserveGroups()
        {
            foreach (var group in _observedGroups) group.PropertyChanged -= GroupVisualChanged;
            _observedGroups.Clear();
            _observedGroups.AddRange(_allGroups);
            foreach (var group in _observedGroups) group.PropertyChanged += GroupVisualChanged;
        }

        private void GroupVisualChanged(object sender, PropertyChangedEventArgs e) => QueueRowsUpdate();

        private void UpdateEmptyResultVisibility()
        {
            EmptyResults.Visibility = _groups.Count == 0 && _serverList.Slots.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void QueueRowsUpdate()
        {
            if (_rowsUpdate != null && _rowsUpdate.Status == DispatcherOperationStatus.Pending) return;
            // A probe changes several properties together; take just one coherent snapshot.
            _rowsUpdate = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RefreshDisplayedRows()));
        }

        private void RefreshDisplayedRows(bool allowReorder = false, bool instant = false)
        {
            if (ServerScroller == null || icCountries == null) return;
            if (_rowsUpdate != null && _rowsUpdate.Status == DispatcherOperationStatus.Pending) _rowsUpdate.Abort();
            _rowsUpdate = null;
            bool animate = !instant && SystemParameters.ClientAreaAnimation && IsLoaded && IsVisible
                && Window.GetWindow(this)?.WindowState != WindowState.Minimized;
            _serverList.Publish(_groups.Select(group => new ServerItem(group)), animate, allowReorder || instant);
        }

        private void CompleteRowTransitions()
        {
            // Flush pending probe/selection changes before canceling old clocks.
            RefreshDisplayedRows();
            _serverList.CompleteImmediately();
        }
    }
}
