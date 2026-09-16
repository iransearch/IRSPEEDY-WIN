using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Sentry;
using Sentry.Protocol;

namespace IRSpeedyVPN.Common
{
    // Only explicitly constructed metadata reaches Sentry. Local diagnostic logs stay local.
    internal static class ErrorReporting
    {
        private static IDisposable sdk;
        private static volatile bool ready;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, DateTime> Recent = new Dictionary<string, DateTime>();
        private static int probeReports;
        private static readonly string Release = "IRSpeedyVPN@" +
            typeof(ErrorReporting).Assembly.GetName().Version;
        private static readonly string[] AllowedTags =
        {
            "report.kind", "error.kind", "countryId", "member", "protocol", "phase",
            "exception.type", "exception.hresult", "process.arch"
        };

        internal static void Initialize()
        {
            try
            {
                var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IRSpeedy", "Sentry");
                Directory.CreateDirectory(cache);
                sdk = SentrySdk.Init(o =>
                {
                    o.Dsn = "https://3b8667a42b4e779280ce1e2a9686c500@o4512093060136960.ingest.us.sentry.io/4512093067345920";
                    o.Debug = false;
                    o.Release = Release;
                    o.Environment = "production";
                    o.SendDefaultPii = false;
                    o.IsEnvironmentUser = false;
                    o.AutoSessionTracking = false;
                    o.EnableLogs = false;
                    o.TracesSampleRate = 0;
                    o.CaptureFailedRequests = false;
                    o.AttachStacktrace = false;
                    o.MaxBreadcrumbs = 0;
                    o.MaxQueueItems = 30;
                    o.MaxCacheItems = 30;
                    o.CacheDirectoryPath = cache;
                    o.InitCacheFlushTimeout = TimeSpan.Zero;
                    o.ShutdownTimeout = TimeSpan.FromSeconds(2);
                    // Existing Program handlers already log WPF, AppDomain and Task errors.
                    o.DisableAppDomainUnhandledExceptionCapture();
                    o.DisableUnobservedTaskExceptionCapture();
                    o.DisableDiagnosticSourceIntegration();
                    o.DisableSystemDiagnosticsMetricsIntegration();
                    o.DisableNetFxInstallationsIntegration();
                    o.SetBeforeSend(KeepSafeFields);
                });
                ready = true;
                LogHelper.WriteLog("[ErrorReporting] initialized");
            }
            catch
            {
                ready = false;
                LogHelper.WriteLog("[ErrorReporting] unavailable; local logging remains active");
            }
        }

        // Rebuild after SDK enrichment so machine names, paths, HTTP data and user data
        // cannot be added by automatic event processors or a shared scope.
        internal static SentryEvent KeepSafeFields(SentryEvent input)
        {
            if (!input.Tags.TryGetValue("report.kind", out var kind)
                || (kind != "exception" && kind != "probe" && kind != "self-test"))
                return null;
            var clean = new SentryEvent
            {
                Message = kind == "exception" ? "Application exception"
                    : kind == "probe" ? "Server probe failed" : "IRSpeedy Sentry verification",
                Level = input.Level,
                Release = Release,
                Environment = "production",
                Fingerprint = input.Fingerprint
            };
            foreach (var key in AllowedTags)
                if (input.Tags.TryGetValue(key, out var value)) clean.SetTag(key, value);
            // These exception objects are built by PrepareException, never from raw exceptions.
            if (kind == "exception") clean.SentryExceptions = input.SentryExceptions;
            return clean;
        }

        internal static SentryEvent PrepareException(Exception exception, bool fatal)
        {
            var chain = new List<SentryException>();
            for (var current = exception; current != null && chain.Count < 8; current = current.InnerException)
            {
                var stack = new SentryStackTrace();
                foreach (var frame in (new StackTrace(current, false).GetFrames() ?? new StackFrame[0]).Take(40).Reverse())
                {
                    var method = frame.GetMethod();
                    stack.Frames.Add(new SentryStackFrame
                    {
                        Function = method == null ? "<unknown>" : method.Name,
                        Module = method?.DeclaringType?.FullName,
                        InApp = method?.DeclaringType?.Assembly == typeof(ErrorReporting).Assembly
                    });
                }
                chain.Add(new SentryException
                {
                    Type = current.GetType().FullName,
                    Value = "Message omitted; HRESULT=0x" + current.HResult.ToString("X8"),
                    Stacktrace = stack
                });
            }
            chain.Reverse();
            var result = new SentryEvent
            {
                Level = fatal ? SentryLevel.Fatal : SentryLevel.Error,
                SentryExceptions = chain
            };
            result.SetTag("report.kind", "exception");
            result.SetTag("exception.type", exception.GetType().FullName);
            result.SetTag("exception.hresult", exception.HResult.ToString("X8"));
            result.SetTag("process.arch", Environment.Is64BitProcess ? "x64" : "x86");
            result.SetTag("error.kind", Classify(exception.ToString()));
            return result;
        }

        internal static void Capture(Exception exception, bool fatal)
        {
            if (!ready || exception == null) return;
            try
            {
                string key = exception.GetType().FullName + ":" + exception.HResult
                    + ":" + exception.TargetSite?.Name;
                if (!Admit(key, false)) return;
                SentrySdk.CaptureEvent(PrepareException(exception, fatal));
                if (fatal) SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            catch { } // Never recurse through LogHelper on a telemetry failure.
        }

        internal static void ObserveDiagnostic(string message)
        {
            if (!ready || string.IsNullOrEmpty(message)
                || !message.StartsWith("[ProbeDetail]", StringComparison.Ordinal)) return;
            try
            {
                var outcome = Field(message, "result");
                if (outcome != "failure" && outcome != "missing" && outcome != "exception") return;
                string country = NumericField(message, "countryId");
                string member = NumericField(message, "member");
                string reason = Classify(message);
                if (!Admit("probe:" + country + ":" + member + ":" + reason, true)) return;
                var report = new SentryEvent { Level = SentryLevel.Warning };
                report.SetTag("report.kind", "probe");
                report.SetTag("countryId", country);
                report.SetTag("member", member);
                report.SetTag("error.kind", reason);
                string phase = Field(message, "phase");
                report.SetTag("phase", phase == "primary" || phase == "alternate" ? phase : "unknown");
                string protocol = Field(message, "protocol").ToLowerInvariant();
                var known = new[] { "vless", "vmess", "hysteria", "hysteria2", "trojan", "shadowsocks", "socks" };
                report.SetTag("protocol", known.Contains(protocol) ? protocol : "other");
                report.Fingerprint = new[] { "server-probe", country, reason };
                SentrySdk.CaptureEvent(report);
            }
            catch { }
        }

        private static bool Admit(string key, bool probe)
        {
            lock (Gate)
            {
                // Bounded per-process reporting. Repeated failed probes must not flood telemetry.
                if (probe && probeReports >= 20) return false;
                if (Recent.TryGetValue(key, out var previous) && DateTime.UtcNow - previous < TimeSpan.FromMinutes(10))
                    return false;
                if (Recent.Count >= 100) return false;
                Recent[key] = DateTime.UtcNow;
                if (probe) probeReports++;
                return true;
            }
        }

        private static string Field(string text, string name)
        {
            return Regex.Match(text, @"(?:^|\s)" + name + @"=([^\s]+)").Groups[1].Value;
        }

        private static string NumericField(string text, string name)
        {
            int value;
            return int.TryParse(Field(text, name), out value) ? value.ToString() : "unknown";
        }

        internal static string Classify(string text)
        {
            text = (text ?? "").ToLowerInvariant();
            if (text.Contains("network changed")) return "network_changed";
            if (text.Contains("forbidden by its access permissions")) return "socket_access_denied";
            if (text.Contains("timeout") || text.Contains("timed out") || text.Contains("deadline")) return "timeout";
            if (text.Contains("no such host") || text.Contains("name resolution")) return "dns";
            if (text.Contains("certificate") || text.Contains("x509")) return "certificate";
            if (text.Contains("eof") || text.Contains("closed") || text.Contains("reset")) return "connection_closed";
            if (text.Contains("refused")) return "connection_refused";
            return "other";
        }

        internal static void Verify()
        {
            if (!ready) return;
            try
            {
                var report = new SentryEvent { Level = SentryLevel.Info };
                report.SetTag("report.kind", "self-test");
                SentrySdk.CaptureEvent(report);
                LogHelper.WriteLog("[ErrorReporting] verification queued; confirm receipt in Sentry");
            }
            catch { }
        }

        internal static void Shutdown()
        {
            ready = false;
            try { sdk?.Dispose(); } catch { }
            sdk = null;
        }
    }
}
