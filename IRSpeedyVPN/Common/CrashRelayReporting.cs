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
                if (stream == null)
                {
                    LogHelper.WriteLog("[CrashRelay] status=disabled reason=missing-build-dsn");
                    return;
                }
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
            LogHelper.WriteLog(Ready
                ? "[CrashRelay] status=ready"
                : "[CrashRelay] status=unavailable reason=initialization-failed");
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
                ? "[CrashRelay] verification requested; confirm receipt in CrashRelay"
                : "[CrashRelay] verification unavailable; see the startup status above");
        }
    }
}
