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
using IRSpeedyVPN.Services.Libcore;

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
            "connection.selected", "connection.observed", "connection.observed_age_ms", "network.monitor_started",
            "exception.handled", "core.rpc", "core.rpc_stage", "core.rpc_budget_ms", "core.rpc_elapsed_ms", "core.close_reason",
            "core.operation", "core.operation_id", "core.lifecycle_stage", "core.outcome", "core.attempts",
            "core.attempt1_ms", "core.attempt2_ms", "core.retry_prepare_ms", "core.total_ms",
            "core.first_rpc_stage", "core.first_close_reason", "core.first_failure", "core.first_hresult", "core.first_socket_error", "core.first_native_error",
            "core.config_id", "core.xray_config_id", "core.runtime_sha256", "core.runtime_commit", "core.runtime_version", "core.runtime_arch",
            "core.process_state", "core.pid", "core.runtime_pid", "core.runtime_matches_process", "core.exit_code", "core.owned", "core.cancel_requested", "core.need_xray",
            "core.main_members", "core.ai_members", "core.connection_generation", "core.connection_id",
            "core.failure_reason", "core.last_output_category", "core.last_output_reason", "core.last_output_age_ms"
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
                    if (DiagnosticTags.Contains(fields[i]) && SafeField(fields[i], fields[i + 1]))
                        values[fields[i]] = fields[i + 1];
            }
            catch { }
        }

        private static bool SafeDiagnosticValue(string value)
            => value != null && value.Length <= 200 && Regex.IsMatch(value, @"\A[A-Za-z0-9_.:;=|,-]+\z");

        private static bool OneOf(string value, string options) => options.Split('|').Contains(value);
        private static bool SafeField(string key, string value)
        {
            if (value == null || value.Length > 200) return false;
            if (key == "exception.handled") return OneOf(value, "true|false");
            if (!key.StartsWith("core.", StringComparison.Ordinal)) return SafeDiagnosticValue(value);
            switch (key)
            {
                case "core.rpc": return OneOf(value, "start|stop|test|stop-test|check-config|query-connections|query-selectors|query-url-test|selector-action|build-info|other");
                case "core.operation": return OneOf(value, "start|test|other");
                case "core.rpc_stage": case "core.first_rpc_stage": return OneOf(value, "connect|write-header|write-body|read-header|read-body|decode|server-error|complete|unknown");
                case "core.lifecycle_stage": return OneOf(value, "ensure|stop-before-start|start|test|other");
                case "core.close_reason": case "core.first_close_reason": return OneOf(value, "deadline|connect-timeout|transport|server-error|canceled|unknown");
                case "core.outcome": return OneOf(value, "failed|retry-failed|retry-succeeded");
                case "core.attempts": return OneOf(value, "1|2");
                case "core.first_failure": return OneOf(value, "timeout|network_changed|socket_access_denied|dns|certificate|connection_closed|connection_refused|other");
                case "core.process_state": return OneOf(value, "alive|exited|unavailable|unknown");
                case "core.runtime_matches_process": return OneOf(value, "true|false|unknown");
                case "core.owned": case "core.cancel_requested": case "core.need_xray": return OneOf(value, "true|false");
                case "core.operation_id": return Regex.IsMatch(value, @"\A[a-f0-9]{32}\z");
                case "core.connection_id": return value == "none" || Regex.IsMatch(value, @"\A[a-f0-9]{32}\z");
                case "core.config_id": case "core.xray_config_id": return OneOf(value, "none|unknown") || Regex.IsMatch(value, @"\A[a-f0-9]{16}\z");
                case "core.runtime_sha256": return value == "unknown" || Regex.IsMatch(value, @"\A[a-f0-9]{64}\z");
                case "core.runtime_commit": return value == "unknown" || Regex.IsMatch(value, @"\A[a-fA-F0-9]{7,64}\z");
                case "core.runtime_version": return value == "unknown" || value.Length <= 80 && Regex.IsMatch(value, @"\Av?[0-9]+(?:\.[0-9]+){1,3}(?:[-+][A-Za-z0-9.-]+)?\z");
                case "core.runtime_arch": return OneOf(value, "unknown|amd64|386|arm64|windows/amd64|windows/386|windows/arm64");
                case "core.first_socket_error":
                    SocketError error;
                    return Enum.TryParse(value, out error) && Enum.IsDefined(typeof(SocketError), error);
                case "core.first_hresult": return Regex.IsMatch(value, @"\A[0-9A-Fa-f]{8}\z");
                case "core.last_output_category": return OneOf(value, "unknown|error|warning|panic|fatal|network-changed|tun|interface|route|other|pool-selection-error");
                case "core.failure_reason": case "core.last_output_reason": return OneOf(value, "unknown|none|unclassified|invalid-http-header|socket-access-denied|address-unavailable|no-healthy-outbound|connection-refused|network-unreachable|dns-failure|tls-failure|quic-idle-timeout|timeout|connection-reset|unexpected-eof|canceled");
                default:
                    if (!DiagnosticTags.Contains(key)) return false;
                    long number;
                    return Regex.IsMatch(value, @"\A-?[0-9]{1,15}\z") && long.TryParse(value, out number)
                        && number >= (key == "core.exit_code" ? int.MinValue : key == "core.pid" || key == "core.runtime_pid" || key == "core.last_output_age_ms" ? -1 : 0)
                        && number <= (key == "core.exit_code" ? int.MaxValue : 999999999999999L);
            }
        }

        private static string Tag(SentryEvent report, string key, string fallback = "unknown")
            => report.Tags.TryGetValue(key, out var value) && SafeField(key, value) ? value : fallback;
        private static void DecorateCoreReport(SentryEvent report)
        {
            if (!report.Tags.ContainsKey("core.operation") && !report.Tags.ContainsKey("core.rpc")) return;
            string operation = Tag(report, "core.operation", Tag(report, "core.rpc", "other"));
            string stage = Tag(report, "core.lifecycle_stage", Tag(report, "core.rpc_stage"));
            string outcome = Tag(report, "core.outcome", "failed");
            report.Message = "Core " + operation + " RPC " + Tag(report, "core.rpc", operation) + " at " + Tag(report, "core.rpc_stage", stage) + ": " + outcome;
            report.SetTag("diagnostic.schema", "core-rpc-v1");
            report.Fingerprint = new[] { "core-rpc-v1", operation, Tag(report, "core.rpc", "other"), stage, Tag(report, "core.rpc_stage"), Tag(report, "error.kind", "other"), outcome };
            if (report.Level != SentryLevel.Fatal && outcome == "retry-succeeded") report.Level = SentryLevel.Warning;
        }

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
                if (input.Tags.TryGetValue(key, out var value) && SafeField(key, value)) clean.SetTag(key, value);
            // These exception objects are built by PrepareException, never from raw exceptions.
            if (kind == "exception") clean.SentryExceptions = input.SentryExceptions;
            DecorateCoreReport(clean);
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
            result.SetTag("exception.handled", fatal ? "false" : "true");
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
                var rpc = CoreRpcTrace.Read(e);
                if (rpc != null)
                    foreach (var pair in rpc)
                        if (DiagnosticTags.Contains(pair.Key) && SafeField(pair.Key, pair.Value)) result.SetTag(pair.Key, pair.Value);
                if (values != null)
                    foreach (var pair in values)
                        if (DiagnosticTags.Contains(pair.Key) && SafeField(pair.Key, pair.Value)) result.SetTag(pair.Key, pair.Value);
                var web = e as WebException;
                if (web != null) result.SetTag("web.status", web.Status.ToString());
                var socket = e as SocketException;
                if (socket != null) result.SetTag("socket.error", socket.SocketErrorCode.ToString());
                var native = e as Win32Exception;
                if (native != null) result.SetTag("native.error", native.NativeErrorCode.ToString());
            }
            DecorateCoreReport(result);
            return result;
        }

        private static string GroupingField(Exception exception, string key)
        {
            // Outer annotations win, matching PrepareException. Read only the
            // five grouping fields before admission, without building stacks.
            int depth = 0;
            for (var current = exception; current != null && depth++ < 8; current = current.InnerException)
            {
                var values = current.Data[DiagnosticKey] as IDictionary<string, string>;
                if (values != null && values.TryGetValue(key, out var value) && SafeField(key, value)) return value;
                var rpc = CoreRpcTrace.Read(current);
                if (rpc != null && rpc.TryGetValue(key, out value) && SafeField(key, value)) return value;
            }
            return "unknown";
        }
        internal static string DeduplicationKey(Exception exception, bool fatal)
            => exception.GetType().FullName + ":" + exception.HResult + ":" + exception.TargetSite?.Name + ":" + FailureCode(exception)
                + ":" + GroupingField(exception, "core.operation") + ":" + GroupingField(exception, "core.rpc")
                + ":" + GroupingField(exception, "core.rpc_stage") + ":" + GroupingField(exception, "core.lifecycle_stage")
                + ":" + GroupingField(exception, "core.outcome") + (fatal ? ":fatal" : ":handled");

        internal static void Capture(Exception exception, bool fatal)
        {
            if (!ready || exception == null) return;
            try
            {
                if (!Admit(DeduplicationKey(exception, fatal), false)) return;
                var report = PrepareException(exception, fatal);
                SentrySdk.CaptureEvent(report);
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
