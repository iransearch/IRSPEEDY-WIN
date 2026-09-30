using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.Traffic
{
    internal sealed class TrafficSnapshot
    {
        public readonly TrafficUsage[] Rows;
        public readonly DateTime SinceUtc;
        public readonly string Status;
        public readonly bool Connected;
        public readonly long Revision;
        public TrafficSnapshot(TrafficUsage[] rows, DateTime since, string status, bool connected, long revision)
        { Rows = rows; SinceUtc = since; Status = status; Connected = connected; Revision = revision; }
    }

    internal sealed class TrafficUsageService : IDisposable
    {
        public static TrafficUsageService Instance { get; } = new TrafficUsageService();
        private readonly object gate = new object();
        private readonly TrafficLedger ledger = new TrafficLedger();
        private readonly TrafficStore store;
        private readonly int pollInterval;
        private readonly Timer timer;
        private Func<int, QueryConnectionsResponse> query;
        private bool loaded, storageBlocked;
        private string error;
        private DateTime lastSaveUtc;
        private long revision;
        private volatile TrafficSnapshot snapshot = new TrafficSnapshot(
            new TrafficUsage[0], DateTime.UtcNow, "در انتظار اتصال", false, 0);

        private TrafficUsageService() : this(new TrafficStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRSpeedyVPN", "traffic-stats-v1.json"))) { }

        internal TrafficUsageService(TrafficStore store, int pollInterval = 1000)
        {
            this.store = store;
            this.pollInterval = pollInterval;
            // No disk or network work in the constructor: the view can read Snapshot safely.
            timer = new Timer(Poll, null, Timeout.Infinite, Timeout.Infinite);
        }
        public TrafficSnapshot Snapshot => snapshot;

        // Called by the connection worker only, once a real Start succeeds. URL probes
        // never create a traffic session. Sampling continues when the view is hidden.
        public void StartSession(Func<int, QueryConnectionsResponse> read)
        {
            lock (gate)
            {
                EnsureLoaded();
                ledger.BeginSession();
                query = read;
                error = null;
                Publish();
                timer.Change(0, pollInterval);
            }
        }

        // Called before the core is stopped. No dispatcher, grpcLock or reconnect calls.
        // Bounded final sample runs on the existing disconnect/connection worker.
        public void StopSession()
        {
            lock (gate)
            {
                if (query == null) return;
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                try { ledger.Apply(query(250)); }
                catch (Exception ex) { Report("final-sample", ex); }
                query = null;
                Persist();
                Publish();
            }
        }

        private void Poll(object state)
        {
            if (!Monitor.TryEnter(gate)) return;
            try
            {
                if (query == null) return;
                try
                {
                    ledger.Apply(query(500));
                    error = null;
                }
                catch (Exception ex)
                {
                    if (error == null) Report("sample", ex);
                    error = "دریافت آمار موقتاً در دسترس نیست";
                }
                if ((DateTime.UtcNow - lastSaveUtc).TotalSeconds >= 10) Persist();
                Publish();
            }
            finally { Monitor.Exit(gate); }
        }

        public Task ResetAsync()
        {
            return Task.Run(() =>
            {
                lock (gate)
                {
                    EnsureLoaded();
                    if (storageBlocked) throw new IOException("Traffic history cannot be read safely.");
                    // Serialize reset with polling. A failed baseline query leaves totals
                    // untouched; a late pre-reset response can never refill the table.
                    var baseline = query == null ? new QueryConnectionsResponse() : query(500);
                    var now = DateTime.UtcNow;
                    store.Save(new TrafficUsage[0], now, true);
                    ledger.Reset(baseline, now);
                    lastSaveUtc = now;
                    error = null;
                    Publish();
                }
            });
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try { store.Load(ledger); }
            catch (Exception ex)
            {
                // Preserve an unreadable history instead of silently overwriting it.
                storageBlocked = true;
                Report("load", ex);
            }
        }

        private void Persist()
        {
            if (storageBlocked) return;
            try
            {
                store.Save(ledger.Snapshot(), ledger.SinceUtc);
                lastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                error = "ذخیرهٔ آمار انجام نشد؛ دوباره تلاش می‌شود";
                Report("save", ex);
            }
        }

        private void Publish()
        {
            snapshot = new TrafficSnapshot(ledger.Snapshot(), ledger.SinceUtc,
                storageBlocked ? "خواندن تاریخچهٔ آمار انجام نشد" : error ??
                (query == null ? "آمار ذخیره شده است" : "ثبت مصرف فعال است"), query != null, ++revision);
        }

        private static void Report(string stage, Exception ex) =>
            LogHelper.WriteExLog("[Traffic] stage=" + stage + " exception=" + ex.GetType().Name);

        public void Dispose()
        {
            StopSession();
            timer.Dispose();
        }
    }
}
