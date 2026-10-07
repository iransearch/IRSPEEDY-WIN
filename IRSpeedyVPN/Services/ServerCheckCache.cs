using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IRSpeedyVPN.Services
{
    // Account-scoped results, never raw subscription links or credentials on disk.
    internal sealed class ServerCheckCache
    {
        private readonly object gate = new object();
        private readonly string path;
        private State state;
        private Dictionary<Url, string> bound = new Dictionary<Url, string>();
        private readonly Random random;
        private Dictionary<IVPNService, string> rowKeys = new Dictionary<IVPNService, string>();
        private Dictionary<string, IVPNService> rows = new Dictionary<string, IVPNService>();

        internal sealed class Result
        {
            public long Latency { get; set; }
            public DateTime CheckedUtc { get; set; }
            public long LastSuccess { get; set; }
            public DateTime LastSuccessUtc { get; set; }
        }
        internal sealed class State
        {
            public int Version { get; set; } = 4;
            public bool InitialScanCompleted { get; set; }
            public bool RoundInProgress { get; set; } = true;
            public bool SequentialRound { get; set; }
            // Bootstrap retains its shuffled order. Disconnect rounds use stable
            // country/ID order within unsuccessful and successful row groups.
            public List<string> PendingRows { get; set; }
            public List<string> CompletedRows { get; set; }
            public Dictionary<string, Result> Results { get; set; } = new Dictionary<string, Result>();
            public Dictionary<string, RowResult> RowResults { get; set; } = new Dictionary<string, RowResult>();
        }
        internal sealed class RowResult
        {
            public bool HasSuccess { get; set; }
            public DateTime CheckedUtc { get; set; }
        }

        internal ServerCheckCache(string account, string scope, string directory = null, Random random = null)
        {
            directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRSpeedy", "ServerChecks");
            path = Path.Combine(directory, Hash(account + "\n" + scope) + ".json");
            this.random = random ?? new Random();
            state = Load();
        }

        internal static string CountryKey(IVPNService service)
        {
            return string.IsNullOrWhiteSpace(service.CountryCode)
                ? service.Country ?? service.ID.ToString()
                : service.CountryCode.Trim().ToUpperInvariant();
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        private static string Key(IVPNService service, Url url)
        {
            // ID identifies the row, the complete config fingerprint invalidates changed links.
            return Hash(JsonConvert.SerializeObject(new object[] { service.ID, service.Name, CountryKey(service),
                url.url, url.extra_field_1, url.extra_field_2, url.chainproxy, url.irancell, url.is_subscription }));
        }

        internal void Bind(IEnumerable<IVPNService> services)
        {
            lock (gate)
            {
                var items = services.Where(s => s.IsUrlTestSupported).ToArray();
                bound = new Dictionary<Url, string>();
                foreach (var service in items)
                    foreach (var url in service.GetServerUrls() ?? new List<Url>())
                    {
                        if (url == null || string.IsNullOrWhiteSpace(url.url)) continue;
                        string key = Key(service, url);
                        bound[url] = key;
                        if (state.Results.TryGetValue(key, out var result)) Apply(url, result);
                        else
                        {
                            // An API refresh may copy a result by URL alone. A config change must invalidate it.
                            url.latency = 0;
                            url.latencychkTime = default(DateTime);
                            url.LastSuccessfulLatency = 0;
                            url.LastSuccessfulCheckTime = default(DateTime);
                        }
                    }
                var live = new HashSet<string>(bound.Values);
                foreach (string key in state.Results.Keys.Where(k => !live.Contains(k)).ToArray()) state.Results.Remove(key);
                rowKeys = new Dictionary<IVPNService, string>();
                rows = new Dictionary<string, IVPNService>();
                foreach (var service in items)
                {
                    // Include the configuration identity, so changed links get a new turn.
                    // Sorting fingerprints makes an API-only URL reorder harmless.
                    var configs = (service.GetServerUrls() ?? new List<Url>())
                        .Where(u => u != null && bound.ContainsKey(u)).Select(u => bound[u])
                        .Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
                    string row = Hash(JsonConvert.SerializeObject(new object[] { service.ID, service.Name, configs }));
                    rowKeys[service] = row;
                    rows[row] = service;
                }
                if (state.Version == 3)
                {
                    // Version 3 committed all member results only after a complete
                    // test. Preserve that history even when CompletedRows was cleared.
                    foreach (var row in rowKeys.Where(p => HasRecordedResults(p.Key)))
                    {
                        var results = row.Key.GetServerUrls().Where(u => u != null && bound.ContainsKey(u))
                            .Select(u => state.Results[bound[u]]).ToArray();
                        state.RowResults[row.Value] = new RowResult {
                            HasSuccess = results.Any(r => r.Latency > 0),
                            CheckedUtc = results.Max(r => r.CheckedUtc)
                        };
                    }
                }
                foreach (string key in state.RowResults.Keys.Where(k => !rows.ContainsKey(k)).ToArray())
                    state.RowResults.Remove(key);
                if (state.Version < 3)
                {
                    // Version 2 pre-created an endless next round. Do not run that
                    // periodic queue after upgrading a completed initial scan.
                    state.RoundInProgress = !state.InitialScanCompleted;
                    state.SequentialRound = false;
                }
                if (!state.RoundInProgress)
                {
                    state.PendingRows = new List<string>();
                    state.CompletedRows = new List<string>();
                }
                else if (state.Version == 1 || state.PendingRows == null || state.CompletedRows == null)
                {
                    // Preserve old results, and resume only missing rows during bootstrap.
                    state.CompletedRows = state.InitialScanCompleted ? new List<string>() : rowKeys
                        .Where(p => HasRecordedResults(p.Key)).Select(p => p.Value).Distinct().ToList();
                    state.PendingRows = Shuffle(rows.Keys.Except(state.CompletedRows));
                }
                else
                {
                    state.CompletedRows = state.CompletedRows.Where(rows.ContainsKey).Distinct().ToList();
                    state.PendingRows = state.PendingRows.Where(rows.ContainsKey)
                        .Except(state.CompletedRows).Distinct().ToList();
                    // Preserve an unfinished turn and the order of all surviving rows.
                    var added = rows.Keys.Except(state.PendingRows).Except(state.CompletedRows);
                    state.PendingRows.AddRange(state.SequentialRound ? OrderPriorityRows(added) : Shuffle(added));
                }
                state.Version = 4;
                if (state.RoundInProgress && state.PendingRows.Count == 0 && rows.Count > 0)
                {
                    state.InitialScanCompleted = true;
                    state.RoundInProgress = false;
                }
            }
        }

        private bool HasRecordedResults(IVPNService service)
        {
            var urls = (service.GetServerUrls() ?? new List<Url>())
                .Where(u => u != null && !string.IsNullOrWhiteSpace(u.url)).ToArray();
            return urls.Length > 0 && urls.All(u => bound.TryGetValue(u, out string key) && state.Results.ContainsKey(key));
        }

        private List<string> Shuffle(IEnumerable<string> keys)
        {
            var result = keys.ToList();
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                string swap = result[i]; result[i] = result[j]; result[j] = swap;
            }
            return result;
        }

        private List<string> OrderRows(IEnumerable<string> keys)
        {
            return keys.OrderBy(key => rows[key].Country, StringComparer.CurrentCulture)
                .ThenBy(key => CountryKey(rows[key]), StringComparer.Ordinal)
                .ThenBy(key => rows[key].ID).ToList();
        }

        private List<string> OrderPriorityRows(IEnumerable<string> keys)
        {
            // LINQ OrderBy is stable: retain country/ID order within both groups.
            // Historical success and in-flight URL values cannot change this queue.
            return OrderRows(keys).OrderBy(key =>
                state.RowResults.TryGetValue(key, out var result) && result.HasSuccess ? 1 : 0).ToList();
        }

        internal void RestartForLogin()
        {
            lock (gate)
            {
                // A real login/app start always gets one fresh bootstrap round.
                // Keep historical URL/row results for display and later disconnect
                // priority; only rebuild the work queue in bootstrap (shuffled) order.
                state.InitialScanCompleted = false;
                state.RoundInProgress = true;
                state.SequentialRound = false;
                state.CompletedRows = new List<string>();
                state.PendingRows = Shuffle(rows.Keys);
                Save();
            }
        }

        internal bool ClearForManualRefresh(IVPNService[] expected = null)
        {
            lock (gate)
            {
                var expectedRows = expected?.Where(s => s.IsUrlTestSupported).ToArray();
                if (expectedRows != null && (expectedRows.Length != rowKeys.Count || expectedRows.Any(s => !rowKeys.ContainsKey(s)))) return false;
                state.Results.Clear(); state.RowResults.Clear();
                state.PendingRows = new List<string>(); state.CompletedRows = new List<string>();
                state.InitialScanCompleted = false; state.RoundInProgress = false; state.SequentialRound = false;
                foreach (var url in bound.Keys)
                {
                    url.latency = 0; url.latencychkTime = default(DateTime);
                    url.LastSuccessfulLatency = 0; url.LastSuccessfulCheckTime = default(DateTime);
                }
                Save();
                return true;
            }
        }

        internal void RecordRefreshMember(IVPNService service, Url url, long latency, DateTime checkedUtc)
        {
            lock (gate)
            {
                string key;
                if (!rowKeys.ContainsKey(service) || !bound.TryGetValue(url, out key)) return;
                state.Results.TryGetValue(key, out var previous);
                var result = new Result { Latency = latency > 0 ? latency : -1, CheckedUtc = checkedUtc,
                    LastSuccess = latency > 0 ? latency : previous?.LastSuccess ?? 0,
                    LastSuccessUtc = latency > 0 ? checkedUtc : previous?.LastSuccessUtc ?? default(DateTime) };
                state.Results[key] = result;
                foreach (var item in bound.Where(p => p.Value == key)) Apply(item.Key, result);
            }
        }

        internal void CompleteRefreshRow(IVPNService service)
        {
            lock (gate)
            {
                string row;
                if (!rowKeys.TryGetValue(service, out row)) return;
                state.RowResults[row] = new RowResult { HasSuccess = (service.GetServerUrls() ?? new List<Url>()).Any(u => u != null && u.latency > 0),
                    CheckedUtc = DateTime.UtcNow };
                if (!state.CompletedRows.Contains(row)) state.CompletedRows.Add(row);
                Save();
            }
        }

        internal void FinishManualRefresh(IVPNService[] expected = null)
        {
            lock (gate)
            {
                var expectedRows = expected?.Where(s => s.IsUrlTestSupported).ToArray();
                if (expectedRows != null && (expectedRows.Length != rowKeys.Count || expectedRows.Any(s => !rowKeys.ContainsKey(s)))) return;
                state.InitialScanCompleted = true;
                Save();
            }
        }

        internal void RestartFromFirstCountry()
        {
            lock (gate)
            {
                state.RoundInProgress = true;
                state.SequentialRound = true;
                state.CompletedRows = new List<string>();
                state.PendingRows = OrderPriorityRows(rows.Keys);
            }
        }

        // A connection consumes the current scan opportunity. Persist this only
        // after its worker drains, so login/reload cannot revive an aborted round.
        internal void StopRound()
        {
            lock (gate)
            {
                state.RoundInProgress = false;
                state.PendingRows = new List<string>();
                state.CompletedRows = new List<string>();
                Save();
            }
        }

        internal bool InitialScanCompleted { get { lock (gate) return state.InitialScanCompleted; } }
        internal bool ShowInitialProgress { get { lock (gate) return !state.InitialScanCompleted && !state.SequentialRound; } }
        internal bool PrioritizeFailedServers { get { lock (gate) return state.SequentialRound; } }

        internal IVPNService NextService
        {
            get
            {
                lock (gate)
                    return state.PendingRows != null && state.PendingRows.Count > 0
                        && rows.TryGetValue(state.PendingRows[0], out var service) ? service : null;
            }
        }

        // Called on the worker before testing; no new disk write on the UI thread.
        internal void PersistQueue() { lock (gate) Save(); }

        // Called only after a completed, non-canceled service test, on the probe worker.
        internal void Record(IVPNService service, DateTime startedLocal)
        {
            lock (gate)
            {
                // A replaced API object cannot overwrite the replacement row's history.
                if (!rowKeys.TryGetValue(service, out string row)) return;
                bool hasSuccess = false;
                DateTime checkedUtc = DateTime.UtcNow;
                var groups = (service.GetServerUrls() ?? new List<Url>())
                    .Where(u => u != null && bound.ContainsKey(u)).GroupBy(u => bound[u]).ToArray();
                bool fullyChecked = groups.Length > 0 && groups.All(group =>
                    group.Any(u => u.latencychkTime >= startedLocal));
                foreach (var group in groups)
                {
                    string key = group.Key;
                    state.Results.TryGetValue(key, out var previous);
                    long latency = group.Where(u => u.latencychkTime >= startedLocal && u.latency > 0)
                        .Select(u => u.latency).DefaultIfEmpty(-1).Min();
                    hasSuccess |= latency > 0;
                    var result = new Result {
                        Latency = latency, CheckedUtc = checkedUtc,
                        LastSuccess = latency > 0 ? latency : previous?.LastSuccess ?? 0,
                        LastSuccessUtc = latency > 0 ? checkedUtc : previous?.LastSuccessUtc ?? default(DateTime)
                    };
                    state.Results[key] = result;
                    foreach (var url in group) Apply(url, result);
                }
                // The service can return after a Core/RPC error without a full
                // response. Only a fresh result for every member replaces the
                // last complete row outcome used for disconnect priority.
                if (fullyChecked)
                    state.RowResults[row] = new RowResult { HasSuccess = hasSuccess, CheckedUtc = checkedUtc };
                Save();
            }
        }

        internal void Restore(IVPNService service)
        {
            lock (gate)
                foreach (var url in service.GetServerUrls() ?? new List<Url>())
                    if (url != null && bound.TryGetValue(url, out string key))
                    {
                        if (state.Results.TryGetValue(key, out var result)) Apply(url, result);
                        else { url.latency = 0; url.latencychkTime = default(DateTime); }
                    }
        }

        internal void CompleteService(IVPNService service)
        {
            lock (gate)
            {
                // An obsolete API row or canceled test cannot consume a replacement row.
                if (!rowKeys.TryGetValue(service, out string row) || state.PendingRows.Count == 0
                    || state.PendingRows[0] != row) return;
                state.PendingRows.RemoveAt(0);
                state.CompletedRows.Add(row);
                if (state.PendingRows.Count == 0)
                {
                    state.InitialScanCompleted = true;
                    state.RoundInProgress = false;
                }
                Save();
            }
        }

        private static void Apply(Url url, Result result)
        {
            url.latency = result.Latency;
            url.latencychkTime = result.CheckedUtc.ToLocalTime();
            url.LastSuccessfulLatency = result.LastSuccess;
            url.LastSuccessfulCheckTime = result.LastSuccessUtc == default(DateTime)
                ? default(DateTime) : result.LastSuccessUtc.ToLocalTime();
        }

        private State Load()
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return new State();
                var value = JsonConvert.DeserializeObject<State>(File.ReadAllText(path), new JsonSerializerSettings { MaxDepth = 8 });
                if (value == null || (value.Version < 1 || value.Version > 4) || value.Results == null || value.Results.Count > 20000
                    || value.RowResults == null || value.RowResults.Count > 20000) return new State();
                if (new[] { value.PendingRows, value.CompletedRows }.Any(list => list != null
                    && (list.Count > 20000 || list.Any(key => key == null || key.Length != 64)))) return new State();
                if (value.Results.Any(p => p.Key == null || p.Key.Length != 64 || p.Value == null
                    || p.Value.CheckedUtc == default(DateTime) || p.Value.CheckedUtc > DateTime.UtcNow.AddMinutes(5)
                    || p.Value.Latency < -1 || p.Value.LastSuccess < 0)) return new State();
                if (value.RowResults.Any(p => p.Key == null || p.Key.Length != 64 || p.Value == null
                    || p.Value.CheckedUtc == default(DateTime) || p.Value.CheckedUtc > DateTime.UtcNow.AddMinutes(5))) return new State();
                return value;
            }
            catch { return new State(); } // A corrupt cache must not prevent login or tests.
        }

        private void Save()
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, JsonConvert.SerializeObject(state), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch { /* Keep usable in-memory results if storage is unavailable. */ }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }
}
