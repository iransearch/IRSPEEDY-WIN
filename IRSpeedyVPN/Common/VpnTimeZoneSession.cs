using System;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal sealed class TimeZoneLocation
    {
        internal string IanaId { get; set; }
        internal string WindowsId { get; set; }
    }

    internal sealed class TimeZoneSnapshot
    {
        public string WindowsId { get; set; }
        public bool DaylightSavingDisabled { get; set; }
        public string SerializedLocalZone { get; set; }
    }

    internal interface ITimeZoneSystem
    {
        TimeZoneSnapshot Capture();
        void Apply(string windowsId);
        void Restore(TimeZoneSnapshot snapshot);
    }

    internal interface ITimeZoneJournal
    {
        TimeZoneSnapshot Read();
        void Write(TimeZoneSnapshot snapshot);
        void Clear();
    }

    internal interface ITimeZoneLookup
    {
        Task<TimeZoneLocation> ResolveAsync(int proxyPort, CancellationToken token);
    }

    // The network request never holds the gate. Disconnect invalidates it and
    // restores the saved Windows setting without waiting for network/Core teardown.
    internal sealed class VpnTimeZoneSession
    {
        private readonly object gate = new object();
        private readonly ITimeZoneSystem system;
        private readonly ITimeZoneJournal journal;
        private readonly ITimeZoneLookup lookup;
        private object connection;
        private long generation;
        private CancellationTokenSource pending;
        private TimeZoneSnapshot original;
        private TimeZoneInfo homeZone;
        private TimeZoneLocation active;

        internal VpnTimeZoneSession(ITimeZoneSystem system, ITimeZoneJournal journal, ITimeZoneLookup lookup)
        { this.system = system; this.journal = journal; this.lookup = lookup; }

        internal object Connection { get { lock (gate) return connection; } }
        internal TimeZoneLocation Active { get { lock (gate) return active; } }
        internal DateTime AccountNow
        {
            get
            {
                lock (gate)
                    return homeZone == null ? DateTime.Now : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, homeZone);
            }
        }

        internal void Recover()
        {
            lock (gate)
            {
                original = original ?? journal.Read();
                if (original != null)
                {
                    homeZone = TimeZoneInfo.FromSerializedString(original.SerializedLocalZone);
                    RestoreLocked();
                }
            }
        }

        internal void BeginConnection(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (gate)
            {
                // Automatic recovery of the same VPN does not change its chosen zone.
                if (ReferenceEquals(connection, owner)) return;
                if (connection != null) throw new InvalidOperationException("Previous time-zone session is still connected.");
                Recover();
                connection = owner;
                generation++;
            }
        }

        internal async Task<TimeZoneLocation> EnableAsync(object owner, int proxyPort)
        {
            CancellationTokenSource request;
            long version;
            lock (gate)
            {
                if (owner == null || !ReferenceEquals(owner, connection)) throw new OperationCanceledException();
                if (active != null) return active;
                if (pending != null) throw new InvalidOperationException("Time-zone lookup is already running.");
                request = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                pending = request;
                version = generation;
            }
            try
            {
                var location = await lookup.ResolveAsync(proxyPort, request.Token).ConfigureAwait(false);
                lock (gate)
                {
                    request.Token.ThrowIfCancellationRequested();
                    if (generation != version || !ReferenceEquals(owner, connection)) throw new OperationCanceledException();
                    var snapshot = system.Capture();
                    var baseline = TimeZoneInfo.FromSerializedString(snapshot.SerializedLocalZone);
                    // Persist before the first system mutation, including a crash in Apply.
                    journal.Write(snapshot);
                    original = snapshot;
                    homeZone = baseline;
                    try
                    {
                        system.Apply(location.WindowsId);
                        active = location;
                        return location;
                    }
                    catch
                    {
                        RestoreLocked();
                        throw;
                    }
                }
            }
            finally
            {
                lock (gate) if (pending == request) pending = null;
                request.Dispose();
            }
        }

        internal void Disable()
        {
            CancellationTokenSource request;
            lock (gate)
            {
                generation++;
                request = pending;
                pending = null;
                try { RestoreLocked(); }
                finally { Cancel(request); }
            }
        }

        internal void EndConnection(object expectedOwner = null)
        {
            CancellationTokenSource request;
            lock (gate)
            {
                if (expectedOwner != null && !ReferenceEquals(connection, expectedOwner)) return;
                connection = null;
                generation++;
                request = pending;
                pending = null;
                try { RestoreLocked(); }
                finally { Cancel(request); }
            }
        }

        private void RestoreLocked()
        {
            if (original == null) return;
            system.Restore(original);
            journal.Clear();
            original = null;
            homeZone = null;
            active = null;
        }

        private static void Cancel(CancellationTokenSource request)
        {
            if (request == null) return;
            // Cancellation callbacks must not delay restoring the Windows setting.
            Task.Run(() => { try { request.Cancel(); } catch (ObjectDisposedException) { } });
        }
    }
}
