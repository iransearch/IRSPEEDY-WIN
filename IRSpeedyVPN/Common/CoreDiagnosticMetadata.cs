using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    internal static class CoreDiagnosticMetadata
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Process> Processes = new Dictionary<int, Process>();
        internal static void Register(int port, Process process)
        {
            try { lock (Gate) Processes[port] = process; } catch { }
        }
        internal static int Pid(int port)
        {
            try
            {
                lock (Gate)
                {
                    Process process;
                    if (Processes.TryGetValue(port, out process) && !process.HasExited) return process.Id;
                }
            }
            catch { }
            return -1;
        }
        // Read-only lookup: never grants lifecycle ownership to another service.
        internal static void ObserveListener(int port)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (Pid(port) > 0) return;
                int size = 0;
                if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0) != 122 || size < 4 || size > 4 * 1024 * 1024) return;
                buffer = Marshal.AllocHGlobal(size);
                if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0) return;
                int count = Math.Min(Marshal.ReadInt32(buffer), (size - 4) / 24);
                for (int i = 0; i < count; i++)
                {
                    int offset = 4 + i * 24;
                    int localPort = Marshal.ReadByte(buffer, offset + 8) * 256 + Marshal.ReadByte(buffer, offset + 9);
                    int address = Marshal.ReadInt32(buffer, offset + 4);
                    if (Marshal.ReadInt32(buffer, offset) != 2 || localPort != port || (address != 0 && address != 0x0100007f)) continue;
                    Register(port, Process.GetProcessById(Marshal.ReadInt32(buffer, offset + 20)));
                    return;
                }
            }
            catch { }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }
        [DllImport("iphlpapi.dll")]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
            int family, int tableClass, uint reserved);

        internal static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")), 0, 8).Replace("-", "").ToLowerInvariant();
        }
        internal static bool TryParse(string line, out string safe)
        {
            safe = null;
            const string prefix = "[CoreDiagnostic] ";
            if (line == null || line.Length > 1024 || !line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var fields = new Dictionary<string, string>();
            foreach (string part in line.Substring(prefix.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int split = part.IndexOf('=');
                if (split <= 0) return false;
                string key = part.Substring(0, split), value = part.Substring(split + 1);
                if (fields.ContainsKey(key)) return false;
                bool valid;
                switch (key)
                {
                    case "schema": valid = value == "core-network-v2"; break;
                    case "event": valid = new[] { "hy2-created", "hy2-reset", "hy2-close", "box-created", "box-start", "box-close",
                        "default-interface", "rpc-start", "rpc-stop", "rpc-stoptest", "tests-cancel", "test-environment", "test-return",
                        "test-begin", "test-end", "probe-begin", "probe-end" }.Contains(value); break;
                    case "reason": valid = new[] { "interface-update", "power-event", "network-manager-reset", "other-caller", "outbound-close", "stop-test" }.Contains(value); break;
                    case "context": valid = new[] { "active", "canceled", "deadline" }.Contains(value); break;
                    case "error": valid = new[] { "none", "network-changed", "canceled", "timeout", "closed", "other", "no-result" }.Contains(value); break;
                    case "current": valid = value == "true" || value == "false"; break;
                    case "tag": case "config": valid = Regex.IsMatch(value, @"\A[0-9a-f]{16}\z"); break;
                    case "pid": case "seq": case "monoMs": case "box": case "dropped": case "outbound": case "index":
                    case "flags": case "test": case "count": case "timeoutMs": case "elapsedMs":
                        long number; valid = long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
                        if (valid) value = number.ToString(CultureInfo.InvariantCulture);
                        break;
                    default: return false;
                }
                if (!valid) return false;
                fields.Add(key, value);
            }
            if (!new[] { "schema", "event", "pid", "seq", "box" }.All(fields.ContainsKey)) return false;
            safe = string.Join(" ", fields.Select(p => p.Key + "=" + p.Value));
            return true;
        }
    }
}
