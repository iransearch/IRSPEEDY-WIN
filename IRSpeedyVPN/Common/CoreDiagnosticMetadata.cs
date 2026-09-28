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
        internal static int ProxyPort = 10808;
        internal static string ProcessState(int port)
        {
            try
            {
                lock (Gate)
                {
                    Process p;
                    if (!Processes.TryGetValue(port, out p)) return "trackedCore=none";
                    bool exited = p.HasExited;
                    return "trackedCorePid=" + p.Id + " trackedCoreExited=" + exited
                        + (exited ? " trackedExitCode=" + p.ExitCode + " trackedExitHex=" + unchecked((uint)p.ExitCode).ToString("X8") : "");
                }
            }
            catch (Exception ex) { return "trackedCoreError=" + ex.GetType().Name; }
        }
        internal static string ListenerState()
        {
            return TcpState(2) + " " + TcpState(23);
        }
        private static string TcpState(int family)
        {
            IntPtr buffer = IntPtr.Zero;
            string label = family == 2 ? "tcp4" : "tcp6";
            try
            {
                int size = 0;
                uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
                if (status != 122 || size < 4 || size > 8 * 1024 * 1024) return label + "QueryStatus=" + status;
                buffer = Marshal.AllocHGlobal(size);
                status = GetExtendedTcpTable(buffer, ref size, false, family, 5, 0);
                if (status != 0) return label + "ReadStatus=" + status;
                int stride = family == 2 ? 24 : 56;
                int count = Math.Min(Marshal.ReadInt32(buffer), (size - 4) / stride);
                int pid = Pid(19810), proxyPort = System.Threading.Volatile.Read(ref ProxyPort);
                var listeners = new List<string>();
                var states = new Dictionary<int,int>();
                for (int i = 0; i < count; i++)
                {
                    int offset = 4 + i * stride;
                    int state = Marshal.ReadInt32(buffer, offset + (family == 2 ? 0 : 48));
                    int owner = Marshal.ReadInt32(buffer, offset + stride - 4);
                    int portOffset = offset + (family == 2 ? 8 : 20);
                    int port = Marshal.ReadByte(buffer, portOffset) * 256 + Marshal.ReadByte(buffer, portOffset + 1);
                    if (state == 2 && (port == 19810 || port == proxyPort))
                    {
                        // The bind address is crucial (loopback versus wildcard), never log remote endpoints.
                        byte[] bytes = new byte[family == 2 ? 4 : 16];
                        Marshal.Copy(IntPtr.Add(buffer, offset + (family == 2 ? 4 : 0)), bytes, 0, bytes.Length);
                        var address = new System.Net.IPAddress(bytes);
                        string bind = System.Net.IPAddress.IsLoopback(address) ? "loopback"
                            : bytes.All(b => b == 0) ? "any" : "other";
                        listeners.Add("port:" + port + ":pid:" + owner + ":bind:" + bind);
                    }
                    if (owner == pid) { int value; states.TryGetValue(state, out value); states[state] = value + 1; }
                }
                return label + "Listeners={" + string.Join("|", listeners.OrderBy(x=>x))
                    + "} " + label + "CoreStates={" + string.Join("|", states.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value)) + "}";
            }
            catch (Exception ex) { return label + "Error=" + ex.GetType().Name; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
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
                        "test-begin", "test-end", "probe-begin", "probe-end", "pool-probes-ready" }.Contains(value); break;
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
