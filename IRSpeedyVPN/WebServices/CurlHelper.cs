using IRSpeedyVPN.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Globalization;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class CurlHelper
    {
        private const ushort PeMachineI386 = 0x014C;
        private const ushort PeMachineAmd64 = 0x8664;
        private const string MarkerDns = "__TIME_DNS__:";
        private const string MarkerConnect = "__TIME_CONNECT__:";
        private const string MarkerTls = "__TIME_TLS__:";
        private const string MarkerTotal = "__TIME_TOTAL__:";
        private const string MarkerRemoteIp = "__REMOTE_IP__:";
        private const string MarkerHttpCode = "__HTTP_CODE__:";

        // Reflects the most recent request. Send still resolves the bundled curl on every
        // call, because the versioned runtime may become ready after CurlHelper was
        // constructed.
        private static volatile bool _curlUnavailable;
        private static int _diagnosticSequence;

        /// <summary>
        /// True when the architecture-specific curl packaged in the active runtime is
        /// missing, invalid, has the wrong architecture, or cannot be launched.
        /// Request-level failures such as network timeouts do not make curl unavailable.
        /// Callers use it to skip work that only helps the curl path, such as resolving
        /// the host over DoH to pin it with --resolve, which the managed fallback cannot
        /// honour anyway.
        /// </summary>
        internal static bool IsUnavailable
        {
            get { return _curlUnavailable; }
        }

        // Kept settable for source compatibility with existing tests/callers. Send
        // always replaces it with the candidate selected by the bundled-curl policy.
        public string CurlExePath { get; set; }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy=null)
        {
            return Send(url, method, headers, body, proxy, null, null);
        }

        public CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds,
            string resolveOverride = null)
        {
            return Send(
                url,
                method,
                headers,
                body,
                proxy,
                timeoutSeconds,
                resolveOverride,
                false);
        }

        /// <summary>
        /// DoH probes can skip the managed retry only after curl has already timed out.
        /// API requests use the public overload above and retain the normal fallback.
        /// </summary>
        internal CurlResponse Send(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds,
            string resolveOverride,
            bool skipHttpFallbackOnTimeout)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var diagnostic = SafeCaptureDiagnostics(url, method);
            var totalBudgetMs = timeoutSeconds.HasValue && timeoutSeconds.Value > 0
                ? (long?)timeoutSeconds.Value * 1000L
                : null;
            // Reserve a real managed retry window on longer API attempts. Short
            // DoH probes retain their existing no-fallback-on-timeout policy.
            long fallbackReserveMs = !skipHttpFallbackOnTimeout
                && totalBudgetMs.HasValue && totalBudgetMs.Value >= 6000
                ? 3000L : 0L;
            long? curlBudgetMs = totalBudgetMs - fallbackReserveMs;
            List<CurlCandidate> candidates = ResolveOrderedCandidates(diagnostic);
            Exception lastFailure = null;
            bool bundledCurlUnavailable = true;
            bool curlRequestTimedOut = false;
            string fallbackStage = "managed-fallback: bundled curl unavailable";
            string args = BuildArgs(
                url, method, headers, body, proxy, timeoutSeconds, resolveOverride);
            foreach (CurlCandidate candidate in candidates)
            {
                CurlExePath = candidate.Path;
                diagnostic.ConfiguredPath = candidate.Path;

                if (!IsExistingFile(candidate.Path))
                {
                    fallbackStage = "managed-fallback: bundled curl missing";
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=missing");
                    continue;
                }

                ushort actualMachine;
                string peFailure;
                if (!TryReadPeMachine(candidate.Path, out actualMachine, out peFailure))
                {
                    fallbackStage = "managed-fallback: bundled curl invalid";
                    lastFailure = new InvalidDataException(
                        "Bundled curl is not a valid PE executable (" + peFailure + ").");
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=invalid-pe expectedMachine="
                        + FormatPeMachine(candidate.ExpectedMachine)
                        + " reason=" + peFailure);
                    continue;
                }

                if (actualMachine != candidate.ExpectedMachine)
                {
                    fallbackStage =
                        "managed-fallback: bundled curl architecture mismatch";
                    lastFailure = new BadImageFormatException(
                        "Bundled curl architecture does not match the Windows architecture.");
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=architecture-mismatch expectedMachine="
                        + FormatPeMachine(candidate.ExpectedMachine)
                        + " actualMachine=" + FormatPeMachine(actualMachine));
                    continue;
                }

                diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                    + " outcome=pe-validated expectedMachine="
                    + FormatPeMachine(candidate.ExpectedMachine)
                    + " actualMachine=" + FormatPeMachine(actualMachine));

                bool processStarted = false;
                try
                {
                    int? remainingTimeoutMs = null;
                    if (totalBudgetMs.HasValue)
                    {
                        long remainingMs = curlBudgetMs.Value - totalStopwatch.ElapsedMilliseconds;
                        if (remainingMs <= 0)
                            throw new TimeoutException("The API request budget was fully consumed before curl.");
                        remainingTimeoutMs = (int)Math.Min(int.MaxValue, remainingMs);
                    }

                    CurlProcessResult processResult = ExecuteCurl(
                        candidate.Path, args, remainingTimeoutMs, out processStarted);
                    // Process.Start succeeded. Any failure from this point describes
                    // this request or transport, not availability of the executable.
                    bundledCurlUnavailable = false;
                    diagnostic.ExitCode = processResult.ExitCode;
                    diagnostic.StderrLength = processResult.StderrLength;

                    if (processResult.ExitCode != 0)
                    {
                        bool requestTimedOut = processResult.ExitCode == 28;
                        curlRequestTimedOut = requestTimedOut;
                        fallbackStage = requestTimedOut
                            ? "managed-fallback: bundled curl request timed out"
                            : "managed-fallback: bundled curl request failed";
                        lastFailure = new InvalidOperationException(
                            "curl exited with code " + processResult.ExitCode
                            + " (stderrLength=" + processResult.StderrLength + ").");
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=" + (requestTimedOut ? "timeout" : "exit-failure")
                        + " exitCode=" + processResult.ExitCode
                        + " stderrLength=" + processResult.StderrLength);
                    continue;
                    }

                    Parse(
                        processResult.Output,
                        out string responseBody,
                        out int httpCode,
                        out long? dnsMs,
                        out long? connectMs,
                        out long? tlsMs,
                        out long? totalMs,
                        out string remoteIp);
                    diagnostic.HttpCode = httpCode;
                    diagnostic.CurlDnsMs = dnsMs;
                    diagnostic.CurlConnectMs = connectMs;
                    diagnostic.CurlTlsMs = tlsMs;
                    diagnostic.CurlTotalMs = totalMs;
                    diagnostic.DestinationIp = remoteIp;

                    // All CurlHelper callers use HTTP(S). A zero status means curl did
                    // not produce a valid HTTP response even if the process itself
                    // happened to return zero, so continue through the transport chain.
                    if (httpCode == 0)
                    {
                        fallbackStage =
                            "managed-fallback: bundled curl returned no HTTP status";
                        lastFailure = new InvalidOperationException(
                            "curl completed without an HTTP status code.");
                        diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                            + " outcome=no-http-status exitCode=0");
                        continue;
                    }

                    _curlUnavailable = false;
                    diagnostic.BundledCurlUnavailable = false;
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=success exitCode=0 httpCode=" + httpCode);
                    WriteDiagnostics(diagnostic,
                        "curl completed via " + candidate.Role, null);
                    return new CurlResponse(responseBody, httpCode);
                }
                catch (Exception ex)
                {
                    bundledCurlUnavailable = !processStarted;
                    curlRequestTimedOut = processStarted && ex is TimeoutException;
                    fallbackStage = processStarted
                        ? (ex is TimeoutException
                            ? "managed-fallback: bundled curl request timed out"
                            : "managed-fallback: bundled curl request execution failed")
                        : "managed-fallback: bundled curl launch failed";
                    lastFailure = ex;
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=" + (processStarted
                            ? "request-execution-failure"
                            : "launch-failure")
                        + " processStarted=" + processStarted
                        + " type="
                        + ex.GetType().FullName + " hresult=0x"
                        + ex.HResult.ToString("X8") + GetNativeErrorSuffix(ex));
                }
            }

            // A request-level failure does not disable DoH pinning on the next request.
            // Missing/invalid/unlaunchable binaries are re-evaluated on every Send, so a
            // newly activated runtime can still recover without recreating CurlHelper.
            if (skipHttpFallbackOnTimeout && curlRequestTimedOut)
            {
                return ThrowAfterSkippingHttpFallback(
                    diagnostic,
                    fallbackStage,
                    bundledCurlUnavailable,
                    lastFailure);
            }

            var fallbackRemainingBudgetMs = totalBudgetMs - totalStopwatch.ElapsedMilliseconds;
            if (fallbackRemainingBudgetMs.HasValue && fallbackRemainingBudgetMs.Value <= 0)
            {
                diagnostic.BundledCurlUnavailable = bundledCurlUnavailable;
                diagnostic.HttpFallbackOutcome = "skipped-budget-exhausted";
                WriteDiagnostics(diagnostic, "request budget exhausted before managed fallback", lastFailure);
                throw new TimeoutException("The API request budget was fully consumed before HttpFallback.", lastFailure);
            }

            return SendViaHttpFallback(
                url,
                method,
                headers,
                body,
                proxy,
                fallbackRemainingBudgetMs.HasValue
                    ? (int?)Math.Min(int.MaxValue, fallbackRemainingBudgetMs.Value)
                    : null,
                totalStopwatch.ElapsedMilliseconds,
                diagnostic,
                fallbackStage,
                bundledCurlUnavailable,
                lastFailure);
        }

        private sealed class CurlCandidate
        {
            internal CurlCandidate(string role, string path, ushort expectedMachine)
            {
                Role = role;
                Path = path;
                ExpectedMachine = expectedMachine;
            }

            internal string Role { get; private set; }
            internal string Path { get; private set; }
            internal ushort ExpectedMachine { get; private set; }
        }

        private sealed class CurlProcessResult
        {
            internal string Output;
            internal int ExitCode;
            internal int StderrLength;
        }

        private sealed class CurlDiagnostics
        {
            internal string RequestId;
            internal string Host;
            internal string Method;
            internal string ConfiguredPath;
            internal string ResolvedNow;
            internal string PathCurlMatch;
            internal string ActiveRuntimePath;
            internal string RuntimeStateDescription;
            internal string RuntimeInitializationError;
            internal string SelectedWindowsArchitecture;
            internal string SelectedBundledFileName;
            internal bool? BundledCurlUnavailable;
            internal string HttpFallbackOutcome;
            internal int? HttpFallbackHttpCode;
            internal long? HttpFallbackElapsedMilliseconds;
            internal bool RuntimeManagerReady;
            internal int? HttpCode;
            internal int? ExitCode;
            internal int StderrLength;
            internal readonly List<string> Candidates = new List<string>();
            internal readonly List<string> Attempts = new List<string>();
            internal long? CurlDnsMs;
            internal long? CurlConnectMs;
            internal long? CurlTlsMs;
            internal long? CurlTotalMs;
            internal long? HttpFallbackTimedBudgetMs;
            internal long? HttpFallbackTotalMs;
            internal string DestinationIp;
        }

        private CurlDiagnostics SafeCaptureDiagnostics(string url, string method)
        {
            try
            {
                return CaptureDiagnostics(url, method);
            }
            catch (Exception ex)
            {
                var result = new CurlDiagnostics
                {
                    RequestId = Interlocked.Increment(ref _diagnosticSequence).ToString("x8"),
                    Method = string.IsNullOrWhiteSpace(method) ? "unknown" : method,
                    ConfiguredPath = CurlExePath,
                    ResolvedNow = "<diagnostic capture failed>",
                    PathCurlMatch = "active-runtime-architecture-specific-only"
                };
                result.Candidates.Add("diagnostic-capture error="
                    + ex.GetType().FullName + ": " + ex.Message);
                return result;
            }
        }

        private CurlDiagnostics CaptureDiagnostics(string url, string method)
        {
            var result = new CurlDiagnostics
            {
                RequestId = Interlocked.Increment(ref _diagnosticSequence).ToString("x8"),
                Method = string.IsNullOrWhiteSpace(method) ? "unknown" : method,
                ConfiguredPath = "<resolved per request>",
                PathCurlMatch = "active-runtime-architecture-specific-only"
            };

            try
            {
                result.Host = new Uri(url).Host;
            }
            catch
            {
                result.Host = "invalid-or-relative-url";
            }

            try
            {
                AppServices.GetRuntimeSnapshot(
                    out result.RuntimeManagerReady,
                    out result.ActiveRuntimePath,
                    out result.RuntimeInitializationError);
            }
            catch (Exception ex)
            {
                result.RuntimeInitializationError = ex.GetType().FullName + ": " + ex.Message;
            }

            CaptureRuntimeStateFile(result);

            return result;
        }

        private List<CurlCandidate> ResolveOrderedCandidates(CurlDiagnostics diagnostic)
        {
            CurlExePath = null;
            string runtimePath = diagnostic.ActiveRuntimePath;

            // Request execution is allowed to wait for deferred runtime initialization.
            // This prevents the first login request from permanently missing the
            // versioned curl simply because ResourceManager was still starting.
            try
            {
                runtimePath = AppServices.ResourceManager.TempPath;
                diagnostic.RuntimeManagerReady = true;
                diagnostic.ActiveRuntimePath = runtimePath;
                diagnostic.RuntimeInitializationError = null;
            }
            catch (Exception ex)
            {
                diagnostic.RuntimeInitializationError = ex.GetType().FullName
                    + ": " + ex.Message;
            }

            CaptureRuntimeStateFile(diagnostic);

            var candidates = new List<CurlCandidate>();
            bool is64BitWindows = Environment.Is64BitOperatingSystem;
            string bundledFileName = is64BitWindows ? "curl64.exe" : "curl32.exe";
            ushort expectedMachine = is64BitWindows ? PeMachineAmd64 : PeMachineI386;
            diagnostic.SelectedWindowsArchitecture = is64BitWindows ? "x64" : "x86";
            diagnostic.SelectedBundledFileName = bundledFileName;

            if (!string.IsNullOrWhiteSpace(runtimePath))
            {
                AddOrderedCandidate(candidates, "active-runtime-architecture-match",
                    Path.Combine(runtimePath, "curl", bundledFileName), expectedMachine);
            }
            else
            {
                diagnostic.Attempts.Add(
                    "active-runtime path=<unresolved> outcome=runtime-unavailable");
            }

            diagnostic.ResolvedNow = null;
            foreach (CurlCandidate candidate in candidates)
            {
                diagnostic.Candidates.Add("role=" + candidate.Role + " "
                    + "expectedMachine=" + FormatPeMachine(candidate.ExpectedMachine) + " "
                    + DescribeCandidate(candidate.Path));
                if (diagnostic.ResolvedNow == null && IsExistingFile(candidate.Path))
                    diagnostic.ResolvedNow = candidate.Path;
            }

            if (diagnostic.ResolvedNow == null)
                diagnostic.ResolvedNow = "<none; managed fallback will be used>";

            return candidates;
        }

        private static void AddOrderedCandidate(
            List<CurlCandidate> candidates,
            string role,
            string path,
            ushort expectedMachine)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            foreach (CurlCandidate existing in candidates)
            {
                if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            candidates.Add(new CurlCandidate(role, path, expectedMachine));
        }

        private static bool TryReadPeMachine(
            string path,
            out ushort machine,
            out string failure)
        {
            machine = 0;
            failure = null;

            try
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 64L)
                    {
                        failure = "file-too-small";
                        return false;
                    }

                    if (reader.ReadUInt16() != 0x5A4D)
                    {
                        failure = "missing-mz-signature";
                        return false;
                    }

                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset < 64 || peOffset > stream.Length - 6L)
                    {
                        failure = "invalid-pe-offset";
                        return false;
                    }

                    stream.Position = peOffset;
                    if (reader.ReadUInt32() != 0x00004550)
                    {
                        failure = "missing-pe-signature";
                        return false;
                    }

                    machine = reader.ReadUInt16();
                    return true;
                }
            }
            catch (Exception ex)
            {
                failure = "inspection-failed type=" + ex.GetType().FullName
                    + " hresult=0x" + ex.HResult.ToString("X8");
                return false;
            }
        }

        private static string FormatPeMachine(ushort machine)
        {
            if (machine == PeMachineI386)
                return "0x014C(I386)";
            if (machine == PeMachineAmd64)
                return "0x8664(AMD64)";
            return "0x" + machine.ToString("X4") + "(unknown)";
        }

        private static CurlProcessResult ExecuteCurl(
            string executablePath,
            string arguments,
            int? timeoutMilliseconds,
            out bool processStarted)
        {
            processStarted = false;
            var processStopwatch = Stopwatch.StartNew();
            var output = new StringBuilder();
            int stderrLength = 0;

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        output.AppendLine(args.Data);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        Interlocked.Add(ref stderrLength, args.Data.Length + 2);
                };

                if (!process.Start())
                    throw new InvalidOperationException("curl process could not be started.");

                processStarted = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited;
                if (timeoutMilliseconds.HasValue)
                {
                    // Process startup consumes the same deadline as the request.
                    long remainingMs = timeoutMilliseconds.Value - processStopwatch.ElapsedMilliseconds;
                    exited = process.WaitForExit((int)Math.Max(0L, remainingMs));
                }
                else
                {
                    process.WaitForExit();
                    exited = true;
                }

                if (!exited)
                {
                    try { process.Kill(); }
                    catch { }
                    throw new TimeoutException("curl process did not exit before the timeout.");
                }

                // Flush asynchronous stdout/stderr event handlers before reading results.
                process.WaitForExit();
                return new CurlProcessResult
                {
                    Output = output.ToString(),
                    ExitCode = process.ExitCode,
                    StderrLength = stderrLength
                };
            }
        }

        private static long? ParseLongTiming(string output, string marker)
        {
            string value = ParseTrailingValue(output, marker);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            double seconds;
            if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out seconds))
            {
                return null;
            }

            if (seconds < 0)
                return null;

            return (long)Math.Round(seconds * 1000d);
        }

        private static string ParseTrailingValue(string output, string marker)
        {
            int markerIndex = output.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return null;

            int start = markerIndex + marker.Length;
            int end = output.IndexOf('\r', start);
            if (end < 0)
                end = output.IndexOf('\n', start);
            if (end < 0)
                end = output.Length;

            return output.Substring(start, end - start).Trim();
        }

        private CurlResponse SendViaHttpFallback(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutMilliseconds,
            long elapsedBeforeFallbackMs,
            CurlDiagnostics diagnostic,
            string fallbackStage,
            bool bundledCurlUnavailable,
            Exception curlFailure)
        {
            _curlUnavailable = bundledCurlUnavailable;
            diagnostic.BundledCurlUnavailable = bundledCurlUnavailable;
            // Send immediately. Diagnostic DNS/TCP/TLS probes used to open a
            // separate connection and could consume the budget before the real request.
            if (timeoutMilliseconds.HasValue && timeoutMilliseconds.Value > 0)
                diagnostic.HttpFallbackTimedBudgetMs = timeoutMilliseconds.Value;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                CurlResponse response = HttpFallback.Send(
                    url, method, headers, body, proxy, timeoutMilliseconds);
                stopwatch.Stop();

                diagnostic.HttpFallbackOutcome = "success";
                diagnostic.HttpFallbackTotalMs = elapsedBeforeFallbackMs > 0
                    ? (long?)(elapsedBeforeFallbackMs + stopwatch.ElapsedMilliseconds)
                    : stopwatch.ElapsedMilliseconds;
                diagnostic.HttpFallbackHttpCode = response.HttpCode;
                diagnostic.HttpFallbackElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                diagnostic.Attempts.Add("http-fallback outcome=success httpCode="
                    + response.HttpCode + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                WriteDiagnostics(
                    diagnostic,
                    fallbackStage + "; HttpFallback completed",
                    curlFailure,
                    "CurlFailure");
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                diagnostic.HttpFallbackOutcome = "failure";
                diagnostic.HttpFallbackElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                diagnostic.Attempts.Add("http-fallback outcome=failure elapsedMs="
                    + stopwatch.ElapsedMilliseconds + " type=" + ex.GetType().FullName
                    + " hresult=0x" + ex.HResult.ToString("X8")
                    + GetNativeErrorSuffix(ex) + GetWebExceptionSuffix(ex));
                WriteDiagnostics(
                    diagnostic,
                    fallbackStage + "; HttpFallback failed",
                    ex,
                    "HttpFallbackException");
                throw;
            }
        }

        private CurlResponse ThrowAfterSkippingHttpFallback(
            CurlDiagnostics diagnostic,
            string fallbackStage,
            bool bundledCurlUnavailable,
            Exception curlFailure)
        {
            _curlUnavailable = bundledCurlUnavailable;
            diagnostic.BundledCurlUnavailable = bundledCurlUnavailable;
            diagnostic.HttpFallbackOutcome = "skipped-timeout-policy";
            diagnostic.Attempts.Add(
                "http-fallback outcome=skipped reason=doh-timeout-policy");
            WriteDiagnostics(
                diagnostic,
                fallbackStage + "; HttpFallback skipped for DoH timeout",
                curlFailure,
                "CurlFailure");

            throw new TimeoutException(
                "The DoH curl request timed out; HttpFallback was skipped so the next provider can be tried.",
                curlFailure);
        }

        private static string GetNativeErrorSuffix(Exception exception)
        {
            var win32 = exception as Win32Exception;
            return win32 == null ? "" : " nativeErrorCode=" + win32.NativeErrorCode;
        }

        private static string GetWebExceptionSuffix(Exception exception)
        {
            var webException = exception as WebException;
            return webException == null
                ? ""
                : " webExceptionStatus=" + webException.Status;
        }

        private static void CaptureRuntimeStateFile(CurlDiagnostics diagnostic)
        {
            string statePath = null;
            try
            {
                statePath = Path.Combine(Path.GetTempPath(), "IRSpeedy", "state", "active.json");
                if (!File.Exists(statePath))
                {
                    diagnostic.RuntimeStateDescription = "path=" + statePath + " exists=False";
                    return;
                }

                var info = new FileInfo(statePath);
                diagnostic.RuntimeStateDescription = "path=" + statePath
                    + " exists=True length=" + info.Length
                    + " lastWriteUtc=" + info.LastWriteTimeUtc.ToString("o");
            }
            catch (Exception ex)
            {
                diagnostic.RuntimeStateDescription = "path=" + (statePath ?? "<unresolved>")
                    + " inspectionError=" + ex.GetType().FullName + ": " + ex.Message;
            }
        }

        private static bool IsExistingFile(string path)
        {
            try { return Path.IsPathRooted(path) && File.Exists(path); }
            catch { return false; }
        }

        private static string DescribeCandidate(string path)
        {
            var description = new StringBuilder();
            description.Append("path=").Append(path);

            try
            {
                bool exists = Path.IsPathRooted(path) && File.Exists(path);
                description.Append(" exists=").Append(exists);
                if (exists)
                {
                    var info = new FileInfo(path);
                    description.Append(" length=").Append(info.Length);
                    description.Append(" lastWriteUtc=").Append(info.LastWriteTimeUtc.ToString("o"));
                    try
                    {
                        var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                        if (!string.IsNullOrWhiteSpace(version))
                            description.Append(" fileVersion=").Append(version);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                description.Append(" inspectionError=")
                    .Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
            }

            return description.ToString();
        }

        private static void WriteDiagnostics(
            CurlDiagnostics diagnostic,
            string stage,
            Exception exception,
            string exceptionLabel = "Exception")
        {
            try
            {
                var message = new StringBuilder();
                message.Append("[CurlDiagnostic id=").Append(diagnostic.RequestId).Append("] ")
                    .Append(stage).Append("\r\n")
                    .Append("method=").Append(diagnostic.Method)
                    .Append(" host=").Append(diagnostic.Host)
                    .Append(" configuredPath=").Append(diagnostic.ConfiguredPath)
                    .Append(" preferredExistingCandidateNow=").Append(diagnostic.ResolvedNow)
                    .Append(" pathCurlMatch=")
                    .Append(diagnostic.PathCurlMatch ?? "<unavailable>").Append("\r\n")
                    .Append("runtimeManagerReady=").Append(diagnostic.RuntimeManagerReady)
                    .Append(" activeRuntimePath=").Append(diagnostic.ActiveRuntimePath ?? "<null>")
                    .Append(" selectedWindowsArchitecture=")
                    .Append(diagnostic.SelectedWindowsArchitecture ?? "<unavailable>")
                    .Append(" selectedBundledFile=")
                    .Append(diagnostic.SelectedBundledFileName ?? "<unavailable>")
                    .Append(" bundledCurlUnavailable=")
                    .Append(diagnostic.BundledCurlUnavailable.HasValue
                        ? diagnostic.BundledCurlUnavailable.Value.ToString()
                        : "<undetermined>")
                    .Append(" runtimeInitializationError=")
                    .Append(diagnostic.RuntimeInitializationError ?? "<none>").Append("\r\n")
                    .Append("runtimeState=")
                    .Append(diagnostic.RuntimeStateDescription ?? "<unavailable>").Append("\r\n")
                    .Append("baseDirectory=").Append(AppDomain.CurrentDomain.BaseDirectory)
                    .Append(" currentDirectory=").Append(Environment.CurrentDirectory).Append("\r\n")
                    .Append("os=").Append(Environment.OSVersion.VersionString)
                    .Append(" clr=").Append(Environment.Version)
                    .Append(" assemblyVersion=")
                    .Append(typeof(CurlHelper).Assembly.GetName().Version)
                    .Append(" process64Bit=").Append(Environment.Is64BitProcess)
                    .Append(" os64Bit=").Append(Environment.Is64BitOperatingSystem)
                    .Append(" pathPolicy=").Append(diagnostic.PathCurlMatch);

                if (diagnostic.HttpCode.HasValue)
                    message.Append(" httpCode=").Append(diagnostic.HttpCode.Value);
                if (diagnostic.ExitCode.HasValue)
                    message.Append(" curlExitCode=").Append(diagnostic.ExitCode.Value)
                        .Append(" stderrLength=").Append(diagnostic.StderrLength);
                if (diagnostic.CurlDnsMs.HasValue)
                    message.Append(" curlDnsMs=").Append(diagnostic.CurlDnsMs.Value);
                if (diagnostic.CurlConnectMs.HasValue)
                    message.Append(" curlConnectMs=").Append(diagnostic.CurlConnectMs.Value);
                if (diagnostic.CurlTlsMs.HasValue)
                    message.Append(" curlTlsMs=").Append(diagnostic.CurlTlsMs.Value);
                if (diagnostic.CurlTotalMs.HasValue)
                    message.Append(" curlTotalMs=").Append(diagnostic.CurlTotalMs.Value);
                if (!string.IsNullOrWhiteSpace(diagnostic.DestinationIp))
                    message.Append(" curlDestinationIp=").Append(diagnostic.DestinationIp);
                if (!string.IsNullOrWhiteSpace(diagnostic.HttpFallbackOutcome))
                {
                    message.Append(" httpFallbackOutcome=")
                        .Append(diagnostic.HttpFallbackOutcome);
                    if (diagnostic.HttpFallbackElapsedMilliseconds.HasValue)
                        message.Append(" httpFallbackElapsedMs=")
                            .Append(diagnostic.HttpFallbackElapsedMilliseconds.Value);
                    if (diagnostic.HttpFallbackHttpCode.HasValue)
                        message.Append(" httpFallbackHttpCode=")
                            .Append(diagnostic.HttpFallbackHttpCode.Value);
                    if (diagnostic.HttpFallbackTimedBudgetMs.HasValue)
                        message.Append(" httpFallbackTimedBudgetMs=")
                            .Append(diagnostic.HttpFallbackTimedBudgetMs.Value);
                    if (diagnostic.HttpFallbackTotalMs.HasValue)
                        message.Append(" httpFallbackTotalMs=")
                            .Append(diagnostic.HttpFallbackTotalMs.Value);
                }

                message.Append("\r\nCandidates:");
                foreach (string candidate in diagnostic.Candidates)
                    message.Append("\r\n - ").Append(candidate);

                message.Append("\r\nAttempts:");
                foreach (string attempt in diagnostic.Attempts)
                    message.Append("\r\n - ").Append(attempt);

                if (exception != null)
                {
                    message.Append("\r\n").Append(exceptionLabel)
                        .Append(" type=").Append(exception.GetType().FullName)
                        .Append(" hresult=0x").Append(exception.HResult.ToString("X8"))
                        .Append(" message=").Append(exception.Message)
                        .Append(GetWebExceptionSuffix(exception));

                    var win32 = exception as Win32Exception;
                    if (win32 != null)
                        message.Append(" nativeErrorCode=").Append(win32.NativeErrorCode);

                    if (exception.InnerException != null)
                    {
                        message.Append("\r\n").Append(exceptionLabel)
                            .Append(".InnerException type=")
                            .Append(exception.InnerException.GetType().FullName)
                            .Append(" hresult=0x").Append(exception.InnerException.HResult.ToString("X8"))
                            .Append(" message=").Append(exception.InnerException.Message);
                    }
                }

                LogHelper.WriteExLog(message.ToString());
            }
            catch
            {
                // Diagnostics must not change request behavior.
            }
        }

        internal sealed class CurlResponse
        {
            public CurlResponse(string body, int httpCode)
            {
                Body = body;
                HttpCode = httpCode;
            }

            public string Body { get; }
            public int HttpCode { get; }
        }

        private static string BuildArgs(
            string url,
            string method,
            string headers,
            string body,
            string proxy,
            int? timeoutSeconds,
            string resolveOverride)
        {
            var args = new StringBuilder();
            args.Append(" -sS -L ");

            bool isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            if (isPost)
                args.Append(" -X POST ");

            // DoH-resolved address: pin the host to this IP so a poisoned system
            // resolver is bypassed while SNI and the Host header stay intact.
            if (!string.IsNullOrWhiteSpace(resolveOverride))
            {
                args.Append(" --resolve ");
                args.Append(EscapeArg(resolveOverride));
            }

            if (!string.IsNullOrWhiteSpace(proxy))
            {
                args.Append(" --proxy ");
                args.Append(EscapeArg(proxy));
            }

            if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0)
            {
                args.Append(" --connect-timeout ");
                args.Append(timeoutSeconds.Value);
                args.Append(" --max-time ");
                args.Append(timeoutSeconds.Value);
                args.Append(" ");
            }

            // Headers
            if (!string.IsNullOrEmpty(headers))
            {
                string[] lines = headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    args.Append(" -H ");
                    args.Append(EscapeArg(line));
                }
            }

            // Body
            if (isPost && body != null)
            {
                args.Append(" --data ");
                args.Append(EscapeArg(body));
            }

            string writeOut = "\r\n"
                + MarkerHttpCode + "%{http_code}\r\n"
                + MarkerRemoteIp + "%{remote_ip}\r\n"
                + MarkerDns + "%{time_namelookup}\r\n"
                + MarkerConnect + "%{time_connect}\r\n"
                + MarkerTls + "%{time_appconnect}\r\n"
                + MarkerTotal + "%{time_total}";

            args.Append(" -w ");
            args.Append(EscapeArg(writeOut));

            // URL
            args.Append(" ");
            args.Append(EscapeArg(url));

            return args.ToString();
        }

        private static void Parse(
            string output,
            out string body,
            out int code,
            out long? dnsMs,
            out long? connectMs,
            out long? tlsMs,
            out long? totalMs,
            out string remoteIp)
        {
            const string marker = MarkerHttpCode;
            int idx = output.LastIndexOf(marker, StringComparison.Ordinal);

            body = output;
            code = 0;
            dnsMs = null;
            connectMs = null;
            tlsMs = null;
            totalMs = null;
            remoteIp = null;

            if (idx >= 0)
            {
                dnsMs = ParseLongTiming(output, MarkerDns);
                connectMs = ParseLongTiming(output, MarkerConnect);
                tlsMs = ParseLongTiming(output, MarkerTls);
                totalMs = ParseLongTiming(output, MarkerTotal);
                remoteIp = ParseTrailingValue(output, MarkerRemoteIp);
                int.TryParse(ParseTrailingValue(output, marker), out code);
                body = output.Substring(0, idx).TrimEnd('\r', '\n');
            }
        }

        private static string EscapeArg(string value)
        {
            // Quote for Windows command-line.
            // Keep it simple: escape backslashes and quotes.
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
