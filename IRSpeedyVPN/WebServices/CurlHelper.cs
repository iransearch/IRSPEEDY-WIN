using IRSpeedyVPN.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class CurlHelper
    {
        // Reflects the most recent request. Send still resolves the ordered candidates on
        // every call, because the versioned runtime may become ready after CurlHelper was
        // constructed.
        private static volatile bool _curlUnavailable;
        private static int _diagnosticSequence;

        /// <summary>
        /// True when the most recent request exhausted both ordered curl candidates.
        /// Callers use it to skip work that only helps the curl path, such as resolving
        /// the host over DoH to pin it with --resolve, which the managed fallback cannot
        /// honour anyway.
        /// </summary>
        internal static bool IsUnavailable
        {
            get { return _curlUnavailable; }
        }

        // Kept settable for source compatibility with existing tests/callers. Send
        // always replaces it with the candidate selected by the ordered policy.
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
            var diagnostic = SafeCaptureDiagnostics(url, method);
            List<CurlCandidate> candidates = ResolveOrderedCandidates(diagnostic);
            Exception lastFailure = null;
            string args = BuildArgs(
                url, method, headers, body, proxy, timeoutSeconds, resolveOverride);

            foreach (CurlCandidate candidate in candidates)
            {
                if (!IsExistingFile(candidate.Path))
                {
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=missing");
                    continue;
                }

                CurlExePath = candidate.Path;
                diagnostic.ConfiguredPath = candidate.Path;

                try
                {
                    CurlProcessResult processResult = ExecuteCurl(
                        candidate.Path, args, timeoutSeconds);
                    diagnostic.ExitCode = processResult.ExitCode;
                    diagnostic.StderrLength = processResult.StderrLength;

                    if (processResult.ExitCode != 0)
                    {
                        lastFailure = new InvalidOperationException(
                            "curl exited with code " + processResult.ExitCode
                            + " (stderrLength=" + processResult.StderrLength + ").");
                        diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                            + " outcome=exit-failure exitCode=" + processResult.ExitCode
                            + " stderrLength=" + processResult.StderrLength);
                        continue;
                    }

                    Parse(processResult.Output, out string responseBody, out int httpCode);
                    diagnostic.HttpCode = httpCode;

                    // All CurlHelper callers use HTTP(S). A zero status means curl did
                    // not produce a valid HTTP response even if the process itself
                    // happened to return zero, so continue through the transport chain.
                    if (httpCode == 0)
                    {
                        lastFailure = new InvalidOperationException(
                            "curl completed without an HTTP status code.");
                        diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                            + " outcome=no-http-status exitCode=0");
                        continue;
                    }

                    _curlUnavailable = false;
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=success exitCode=0 httpCode=" + httpCode);
                    WriteDiagnostics(diagnostic,
                        "curl completed via " + candidate.Role, null);
                    return new CurlResponse(responseBody, httpCode);
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    diagnostic.Attempts.Add(candidate.Role + " path=" + candidate.Path
                        + " outcome=launch-or-transport-failure type="
                        + ex.GetType().FullName + " hresult=0x"
                        + ex.HResult.ToString("X8") + GetNativeErrorSuffix(ex));
                }
            }

            // Do not permanently short-circuit later calls. A newly activated runtime
            // can make its packaged curl available after this request completes.
            _curlUnavailable = true;
            WriteDiagnostics(diagnostic,
                "managed-fallback: ordered curl candidates exhausted", lastFailure);
            return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
        }

        private sealed class CurlCandidate
        {
            internal CurlCandidate(string role, string path)
            {
                Role = role;
                Path = path;
            }

            internal string Role { get; private set; }
            internal string Path { get; private set; }
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
            internal bool RuntimeManagerReady;
            internal int? HttpCode;
            internal int? ExitCode;
            internal int StderrLength;
            internal readonly List<string> Candidates = new List<string>();
            internal readonly List<string> Attempts = new List<string>();
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
                    PathCurlMatch = "<not used by policy>"
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
                PathCurlMatch = "<not used by policy>"
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
            if (!string.IsNullOrWhiteSpace(runtimePath))
            {
                AddOrderedCandidate(candidates, "active-runtime",
                    Path.Combine(runtimePath, "curl", "curl.exe"));
            }
            else
            {
                diagnostic.Attempts.Add(
                    "active-runtime path=<unresolved> outcome=runtime-unavailable");
            }

            string systemCurlPath = GetSystemCurlPath();
            if (!string.IsNullOrWhiteSpace(systemCurlPath))
            {
                AddOrderedCandidate(candidates, "windows-system32", systemCurlPath);
            }
            else
            {
                diagnostic.Attempts.Add(
                    "windows-system32 path=<unresolved> outcome=windows-folder-unavailable");
            }

            diagnostic.ResolvedNow = null;
            foreach (CurlCandidate candidate in candidates)
            {
                diagnostic.Candidates.Add("role=" + candidate.Role + " "
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
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            foreach (CurlCandidate existing in candidates)
            {
                if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            candidates.Add(new CurlCandidate(role, path));
        }

        private static string GetSystemCurlPath()
        {
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (string.IsNullOrWhiteSpace(windows))
                    windows = Environment.GetEnvironmentVariable("SystemRoot");
                if (string.IsNullOrWhiteSpace(windows))
                    return null;

                // A 32-bit process on 64-bit Windows is redirected away from the native
                // System32 directory. Sysnative is the supported alias that reaches the
                // same native System32 curl requested by the policy.
                if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                    return Path.Combine(windows, "Sysnative", "curl.exe");

                return Path.Combine(windows, "System32", "curl.exe");
            }
            catch
            {
                return null;
            }
        }

        private static CurlProcessResult ExecuteCurl(
            string executablePath,
            string arguments,
            int? timeoutSeconds)
        {
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

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited;
                if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0)
                {
                    long waitMilliseconds = ((long)timeoutSeconds.Value + 5L) * 1000L;
                    if (waitMilliseconds > int.MaxValue)
                        waitMilliseconds = int.MaxValue;
                    exited = process.WaitForExit((int)waitMilliseconds);
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

        private static string GetNativeErrorSuffix(Exception exception)
        {
            var win32 = exception as Win32Exception;
            return win32 == null ? "" : " nativeErrorCode=" + win32.NativeErrorCode;
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

        private static void WriteDiagnostics(CurlDiagnostics diagnostic, string stage, Exception exception)
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

                message.Append("\r\nCandidates:");
                foreach (string candidate in diagnostic.Candidates)
                    message.Append("\r\n - ").Append(candidate);

                message.Append("\r\nAttempts:");
                foreach (string attempt in diagnostic.Attempts)
                    message.Append("\r\n - ").Append(attempt);

                if (exception != null)
                {
                    message.Append("\r\nException type=").Append(exception.GetType().FullName)
                        .Append(" hresult=0x").Append(exception.HResult.ToString("X8"))
                        .Append(" message=").Append(exception.Message);

                    var win32 = exception as Win32Exception;
                    if (win32 != null)
                        message.Append(" nativeErrorCode=").Append(win32.NativeErrorCode);

                    if (exception.InnerException != null)
                    {
                        message.Append("\r\nInnerException type=")
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

            string writeOut = "\r\n__HTTP_CODE__:%{http_code}";

            args.Append(" -w ");
            args.Append(EscapeArg(writeOut));

            // URL
            args.Append(" ");
            args.Append(EscapeArg(url));

            return args.ToString();
        }

        private static void Parse(string output, out string body, out int code)
        {
            const string marker = "__HTTP_CODE__:";
            int idx = output.LastIndexOf(marker, StringComparison.Ordinal);

            body = output;
            code = 0;

            if (idx >= 0)
            {

                body = output.Substring(0, idx).TrimEnd('\r', '\n');
                int.TryParse(output.Substring(idx + marker.Length).Trim(), out code);
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
