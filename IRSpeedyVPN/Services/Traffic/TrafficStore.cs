using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Services.Traffic
{
    internal sealed class TrafficStore
    {
        private readonly string path;
        public TrafficStore(string path) { this.path = path; }

        public void Load(TrafficLedger ledger)
        {
            if (!File.Exists(path)) return;
            try { Read(path, ledger); }
            catch
            {
                if (!File.Exists(path + ".bak")) throw;
                Read(path + ".bak", ledger);
            }
        }

        private static void Read(string file, TrafficLedger ledger)
        {
            var root = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
            if ((int?)root["version"] != 1 || !(root["apps"] is JArray apps))
                throw new InvalidDataException("Unsupported traffic history.");
            var rows = apps.Select(token => new TrafficUsage
            {
                Name = (string)token["name"], Path = (string)token["path"],
                Upload = (long?)token["up"] ?? 0, Download = (long?)token["down"] ?? 0
            }).ToArray();
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.Name) || row.Upload < 0 || row.Download < 0))
                throw new InvalidDataException("Invalid traffic history.");
            ledger.Restore(rows, (DateTime?)root["sinceUtc"] ?? DateTime.UtcNow);
        }

        public void Save(TrafficUsage[] rows, DateTime since, bool reset = false)
        {
            var root = new JObject
            {
                ["version"] = 1,
                ["sinceUtc"] = since,
                ["apps"] = new JArray(rows.Select(row => new JObject
                {
                    ["name"] = row.Name, ["path"] = row.Path,
                    ["up"] = row.Upload, ["down"] = row.Download
                }))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(root.ToString(Formatting.None));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // A reset must not resurrect pre-reset totals from a recovery backup.
                // Delete the old backup before committing; failure leaves the main file intact.
                if (reset && File.Exists(path + ".bak")) File.Delete(path + ".bak");
                if (File.Exists(path)) File.Replace(temp, path, reset ? null : path + ".bak");
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
