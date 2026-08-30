using System;
using System.IO;

namespace IRSpeedyVPN.Common
{
    public static class LogHelper
    {
        private static readonly string LogPath = ResolveLogPath();

        /// <summary>
        /// The log used to be written to the relative path ".\log.txt", which follows the
        /// working directory rather than the app. A shortcut with a different "Start in",
        /// or the updater launching the app from elsewhere, sent crash reports somewhere
        /// nobody looks - or failed outright when that directory was not writable. Resolve
        /// the app's own folder first and fall back to the runtime temp folder.
        /// </summary>
        private static string ResolveLogPath()
        {
            try
            {
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDirectory))
                {
                    var candidate = Path.Combine(baseDirectory, "log.txt");
                    if (CanWrite(candidate))
                        return candidate;
                }
            }
            catch
            {
            }

            try
            {
                var fallbackDirectory = Path.Combine(Path.GetTempPath(), "IRSpeedy");
                Directory.CreateDirectory(fallbackDirectory);
                return Path.Combine(fallbackDirectory, "log.txt");
            }
            catch
            {
            }

            return ".\\log.txt";
        }

        private static bool CanWrite(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        public static void WriteExLog(string Message)
        {
            // Kept as a separate entry point, but writes to the single log.txt so
            // there is only one log file to collect.
            Append(string.Format("{0:s} : {1}\n", DateTime.Now, Scrub(Message)));
        }

        public static void WriteLog(string Message)
        {
            Append(string.Format("{0:s} : {1}\n", DateTime.Now, Scrub(Message)));
        }

        public static void WriteLog(Exception ex, bool isAppCrash = false)
        {
            if (ex == null)
                return;

            Append(string.Format(
                "{0:s} :{3} {1}\n{2}\n",
                DateTime.Now,
                Scrub(ex.Message),
                ex.ToString(),
                isAppCrash ? "[Crashed]" : ""));
        }

        private static string Scrub(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;
            return message
                .Replace("api1.isdm.ir", "[ServerUrl]")
                .Replace("apichcek-p.isdm.ir", "[ProxyUrl]");
        }

        /// <summary>
        /// Logging must never take the app down - least of all while it is already
        /// handling a crash - so a failed write is swallowed rather than thrown on.
        /// </summary>
        private static void Append(string line)
        {
            try
            {
                lock (Locks.Log)
                    File.AppendAllText(LogPath, line);
            }
            catch
            {
            }
        }
    }
}
