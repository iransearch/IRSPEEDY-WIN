using System;
using System.Globalization;
using System.IO;

namespace IRSpeedyVPN.Common
{
    internal sealed class DailyLogStore
    {
        private readonly string directory;
        private readonly object gate = new object();
        private DateTime lastCleanup = DateTime.MinValue;

        internal DailyLogStore(string directory) { this.directory = directory; }

        internal static string ResolveDirectory()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
                throw new IOException("Local application data directory is unavailable.");
            return Path.Combine(local, "IRSpeedyVPN", "Logs");
        }

        internal void Append(DateTime timestamp, string line)
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                if (lastCleanup != timestamp.Date)
                {
                    Cleanup(timestamp.Date);
                    lastCleanup = timestamp.Date;
                }
                File.AppendAllText(Path.Combine(directory, "log-"
                    + timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt"), line);
            }
        }

        private void Cleanup(DateTime today)
        {
            // Keep seven calendar dates, including today. Only our daily files
            // are eligible for deletion; unrelated files in this folder stay intact.
            foreach (var file in Directory.EnumerateFiles(directory, "log-*.txt"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                DateTime date;
                if (!name.StartsWith("log-", StringComparison.Ordinal)
                    || !DateTime.TryParseExact(name.Substring(4), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    || date >= today.AddDays(-6)) continue;
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
