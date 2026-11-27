using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CrashRelay;

namespace IRSpeedyVPN.Common
{
    internal static class CrashRelayReporting
    {
        internal static bool Ready { get; private set; }

        internal static void Initialize()
        {
            using (var stream = typeof(CrashRelayReporting).Assembly
                .GetManifestResourceStream("IRSpeedyVPN.CrashRelayDsn"))
            {
                if (stream == null) return;
                using (var reader = new StreamReader(stream))
                    CrashReporter.Init(reader.ReadToEnd().Trim(), new Options
                    {
                        CaptureUnhandledExceptions = false,
                        CollectConsole = false,
                        CollectTrace = false,
                        CollectHttp = false
                    });
            }
            Ready = CrashReporter.IsInitialized;
            if (!Ready) LogHelper.WriteLog("[CrashRelay] unavailable; check the build configuration");
        }

        internal static void BindUi(SynchronizationContext context)
        {
            if (Ready) CrashReporter.BindUiContext(context);
        }

        internal static void Capture(Exception exception, bool fatal, IDictionary<string, string> details = null)
        {
            CrashReporter.CaptureDiagnostic(exception,
                fatal ? "Windows application crash" : "Windows application error", fatal, details);
        }

        internal static void CaptureCoreExit()
        {
            CrashReporter.CaptureMessage("VPN core exited during an active connection", "error");
        }

        internal static void Verify()
        {
            if (Ready) CrashReporter.CaptureMessage("IRSPEEDY Windows telemetry test", "error");
            LogHelper.WriteLog(Ready
                ? "[CrashRelay] verification queued; confirm receipt in CrashRelay"
                : "[CrashRelay] verification unavailable; build with a valid CrashRelay DSN");
        }
    }
}
