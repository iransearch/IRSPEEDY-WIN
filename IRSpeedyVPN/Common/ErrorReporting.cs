using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.ComponentModel;
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
        private static readonly string SessionId = Guid.NewGuid().ToString("N");
        private static string installationId = SessionId;
        private static string identityScope = "session";
        private static readonly object DiagnosticKey = new object();
        private static readonly string[] DiagnosticTags =
        {
            "http.stage", "http.transport", "http.route", "http.endpoint", "http.budget_ms", "http.elapsed_ms",
            "http.deadline_expired", "curl.reason", "curl.exit_code", "curl.unavailable",
            "curl.elapsed_ms", "curl.failure_type", "curl.native_error", "curl.request_id",
            "api.request_id", "api.flow", "api.endpoint", "api.attempt", "api.elapsed_ms",
            "api.budget_ms", "api.attempt1", "api.attempt2", "api.attempt3", "api.attempt4", "network.start_seq", "network.end_seq",
            "connection.selected", "connection.observed", "connection.observed_age_ms", "network.monitor_started"
        };

        // Only our private dictionary is exported, never arbitrary Exception.Data.
        internal static void Annotate(Exception exception, params string[] fields)
        {
            try
            {
                if (exception == null) return;
                var values = exception.Data[DiagnosticKey] as Dictionary<string, string>;
                if (values == null) exception.Data[DiagnosticKey] = values = new Dictionary<string, string>();
                for (int i = 0; i + 1 < fields.Length; i += 2)
                    if (DiagnosticTags.Contains(fields[i]) && SafeDiagnosticValue(fields[i + 1]))
                        values[fields[i]] = fields[i + 1];
            }
            catch { }
        }

        private static bool SafeDiagnosticValue(string value)
            => value != null && value.Length <= 200 && Regex.IsMatch(value, @"\A[A-Za-z0-9_.:;=|,-]+\z");

        internal static string EndpointAlias(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return "other";
            switch (uri.Host.ToLowerInvariant())
            {
                case "api1.greadia.app": return "api1";
                case "api3.greadia.ir": return "api3";
                case "api2.greadia.app": return "api2";
                case "apix.myapifast.ir": return "apix";
                default: return "other";
            }
        }

        internal static string FailureCode(Exception exception)
        {
            for (var e = exception; e != null; e = e.InnerException)
            {
                var web = e as WebException;
                if (web != null) return web.Status.ToString();
                if (e is TimeoutException) return "Timeout";
            }
            return "Other";
        }
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
                // Random identity, independent of account, device name and hardware IDs.
                try
                {
                    string path = Path.Combine(cache, "installation-id");
                    if (!File.Exists(path))
                    {
                        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        using (var writer = new StreamWriter(file)) writer.Write(Guid.NewGuid().ToString("N"));
                    }
                    Guid parsed;
                    if (Guid.TryParse(File.ReadAllText(path).Trim(), out parsed))
                    {
                        installationId = parsed.ToString("N");
                        identityScope = "installation";
                    }
                }
                catch { } // A read-only profile still gets a session identity.
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
                    // DiagnosticSource and System.Diagnostics.Metrics integrations are
                    // not included in Sentry's .NET Framework target; no disable calls needed.
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
            clean.SetTag("installation.id", installationId);
            clean.SetTag("identity.scope", identityScope);
            clean.SetTag("session.id", SessionId);
            clean.SetTag("diagnostic.schema", "http-context-v2");
            foreach (var key in DiagnosticTags.Concat(new[] { "diagnostic.schema", "installation.id", "identity.scope",
                "session.id", "build.mvid", "os.version", "clr.version", "web.status", "socket.error", "native.error" }))
                if (input.Tags.TryGetValue(key, out var value) && SafeDiagnosticValue(value)) clean.SetTag(key, value);
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
                    Value = "HRESULT=0x" + current.HResult.ToString("X8")
                        + (current is WebException ? "; WebException.Status=" + ((WebException)current).Status : "")
                        + (current is SocketException ? "; SocketError=" + ((SocketException)current).SocketErrorCode : ""),
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
            result.SetTag("diagnostic.schema", "http-context-v2");
            result.SetTag("installation.id", installationId);
            result.SetTag("identity.scope", identityScope);
            result.SetTag("session.id", SessionId);
            result.SetTag("build.mvid", typeof(ErrorReporting).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
            result.SetTag("os.version", Environment.OSVersion.Version.ToString());
            result.SetTag("clr.version", Environment.Version.ToString());
            result.SetTag("connection.selected", ConnectionDiagnostics.SelectedMode);
            result.SetTag("connection.observed", ConnectionDiagnostics.ObservedMode);
            result.SetTag("connection.observed_age_ms", ConnectionDiagnostics.ObservedModeAgeMs.ToString());
            result.SetTag("network.end_seq", ConnectionDiagnostics.EventSequence.ToString());
            result.SetTag("network.monitor_started", ConnectionDiagnostics.MonitorStarted.ToString());
            // Inner transport metadata survives wrapper exceptions; outer metadata wins.
            var exceptions = new List<Exception>();
            for (var e = exception; e != null && exceptions.Count < 8; e = e.InnerException) exceptions.Add(e);
            foreach (var e in exceptions.AsEnumerable().Reverse())
            {
                var values = e.Data[DiagnosticKey] as Dictionary<string, string>;
                if (values != null)
                    foreach (var pair in values) result.SetTag(pair.Key, pair.Value);
                var web = e as WebException;
                if (web != null) result.SetTag("web.status", web.Status.ToString());
                var socket = e as SocketException;
                if (socket != null) result.SetTag("socket.error", socket.SocketErrorCode.ToString());
                var native = e as Win32Exception;
                if (native != null) result.SetTag("native.error", native.NativeErrorCode.ToString());
            }
            return result;
        }

        internal static void Capture(Exception exception, bool fatal)
        {
            if (!ready || exception == null) return;
            try
            {
                string key = exception.GetType().FullName + ":" + exception.HResult
                    + ":" + exception.TargetSite?.Name + ":" + FailureCode(exception);
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
