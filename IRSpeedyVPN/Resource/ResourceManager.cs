using Ionic.Zip;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Security;
using IRSpeedyVPN.Services.Libcore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows;

namespace IRSpeedyVPN.Resource
{
    public class ResourceManager
    {
        private const string RuntimeMutexName = @"Global\IRSpeedy_RuntimeManager_v1";
        private const string LocalRuntimeMutexName = @"Local\IRSpeedy_RuntimeManager_v1";
        private const string SettingFile = "seed.set";
        private const string StagingFile = ".staging";
        private const string ActiveStateFile = "active.json";
        private const int ProbeReadyTimeoutMs = 8000;
        private const int ProbeExitTimeoutMs = 3000;
        private const uint MoveFileReplaceExisting = 0x1;
        private const uint MoveFileWriteThrough = 0x8;
        private static readonly TimeSpan StagingStaleAge = TimeSpan.FromMinutes(30);

        private readonly TripleDesHelper tdes;
        private readonly object extractionLock = new object();
        private readonly string runtimeRoot;
        private readonly string versionsPath;
        private readonly string statePath;
        private readonly string activeStatePath;
        private readonly string inactivePath;

        public string TempPath { get; set; }
        public string Password { get; set; }
        public event EventHandler onResourceExtracted;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);

        private sealed class RuntimeState
        {
            public string activeHash { get; set; }
            public string previousHash { get; set; }
            public string activatedAtUtc { get; set; }
        }

        public ResourceManager()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "IRSpeedy");
            versionsPath = Path.Combine(runtimeRoot, "versions");
            statePath = Path.Combine(runtimeRoot, "state");
            activeStatePath = Path.Combine(statePath, ActiveStateFile);
            inactivePath = Path.Combine(runtimeRoot, "inactive");

            Directory.CreateDirectory(runtimeRoot);
            Directory.CreateDirectory(versionsPath);
            Directory.CreateDirectory(statePath);
            Directory.CreateDirectory(inactivePath);

            TempPath = inactivePath;
            tdes = new TripleDesHelper(
                0x1F,
                0xaf,
                "b1",
                "8a",
                "aa",
                "d6",
                "ef",
                0xea,
                SysThumbPrint.Value());

            try
            {
                WithRuntimeLock(() =>
                {
                    var state = ValidateAndRepairRuntimeStateLocked();
                    ApplyStateToTempPathLocked(state);
                    CleanupOldVersionsLocked(state);
                    CleanupLegacyRuntimeLocked();
                    MigrateSettingsFileLocked();
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }
        }

        public void ExtractResource(bool W84Exit = false)
        {
            Action extraction = () =>
            {
                var ready = false;
                lock (extractionLock)
                {
                    try
                    {
                        WithRuntimeLock(() =>
                        {
                            ready = EnsureEmbeddedRuntimeLocked();
                        });
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLog(ex);
                        try
                        {
                            WithRuntimeLock(() =>
                            {
                                var state = ValidateAndRepairRuntimeStateLocked();
                                ApplyStateToTempPathLocked(state);
                                ready = IsValidFinalizedRuntimeHash(state.activeHash);
                            });
                        }
                        catch (Exception recoveryEx)
                        {
                            LogHelper.WriteLog(recoveryEx);
                        }
                    }
                }

                if (!W84Exit && ready)
                    onResourceExtracted?.Invoke(this, EventArgs.Empty);
            };

            if (W84Exit)
                extraction();
            else
                Task.Run(extraction);
        }

        public string GetActiveRuntimePath()
        {
            try
            {
                string activePath = null;
                WithRuntimeLock(() =>
                {
                    var state = ValidateAndRepairRuntimeStateLocked();
                    if (IsValidFinalizedRuntimeHash(state.activeHash))
                        activePath = GetVersionPath(state.activeHash);
                });
                return activePath;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                return null;
            }
        }

        public bool TryRollbackActiveRuntime(string failedCorePath)
        {
            if (string.IsNullOrWhiteSpace(failedCorePath))
                return false;

            try
            {
                var rolledBack = false;
                WithRuntimeLock(() =>
                {
                    var state = ValidateAndRepairRuntimeStateLocked();
                    if (!IsValidFinalizedRuntimeHash(state.activeHash)
                        || !IsValidFinalizedRuntimeHash(state.previousHash))
                        return;

                    var activePath = GetVersionPath(state.activeHash);
                    var normalizedCore = Path.GetFullPath(failedCorePath);
                    if (!IsPathInsideRoot(activePath, normalizedCore))
                        return;

                    var failedHash = state.activeHash;
                    state.activeHash = state.previousHash;
                    state.previousHash = null;
                    state.activatedAtUtc = DateTime.UtcNow.ToString("o");
                    WriteRuntimeStateAtomicLocked(state);
                    ApplyStateToTempPathLocked(state);
                    CleanupOldVersionsLocked(state);
                    rolledBack = true;

                    LogHelper.WriteExLog(
                        "Core runtime rollback: failed=" + failedHash
                        + " active=" + state.activeHash + "\r\n");
                });
                return rolledBack;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                return false;
            }
        }

        public void Extract(string toExtractPath, string path = "")
        {
            using (var stream = File.OpenRead(toExtractPath))
                Extract(stream, path);
        }

        public void Extract(Stream toExtractStream, string path = "")
        {
            if (toExtractStream == null)
                throw new ArgumentNullException(nameof(toExtractStream));

            var basePath = string.IsNullOrWhiteSpace(TempPath) ? inactivePath : TempPath;
            var outputPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(basePath, path ?? string.Empty);
            Directory.CreateDirectory(outputPath);

            using (var zip = ZipFile.Read(toExtractStream))
            {
                ValidateEntries(zip);
                ExtractZipEntriesSafely(zip, outputPath);
            }
        }

        internal AccountInfoEx GetConfig()
        {
            try
            {
                MigrateSettingsFile();
                var settingsFile = Path.Combine(statePath, SettingFile);
                var acc = tdes.Decrypt(File.ReadAllBytes(settingsFile))
                    .ToUTF8String()
                    .JsonDeserilize<AccountInfoEx>();
                var userInfo = tdes.Decrypt(RegHelper.GetSettingValue("UserInfo").FromBase64String())
                    .ToUTF8String()
                    .Split('\n');
                if (userInfo.Length < 2
                    || userInfo[0] != acc.UserAccount.Username
                    || (acc.UserAccount.ExpiryDate != null && acc.UserAccount.ExpiryDate.Value < DateTime.Now))
                    return null;

                Password = userInfo[1];
                return acc;
            }
            catch
            {
                return null;
            }
        }

        internal AccountInfoEx GetLocalConfig()
        {
            try
            {
                var acc = File.ReadAllText("./account.txt").JsonDeserilize<AccountInfoEx>();
                if (File.Exists("./oneclick.txt"))
                {
                    var cfgserver = Encoding.UTF8
                        .GetString(Convert.FromBase64String(
                            new System.Net.WebClient().DownloadString(File.ReadAllText("./oneclick.txt"))))
                        .Split('\n');
                    acc.groups.Clear();
                    acc.groups.Add(new Group { title = "OneClick" });
                    var id = 1;
                    foreach (var serverLink in cfgserver)
                    {
                        if (serverLink.StartsWith("vmess:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx
                            {
                                urls = new[] { new Url { url = serverLink } }.ToList(),
                                ID = id++,
                                Country = ((Dictionary<string, object>)(
                                    new JavaScriptSerializer().DeserializeObject(
                                        Encoding.UTF8.GetString(
                                            Convert.FromBase64String(serverLink.Substring(8))))))["ps"].ToString()
                            });
                        }
                        else if (serverLink.StartsWith("trojan:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx
                            {
                                urls = new[] { new Url { url = serverLink } }.ToList(),
                                ID = id++,
                                Country = HttpUtility.UrlDecode(serverLink)
                                    .Substring(serverLink.IndexOf('#'))
                            });
                        }
                    }
                }
                return acc;
            }
            catch
            {
                return null;
            }
        }

        internal void SaveConfig(AccountInfoEx info, string password)
        {
            Directory.CreateDirectory(statePath);
            RegHelper.SetSettingValue(
                "UserInfo",
                tdes.Encrypt(string.Format("{0}\n{1}", info.UserAccount.Username, password).ToUTF8Bytes())
                    .ToBase64String());
            File.WriteAllBytes(
                Path.Combine(statePath, SettingFile),
                tdes.Encrypt(info.JsonSerilize().ToUTF8Bytes()));
        }

        internal void RemoveConfig()
        {
            RegHelper.SetSettingValue("UserInfo", "");
        }

        private bool EnsureEmbeddedRuntimeLocked()
        {
            var state = ValidateAndRepairRuntimeStateLocked();
            var archiveBytes = ReadEmbeddedArchiveAndVerifyHash();
            var versionId = ComputeSha256Hex(archiveBytes);
            var versionDirectory = GetVersionPath(versionId);

            if (string.Equals(state.activeHash, versionId, StringComparison.OrdinalIgnoreCase)
                && IsValidFinalizedRuntime(versionDirectory))
            {
                ApplyStateToTempPathLocked(state);
                CleanupOldVersionsLocked(state);
                CleanupLegacyRuntimeLocked();
                MigrateSettingsFileLocked();
                return true;
            }

            if (!IsValidFinalizedRuntime(versionDirectory))
                PrepareCandidateDirectoryLocked(versionId, versionDirectory, archiveBytes);

            if (!ProbeRuntimeCandidate(versionDirectory, out var probeError))
            {
                SafeDeleteDirectory(versionDirectory);
                throw new InvalidDataException("Extracted Core failed startup probe: " + probeError);
            }

            DeleteFileIfPresent(Path.Combine(versionDirectory, StagingFile));
            if (!IsValidFinalizedRuntime(versionDirectory))
                throw new InvalidDataException("Runtime became invalid after successful probe: " + versionDirectory);

            var previousHash = IsValidFinalizedRuntimeHash(state.activeHash)
                && !string.Equals(state.activeHash, versionId, StringComparison.OrdinalIgnoreCase)
                ? state.activeHash
                : state.previousHash;
            if (!IsValidFinalizedRuntimeHash(previousHash)
                || string.Equals(previousHash, versionId, StringComparison.OrdinalIgnoreCase))
            {
                previousHash = null;
            }

            state = new RuntimeState
            {
                activeHash = versionId,
                previousHash = previousHash,
                activatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            WriteRuntimeStateAtomicLocked(state);
            ApplyStateToTempPathLocked(state);
            CleanupOldVersionsLocked(state);
            CleanupLegacyRuntimeLocked();
            MigrateSettingsFileLocked();

            LogHelper.WriteExLog(
                "Core runtime activated: hash=" + state.activeHash
                + " previous=" + (state.previousHash ?? "")
                + " path=" + TempPath + "\r\n");
            return true;
        }

        private void PrepareCandidateDirectoryLocked(string versionId, string versionDirectory, byte[] archiveBytes)
        {
            if (Directory.Exists(versionDirectory))
            {
                var stagingPath = Path.Combine(versionDirectory, StagingFile);
                if (File.Exists(stagingPath))
                {
                    var ownerPid = ReadStagingPid(stagingPath);
                    var ownedByCurrentProcess = ownerPid == Process.GetCurrentProcess().Id;
                    if (!ownedByCurrentProcess && !IsStagingAbandoned(stagingPath))
                    {
                        throw new IOException(
                            "Runtime extraction is already active for hash " + versionId
                            + " (PID " + ownerPid + ").");
                    }
                }

                SafeDeleteDirectory(versionDirectory);
                if (Directory.Exists(versionDirectory))
                    throw new IOException("Unable to clear incomplete runtime: " + versionDirectory);
            }

            Directory.CreateDirectory(versionDirectory);
            var markerPath = Path.Combine(versionDirectory, StagingFile);
            WriteStagingMarker(markerPath);

            try
            {
                using (var memory = new MemoryStream(archiveBytes, false))
                using (var zip = ZipFile.Read(memory))
                {
                    ValidateEntries(zip);
                    ExtractZipEntriesSafely(zip, versionDirectory);
                }

                if (!IsValidRuntimeFiles(versionDirectory))
                    throw new InvalidDataException("The embedded runtime archive is missing the required Core executable.");
            }
            catch
            {
                // Keep .staging if AV/file locks prevent immediate cleanup. Startup cleanup
                // will remove it once the creator PID is dead or the marker becomes stale.
                throw;
            }
        }

        private byte[] ReadEmbeddedArchiveAndVerifyHash()
        {
            byte[] archiveBytes;
            using (var source = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/Files.zip"))?.Stream)
            {
                if (source == null)
                    throw new FileNotFoundException("Embedded Resources/Files.zip was not found.");
                archiveBytes = ReadAllBytes(source);
            }

            var actualHash = ComputeSha256Hex(archiveBytes);
            var expectedHash = (FilesZipIntegrity.ExpectedSha256 ?? string.Empty).Trim();
            if (!IsSha256(expectedHash))
                throw new InvalidDataException("Build-time Files.zip SHA256 is missing or invalid.");
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Embedded Files.zip SHA256 mismatch. Expected " + expectedHash
                    + ", got " + actualHash + ".");
            }
            return archiveBytes;
        }

        private bool ProbeRuntimeCandidate(string versionDirectory, out string error)
        {
            error = null;
            var corePath = GetCorePath(versionDirectory);
            if (string.IsNullOrWhiteSpace(corePath) || !File.Exists(corePath))
            {
                error = "Core executable is missing: " + (corePath ?? "");
                return false;
            }

            var ready = new ManualResetEventSlim(false);
            var diagnostics = new StringBuilder();
            var diagnosticsLock = new object();
            string probeHost = null;
            var probePort = 0;
            var forcedStop = false;

            using (ready)
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = corePath,
                    Arguments = "--probe-mode -port 0",
                    WorkingDirectory = Path.GetDirectoryName(corePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                DataReceivedEventHandler stdoutHandler = (sender, args) =>
                {
                    if (args.Data == null) return;
                    lock (diagnosticsLock)
                    {
                        diagnostics.AppendLine("[stdout] " + args.Data);
                    }
                    const string marker = "PROBE_READY ";
                    if (!args.Data.StartsWith(marker, StringComparison.Ordinal)) return;
                    var endpoint = args.Data.Substring(marker.Length).Trim();
                    var separator = endpoint.LastIndexOf(':');
                    if (separator <= 0 || separator >= endpoint.Length - 1) return;
                    if (!int.TryParse(endpoint.Substring(separator + 1), out var parsedPort)) return;
                    probeHost = endpoint.Substring(0, separator);
                    probePort = parsedPort;
                    ready.Set();
                };
                DataReceivedEventHandler stderrHandler = (sender, args) =>
                {
                    if (args.Data == null) return;
                    lock (diagnosticsLock)
                    {
                        diagnostics.AppendLine("[stderr] " + args.Data);
                    }
                };
                process.OutputDataReceived += stdoutHandler;
                process.ErrorDataReceived += stderrHandler;

                try
                {
                    if (!process.Start())
                    {
                        error = "Process.Start returned false for probe Core.";
                        return false;
                    }
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!ready.Wait(ProbeReadyTimeoutMs))
                    {
                        if (process.HasExited)
                        {
                            process.WaitForExit();
                            error = "Probe Core exited before PROBE_READY. Exit code: " + process.ExitCode
                                + ". " + ReadDiagnostics(diagnostics, diagnosticsLock);
                        }
                        else
                        {
                            error = "Timed out waiting for PROBE_READY. "
                                + ReadDiagnostics(diagnostics, diagnosticsLock);
                        }
                        TryKillProcess(process);
                        return false;
                    }

                    if (process.HasExited)
                    {
                        process.WaitForExit();
                        error = "Probe Core exited before health RPC. Exit code: " + process.ExitCode
                            + ". " + ReadDiagnostics(diagnostics, diagnosticsLock);
                        return false;
                    }

                    try
                    {
                        var client = new LibcoreServiceClient(probeHost, probePort, 2000);
                        client.IsPrivileged();
                    }
                    catch (Exception ex)
                    {
                        error = "ProtoRPC health call failed: " + ex.Message + ". "
                            + ReadDiagnostics(diagnostics, diagnosticsLock);
                        TryKillProcess(process);
                        return false;
                    }

                    if (!process.WaitForExit(ProbeExitTimeoutMs))
                    {
                        forcedStop = true;
                        TryKillProcess(process);
                    }
                    if (!process.HasExited)
                    {
                        error = "Probe Core did not stop after health RPC.";
                        return false;
                    }

                    process.WaitForExit();
                    if (!forcedStop && process.ExitCode != 0)
                    {
                        error = "Probe Core returned exit code " + process.ExitCode + ". "
                            + ReadDiagnostics(diagnostics, diagnosticsLock);
                        return false;
                    }

                    if (ProtorpcClient.CanConnect(probeHost, probePort, 200))
                    {
                        error = "Probe listener is still accepting connections after probe shutdown.";
                        return false;
                    }
                    return true;
                }
                catch (Win32Exception ex)
                {
                    error = "Probe Core failed to launch: " + ex.Message;
                    TryKillProcess(process);
                    return false;
                }
                finally
                {
                    process.OutputDataReceived -= stdoutHandler;
                    process.ErrorDataReceived -= stderrHandler;
                }
            }
        }

        private RuntimeState ValidateAndRepairRuntimeStateLocked()
        {
            CleanupOrphanStateTempsLocked();

            var state = TryReadRuntimeStateLocked(out var parsed)
                ? parsed
                : new RuntimeState();
            var changed = parsed == null;

            if (!IsSha256(state.activeHash))
            {
                if (!string.IsNullOrWhiteSpace(state.activeHash)) changed = true;
                state.activeHash = null;
            }
            if (!IsSha256(state.previousHash))
            {
                if (!string.IsNullOrWhiteSpace(state.previousHash)) changed = true;
                state.previousHash = null;
            }

            var activeValid = IsValidFinalizedRuntimeHash(state.activeHash);
            var previousValid = IsValidFinalizedRuntimeHash(state.previousHash);

            if (!activeValid)
            {
                if (previousValid)
                {
                    state.activeHash = state.previousHash;
                    state.previousHash = null;
                    state.activatedAtUtc = DateTime.UtcNow.ToString("o");
                    changed = true;
                }
                else
                {
                    if (state.activeHash != null || state.previousHash != null) changed = true;
                    state.activeHash = null;
                    state.previousHash = null;
                }
            }
            else
            {
                if (!previousValid || string.Equals(state.activeHash, state.previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (state.previousHash != null) changed = true;
                    state.previousHash = null;
                }
            }

            if (changed)
                WriteRuntimeStateAtomicLocked(state);

            return state;
        }

        private bool TryReadRuntimeStateLocked(out RuntimeState state)
        {
            state = null;
            if (!File.Exists(activeStatePath))
            {
                state = new RuntimeState();
                return true;
            }

            try
            {
                var json = File.ReadAllText(activeStatePath, Encoding.UTF8);
                state = new JavaScriptSerializer().Deserialize<RuntimeState>(json) ?? new RuntimeState();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
                return false;
            }
        }

        private void WriteRuntimeStateAtomicLocked(RuntimeState state)
        {
            Directory.CreateDirectory(statePath);
            var temporary = Path.Combine(
                statePath,
                ActiveStateFile + ".tmp." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N"));
            var json = new JavaScriptSerializer().Serialize(state ?? new RuntimeState());

            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                    {
                        writer.Write(json);
                        writer.Flush();
                    }
                    stream.Flush(true);
                }

                var lastError = 0;
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    if (MoveFileExW(
                        temporary,
                        activeStatePath,
                        MoveFileReplaceExisting | MoveFileWriteThrough))
                    {
                        return;
                    }

                    lastError = Marshal.GetLastWin32Error();
                    if (attempt < 4)
                        Thread.Sleep(50 * (attempt + 1));
                }
                throw new Win32Exception(lastError, "Atomic active.json replacement failed.");
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private void ApplyStateToTempPathLocked(RuntimeState state)
        {
            TempPath = state != null && IsValidFinalizedRuntimeHash(state.activeHash)
                ? GetVersionPath(state.activeHash)
                : inactivePath;

            try
            {
                if (AppServices.GlobalInfo != null)
                    AppServices.GlobalInfo.TempPath = TempPath;
            }
            catch
            {
            }
        }

        private void CleanupOldVersionsLocked(RuntimeState state)
        {
            Directory.CreateDirectory(versionsPath);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (state != null && IsSha256(state.activeHash)) keep.Add(state.activeHash);
            if (state != null && IsSha256(state.previousHash)) keep.Add(state.previousHash);

            foreach (var directory in Directory.GetDirectories(versionsPath))
            {
                var hash = Path.GetFileName(directory);
                if (keep.Contains(hash))
                    continue;

                var marker = Path.Combine(directory, StagingFile);
                if (File.Exists(marker) && !IsStagingAbandoned(marker))
                    continue;

                SafeDeleteDirectory(directory);
            }
        }

        private void CleanupLegacyRuntimeLocked()
        {
            // Legacy builds extracted Core directly into %TEMP%\IRSpeedy\V-Guard.
            // It is never a valid execution source now; deletion is best effort so a
            // locked old SGuard cannot block activation of a versioned runtime.
            SafeDeleteDirectory(Path.Combine(runtimeRoot, "V-Guard"));
        }

        private void CleanupOrphanStateTempsLocked()
        {
            try
            {
                if (!Directory.Exists(statePath)) return;
                foreach (var file in Directory.GetFiles(statePath, ActiveStateFile + ".tmp.*"))
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private bool IsStagingAbandoned(string markerPath)
        {
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(markerPath);
                if (age >= StagingStaleAge)
                    return true;

                var pid = ReadStagingPid(markerPath);
                if (pid <= 0)
                    return true;
                return !IsProcessAlive(pid);
            }
            catch
            {
                return true;
            }
        }

        private static int ReadStagingPid(string markerPath)
        {
            try
            {
                return int.TryParse(File.ReadAllText(markerPath).Trim(), out var pid) ? pid : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                    return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteStagingMarker(string markerPath)
        {
            using (var stream = new FileStream(
                markerPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                512,
                FileOptions.WriteThrough))
            {
                var bytes = Encoding.ASCII.GetBytes(Process.GetCurrentProcess().Id.ToString());
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private bool IsValidFinalizedRuntimeHash(string hash)
        {
            return IsSha256(hash) && IsValidFinalizedRuntime(GetVersionPath(hash));
        }

        private bool IsValidFinalizedRuntime(string path)
        {
            return IsValidRuntimeFiles(path) && !File.Exists(Path.Combine(path, StagingFile));
        }

        private bool IsValidRuntimeFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;
            var core = GetCorePath(path);
            return !string.IsNullOrWhiteSpace(core) && File.Exists(core);
        }

        private string GetCorePath(string runtimePath)
        {
            if (string.IsNullOrWhiteSpace(runtimePath)) return null;
            var coreName = (Tools.IsWin7OrLower() ? "SGuard7" : "SGuard")
                + (Environment.Is64BitOperatingSystem ? "64.exe" : "32.exe");
            return Path.Combine(runtimePath, "V-Guard", coreName);
        }

        private string GetVersionPath(string hash)
        {
            return Path.Combine(versionsPath, hash);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data ?? new byte[0]))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private void MigrateSettingsFile()
        {
            try
            {
                WithRuntimeLock(MigrateSettingsFileLocked);
            }
            catch
            {
            }
        }

        private void MigrateSettingsFileLocked()
        {
            try
            {
                Directory.CreateDirectory(statePath);
                var destination = Path.Combine(statePath, SettingFile);
                if (File.Exists(destination))
                    return;

                var candidates = new[]
                {
                    Path.Combine(runtimeRoot, SettingFile),
                    string.IsNullOrWhiteSpace(TempPath) ? null : Path.Combine(TempPath, SettingFile)
                };
                var source = candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x));
                if (source != null)
                    File.Copy(source, destination, false);
            }
            catch
            {
            }
        }

        private void WithRuntimeLock(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            using (var mutex = CreateRuntimeMutex())
            {
                var ownsMutex = false;
                var abandoned = false;
                try
                {
                    try
                    {
                        ownsMutex = mutex.WaitOne(TimeSpan.FromMinutes(2));
                        if (!ownsMutex)
                            throw new TimeoutException("Timed out waiting for the IRSpeedy runtime mutex.");
                    }
                    catch (AbandonedMutexException)
                    {
                        // .NET transfers mutex ownership to this thread before throwing.
                        ownsMutex = true;
                        abandoned = true;
                    }

                    if (abandoned)
                    {
                        var recovered = ValidateAndRepairRuntimeStateLocked();
                        ApplyStateToTempPathLocked(recovered);
                        CleanupOldVersionsLocked(recovered);
                    }

                    action();
                }
                finally
                {
                    if (ownsMutex)
                        mutex.ReleaseMutex();
                }
            }
        }

        private static Mutex CreateRuntimeMutex()
        {
            try
            {
                return new Mutex(false, RuntimeMutexName);
            }
            catch (UnauthorizedAccessException)
            {
                // %TEMP% is per-user. Local\ keeps the same serialization guarantee if
                // policy denies creation in the Global namespace for a standard user.
                return new Mutex(false, LocalRuntimeMutexName);
            }
        }

        private static void ExtractZipEntriesSafely(ZipFile zip, string outputPath)
        {
            if (zip == null)
                throw new ArgumentNullException(nameof(zip));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            var root = Path.GetFullPath(outputPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(root);

            foreach (var entry in zip.Entries)
            {
                var entryName = (entry.FileName ?? string.Empty)
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .TrimStart(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(entryName))
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(root, entryName));
                if (!IsPathInsideRoot(root, targetPath))
                    throw new InvalidDataException("Unsafe path in ZIP archive: " + entry.FileName);

                var isDirectory = entry.IsDirectory
                    || entryName.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
                if (isDirectory)
                {
                    DeleteFileIfPresent(targetPath);
                    EnsureDirectoryPath(targetPath, root);
                    continue;
                }

                var parent = Path.GetDirectoryName(targetPath);
                EnsureDirectoryPath(parent, root);

                if (Directory.Exists(targetPath))
                    SafeDeleteDirectory(targetPath);
                if (Directory.Exists(targetPath))
                    throw new IOException("Unable to replace directory with runtime file: " + targetPath);

                DeleteFileIfPresent(targetPath);

                using (var input = entry.OpenReader())
                using (var output = new FileStream(
                    targetPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.SequentialScan))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
            }
        }

        private static void EnsureDirectoryPath(string directoryPath, string root)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            directoryPath = Path.GetFullPath(directoryPath);
            if (!IsPathInsideRoot(root, directoryPath)
                && !string.Equals(directoryPath, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Unsafe directory path while extracting runtime: " + directoryPath);
            }

            if (Directory.Exists(directoryPath))
                return;

            var parent = Path.GetDirectoryName(directoryPath);
            if (!string.IsNullOrWhiteSpace(parent)
                && !string.Equals(parent, directoryPath, StringComparison.OrdinalIgnoreCase)
                && !Directory.Exists(parent))
            {
                EnsureDirectoryPath(parent, root);
            }

            DeleteFileIfPresent(directoryPath);
            Directory.CreateDirectory(directoryPath);
        }

        private static bool IsPathInsideRoot(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (!File.Exists(path))
                return;

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        private static void ValidateEntries(ZipFile zip)
        {
            foreach (var entry in zip.Entries)
            {
                var name = (entry.FileName ?? string.Empty).Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var normalized = name.TrimStart('/');
                if (name.StartsWith("/", StringComparison.Ordinal)
                    || name.Contains(":")
                    || name.Split('/').Any(x => x == "..")
                    || string.Equals(normalized, StagingFile, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Unsafe path in ZIP archive: " + name);
                }
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static string ReadDiagnostics(StringBuilder diagnostics, object diagnosticsLock)
        {
            lock (diagnosticsLock)
                return diagnostics.ToString().Trim();
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process == null || process.HasExited) return;
                process.Kill();
                process.WaitForExit(2000);
            }
            catch
            {
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch { }
                }

                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                    .OrderByDescending(x => x.Length))
                {
                    try { File.SetAttributes(directory, FileAttributes.Normal); }
                    catch { }
                }

                try { File.SetAttributes(path, FileAttributes.Normal); }
                catch { }
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
