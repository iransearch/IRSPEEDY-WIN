using System.Threading;

namespace IRSpeedyVPN.Services
{
    /// <summary>
    /// Coordinates URL tests across services. Lets the UI request that all
    /// in-flight/queued tests stop (e.g. when the user starts a service) and
    /// then allows a fresh batch (e.g. the connect-triggered test).
    /// </summary>
    internal static class UrlTestCoordinator
    {
        private static readonly object Lock = new object();
        private static bool _abortRequested;
        private static CancellationTokenSource displayCancellation = new CancellationTokenSource();
        internal static CancellationToken DisplayCancellation
        {
            get { lock (Lock) return displayCancellation.Token; }
        }

        /// <summary>True while a "stop all tests" request is pending.</summary>
        public static bool AbortRequested
        {
            get { lock (Lock) return _abortRequested; }
        }

        /// <summary>Request that all running URL tests stop (user started a service).</summary>
        public static void CancelAll()
        {
            CancellationTokenSource display;
            lock (Lock)
            {
                _abortRequested = true;
                display = displayCancellation;
            }
            display.Cancel();
        }

        /// <summary>Allow a new batch of tests (fresh UI load / connect flow).</summary>
        public static void BeginBatch()
        {
            lock (Lock)
            {
                if (_abortRequested) displayCancellation = new CancellationTokenSource();
                _abortRequested = false;
            }
        }
    }
}
