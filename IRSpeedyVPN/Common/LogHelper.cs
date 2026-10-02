using System;

namespace IRSpeedyVPN.Common
{
    public static class LogHelper
    {
        private static readonly DailyLogStore Store = CreateStore();

        private static DailyLogStore CreateStore()
        {
            try { return new DailyLogStore(DailyLogStore.ResolveDirectory()); }
            catch { return null; }
        }

        public static void WriteExLog(string Message)
        {
            try { ErrorReporting.ObserveDiagnostic(Message); } catch { }
            if (!LogPolicy.ShouldWrite(Message)) return;
            var now = DateTime.Now;
            Append(now, string.Format("{0:s} : {1}\n", now, Scrub(Message)));
        }

        public static void WriteLog(string Message)
        {
            if (!LogPolicy.ShouldWrite(Message)) return;
            var now = DateTime.Now;
            Append(now, string.Format("{0:s} : {1}\n", now, Scrub(Message)));
        }

        public static void WriteLog(Exception ex, bool isAppCrash = false)
        {
            if (ex == null)
                return;

            try { ErrorReporting.Capture(ex, isAppCrash); } catch { }
            var now = DateTime.Now;
            Append(now, string.Format(
                "{0:s} :{3} {1}\n{2}\n",
                now,
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
        private static void Append(DateTime timestamp, string line)
        {
            try
            {
                Store?.Append(timestamp, line);
            }
            catch
            {
            }
        }
    }
}
