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
        // Set once a launch has failed with "file not found", so the remaining requests
        // go straight to the managed stack instead of paying a failed CreateProcess each.
        private static volatile bool _curlUnavailable;
        private static int _diagnosticSequence;

        /// <summary>
        /// True once curl has been found missing. Callers use it to skip work that only
        /// helps the curl path, such as resolving the host over DoH to pin it with
        /// --resolve, which the managed fallback cannot honour anyway.
        /// </summary>
        internal static bool IsUnavailable
        {
            get { return _curlUnavailable; }
        }

        public string CurlExePath { get; set; } = GetDefaultCurlPath();

        private static string GetDefaultCurlPath()
        {
            foreach (string candidate in CurlCandidates())
            {
                try
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }

            // Last resort: let CreateProcess search PATH. On Windows 7 and 8.1 there is
            // usually nothing to find, which is what HttpFallback is for.
            return "curl";
        }

        /// <summary>
        /// Full paths are preferred over bare "curl" because a damaged user PATH is one
        /// of the ways this went wrong in the field.
        /// </summary>
        private static string[] CurlCandidates()
        {
            string system = null;
            string windows = null;
            try
            {
                system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }
            catch
            {
            }

            return new[]
            {
                // Windows ships curl.exe from Windows 10 1803 onwards. This app builds as
                // x86, so on 64-bit Windows SpecialFolder.System is SysWOW64 and the file
                // system redirector sends a System32 path there too - Sysnative is the
                // only way a 32-bit process reaches the real System32.
                system == null ? null : Path.Combine(system, "curl.exe"),
                windows == null ? null : Path.Combine(windows, "Sysnative", "curl.exe"),

                // Kept for a build that ships its own copy; nothing populates it today.
                Path.Combine(Path.GetTempPath(), "IRSpeedy", "curl", "curl.exe"),
            };
        }

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

            if (_curlUnavailable)
            {
                WriteDiagnostics(diagnostic, "managed-fallback: curl was marked unavailable", null);
                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
            }

            string output;
            try
            {
                output = ShellExecute.ShellexecAndReturnStringOutput(
                    CurlExePath, BuildArgs(url, method, headers, body, proxy, timeoutSeconds, resolveOverride));
            }
            catch (Win32Exception ex)
            {
                // ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND: there is no curl.exe on
                // this machine, which is the normal state on Windows 7 and 8.1. Every
                // later request skips the launch entirely.
                if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
                {
                    _curlUnavailable = true;
                    LogHelper.WriteExLog(
                        "curl.exe is unavailable (" + CurlExePath + "); using the managed HTTP client instead.");
                }

                WriteDiagnostics(diagnostic, "curl launch failed; using managed fallback", ex);

                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
            }
            catch (Exception ex)
            {
                // Something else stopped the process from running - this once. Serve the
                // request from the managed stack without writing curl off for good.
                WriteDiagnostics(diagnostic, "curl launch failed; using managed fallback", ex);
                return HttpFallback.Send(url, method, headers, body, proxy, timeoutSeconds);
            }

            Parse(output, out string responseBody, out int httpCode);
            diagnostic.HttpCode = httpCode;
            WriteDiagnostics(diagnostic, "curl completed", null);
            return new CurlResponse(responseBody, httpCode);
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
            internal int PathEntryCount;
            internal int? HttpCode;
            internal readonly List<string> Candidates = new List<string>();
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
                    ResolvedNow = "<diagnostic capture failed>"
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
                ConfiguredPath = CurlExePath
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

            var candidates = new List<string>();
            AddCandidate(candidates, result.ConfiguredPath);

            // The embedded runtime is versioned. Record both conventional locations and
            // every curl.exe actually present below the active directory, so the log
            // proves where Files.zip placed the executable without assuming its layout.
            AddRuntimeCandidates(candidates, result.ActiveRuntimePath, result, "resource-manager");

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AddCandidate(candidates, Path.Combine(baseDirectory, "curl", "curl.exe"));
            AddCandidate(candidates, Path.Combine(baseDirectory, "curl.exe"));

            foreach (string candidate in CurlCandidates())
                AddCandidate(candidates, candidate);

            result.PathCurlMatch = AddPathMatch(candidates, result);

            foreach (string candidate in candidates)
            {
                result.Candidates.Add(DescribeCandidate(candidate));
                if (result.ResolvedNow == null && IsExistingFile(candidate))
                    result.ResolvedNow = candidate;
            }

            if (result.ResolvedNow == null)
                result.ResolvedNow = "<none; bare curl would rely on CreateProcess search>";

            return result;
        }

        private static string AddPathMatch(
            List<string> candidates,
            CurlDiagnostics diagnostic)
        {
            string path = null;
            try
            {
                path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrWhiteSpace(path))
                    return "<none>";

                foreach (string rawEntry in path.Split(Path.PathSeparator))
                {
                    string entry = rawEntry == null ? null : rawEntry.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(entry))
                        continue;

                    diagnostic.PathEntryCount++;
                    string candidate = Path.Combine(entry, "curl.exe");
                    if (!IsExistingFile(candidate))
                        continue;

                    AddCandidate(candidates, candidate);
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                return "<search failed: " + ex.GetType().FullName + ": " + ex.Message + ">";
            }

            return "<none>";
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

        private static void AddRuntimeCandidates(
            List<string> candidates,
            string runtimePath,
            CurlDiagnostics diagnostic,
            string source)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
                return;

            try
            {
                AddCandidate(candidates, Path.Combine(runtimePath, "curl", "curl.exe"));
                AddCandidate(candidates, Path.Combine(runtimePath, "curl.exe"));

                if (!Directory.Exists(runtimePath))
                    return;

                foreach (string discovered in Directory.GetFiles(
                    runtimePath, "curl.exe", SearchOption.AllDirectories))
                {
                    AddCandidate(candidates, discovered);
                }
            }
            catch (Exception ex)
            {
                diagnostic.Candidates.Add(source + "-runtime-search error="
                    + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private static void AddCandidate(List<string> candidates, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            foreach (string existing in candidates)
            {
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            candidates.Add(candidate);
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
                    .Append(" PATHEntriesChecked=").Append(diagnostic.PathEntryCount);

                if (diagnostic.HttpCode.HasValue)
                    message.Append(" httpCode=").Append(diagnostic.HttpCode.Value);

                message.Append("\r\nCandidates:");
                foreach (string candidate in diagnostic.Candidates)
                    message.Append("\r\n - ").Append(candidate);

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
