using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Common
{
    internal sealed class CoreRuntimeIdentity
    {
        internal const string ErrorPrefix = "[CoreFailure]\n";
        internal const string ConnectionFailureMessage = "خطا در اتصال به سرویس";

        internal static string UserConnectionError(string error)
            => string.IsNullOrWhiteSpace(error) || error == "سروری یافت نشد" || error == "سرور یافت نشد" || error == "Connection canceled."
                ? string.Empty : ConnectionFailureMessage;
        internal string Version = "unknown", Xray = "unknown", Commit = "unknown", Modified = "unknown",
            Architecture = "unknown", GoVersion = "unknown", Path = "unknown", Sha256 = "unknown", Privileged = "unknown";
        internal int Pid = -1;

        internal static string Token(string value)
            => !string.IsNullOrEmpty(value) && value.Length <= 128
                && Regex.IsMatch(value, @"\A[A-Za-z0-9.+_/-]+\z") ? value : "unknown";

        internal static CoreRuntimeIdentity Capture(CoreBuildInfo info, int listenerPid)
        {
            var result = new CoreRuntimeIdentity();
            // Never attach RPC metadata to a different listener's executable.
            if (info != null && info.ProcessId > 0 && listenerPid > 0 && info.ProcessId != listenerPid)
                return result;
            if (info != null)
            {
                result.Version = Token(info.SingBoxVersion); result.Xray = Token(info.XrayVersion);
                result.Commit = Token(info.Commit); result.Modified = Token(info.Modified);
                result.Architecture = Token(info.Architecture); result.GoVersion = Token(info.GoVersion);
                result.Privileged = info.HasPrivilege ? "true" : "false";
            }
            result.Pid = info != null && info.ProcessId > 0 ? info.ProcessId : listenerPid;
            if (result.Pid <= 0) return result;
            try
            {
                using (var process = Process.GetProcessById(result.Pid))
                {
                    var startTime = process.StartTime;
                    var path = process.MainModule.FileName;
                    result.Path = path;
                    using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var hash = SHA256.Create())
                        result.Sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (process.HasExited || process.StartTime != startTime) return new CoreRuntimeIdentity();
                    result.Path = path;
                    if (result.Architecture == "unknown") result.Architecture = ReadPeArchitecture(path);
                }
            }
            catch { /* Access denied is diagnostic unavailability, not a connection failure. */ }
            return result;
        }

        private static string ReadPeArchitecture(string path)
        {
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt16() != 0x5a4d) return "unknown";
                    r.BaseStream.Position = 0x3c;
                    int offset = r.ReadInt32();
                    if (offset < 0 || offset > r.BaseStream.Length - 6) return "unknown";
                    r.BaseStream.Position = offset;
                    if (r.ReadUInt32() != 0x00004550) return "unknown";
                    switch (r.ReadUInt16())
                    {
                        case 0x8664: return "windows/amd64";
                        case 0x014c: return "windows/386";
                        case 0xaa64: return "windows/arm64";
                        default: return "unknown";
                    }
                }
            }
            catch { return "unknown"; }
        }

        internal string Details => "App: " + typeof(CoreRuntimeIdentity).Assembly.GetName().Version.ToString(3)
            + "\nCore (sing-box): " + Version + "\nXray: " + Xray
            + "\nCore commit: " + Commit + "\nSource modified: " + Modified
            + "\nArchitecture: " + Architecture + "\nGo: " + GoVersion + "\nCore privileged: " + Privileged
            + "\nPID: " + Pid + "\nExecutable: " + Path + "\nSHA256: " + Sha256;

        internal string Failure(string error)
        {
            // Core errors can contain a complete config with credentials. Show a
            // fixed summary here; the existing detailed error log is independent.
            string summary = (error ?? "").IndexOf("unknown outbound type: auto-selector-round-robin", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Core اجراشده با Pool سازگار نیست."
                : error == "startup-timeout" ? "مهلت شروع Core پایان یافت."
                : error == "rpc-transport-failure" ? "ارتباط با Core هنگام شروع قطع شد."
                : "شروع سرویس اتصال با خطا مواجه شد.";
            return ErrorPrefix + summary + "\n\n" + Details;
        }
    }
}

