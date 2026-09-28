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
            public int Version { get; set; } = 2;
            public bool InitialScanCompleted { get; set; }
            // A persisted permutation: consume the head, never choose a random row per tick.
            // Null also identifies old country-based caches for migration.
            public List<string> PendingRows { get; set; }
            public List<string> CompletedRows { get; set; }
            public Dictionary<string, Result> Results { get; set; } = new Dictionary<string, Result>();
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
                if (state.Version == 1 || state.PendingRows == null || state.CompletedRows == null)
                {
                    // Preserve old results, and resume only missing rows during bootstrap.
                    state.CompletedRows = state.InitialScanCompleted ? new List<string>() : rowKeys
                        .Where(p => HasRecordedResults(p.Key)).Select(p => p.Value).Distinct().ToList();
                    state.PendingRows = Shuffle(rows.Keys.Except(state.CompletedRows));
                    state.Version = 2;
                }
                else
                {
                    state.CompletedRows = state.CompletedRows.Where(rows.ContainsKey).Distinct().ToList();
                    state.PendingRows = state.PendingRows.Where(rows.ContainsKey)
                        .Except(state.CompletedRows).Distinct().ToList();
                    // Preserve an unfinished turn and the order of all surviving rows.
                    state.PendingRows.AddRange(Shuffle(rows.Keys.Except(state.PendingRows).Except(state.CompletedRows)));
                }
                if (state.PendingRows.Count == 0 && rows.Count > 0)
                {
                    state.InitialScanCompleted = true;
                    StartNextRound();
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

        private void StartNextRound()
        {
            state.CompletedRows.Clear();
            state.PendingRows = Shuffle(rows.Keys);
        }

        internal bool InitialScanCompleted { get { lock (gate) return state.InitialScanCompleted; } }

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
                var groups = (service.GetServerUrls() ?? new List<Url>())
                    .Where(u => u != null && bound.ContainsKey(u)).GroupBy(u => bound[u]);
                foreach (var group in groups)
                {
                    string key = group.Key;
                    state.Results.TryGetValue(key, out var previous);
                    long latency = group.Where(u => u.latencychkTime >= startedLocal && u.latency > 0)
                        .Select(u => u.latency).DefaultIfEmpty(-1).Min();
                    var result = new Result {
                        Latency = latency, CheckedUtc = DateTime.UtcNow,
                        LastSuccess = latency > 0 ? latency : previous?.LastSuccess ?? 0,
                        LastSuccessUtc = latency > 0 ? DateTime.UtcNow : previous?.LastSuccessUtc ?? default(DateTime)
                    };
                    state.Results[key] = result;
                    foreach (var url in group) Apply(url, result);
                }
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
                    StartNextRound();
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
                if (value == null || (value.Version != 1 && value.Version != 2) || value.Results == null || value.Results.Count > 20000) return new State();
                if (new[] { value.PendingRows, value.CompletedRows }.Any(list => list != null
                    && (list.Count > 20000 || list.Any(key => key == null || key.Length != 64)))) return new State();
                if (value.Results.Any(p => p.Key == null || p.Key.Length != 64 || p.Value == null
                    || p.Value.CheckedUtc == default(DateTime) || p.Value.CheckedUtc > DateTime.UtcNow.AddMinutes(5)
                    || p.Value.Latency < -1 || p.Value.LastSuccess < 0)) return new State();
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
