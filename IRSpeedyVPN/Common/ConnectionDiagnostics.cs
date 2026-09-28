using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using IRSpeedyVPN.Resource;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.Common
{
    // Observational only: no socket probes, DNS lookups, route mutations or reconnects.
    internal static class ConnectionDiagnostics
    {
        internal const string Schema = "network-core-v2";
        private static readonly string Session = Guid.NewGuid().ToString("N");
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly byte[] Salt = Guid.NewGuid().ToByteArray();
        private static Timer timer;
        private static readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
        private static readonly object writerGate = new object();
        private static int writing, queued, lost;
        private static long recordSequence;
        private static Func<string> activeState;
        private static string lastRuntime = "";
        private static long lastRuntimeAt = -30000;
        internal static void SetActiveStateReader(Func<string> reader) { Volatile.Write(ref activeState, reader); }
        internal static string ActiveState
        {
            get { try { return Volatile.Read(ref activeState)?.Invoke() ?? "activeService=none"; }
                catch (Exception ex) { return "activeReadError=" + ex.GetType().Name; } }
        }
        internal static string Caller()
        {
            try { return string.Join(">", new StackTrace(false).GetFrames().Select(f => f.GetMethod())
                .Where(m => m != null && m.DeclaringType != typeof(ConnectionDiagnostics))
                .Take(10).Select(m => m.DeclaringType?.Name + "." + m.Name)); }
            catch { return "unavailable"; }
        }
        private static int started, sampling, requested;
        private static long addressEvents, availabilityEvents;
        private static int available = -1;
        private static long events, lastEventMs = -1, revision, snapshotAt = -1;
        private static string previous = "", snapshot = "pending";
        internal static long EventSequence => Interlocked.Read(ref events);
        internal static bool MonitorStarted => Volatile.Read(ref started) != 0;
        private static string observedMode = "unknown";
        private static long observedModeAt = -1;
        internal static string ObservedMode => Volatile.Read(ref observedMode);
        internal static string SelectedMode
        {
            get
            {
                try { return RegHelper.GetSettingValue("VGAURDVPNMode") == "0" ? "Proxy" : "TUN"; }
                catch { return "unknown"; }
            }
        }
        internal static long ObservedModeAgeMs => Interlocked.Read(ref observedModeAt) < 0 ? -1
            : Clock.ElapsedMilliseconds - Interlocked.Read(ref observedModeAt);
        internal static void ObserveMode(string mode)
        {
            Volatile.Write(ref observedMode, mode);
            Interlocked.Exchange(ref observedModeAt, Clock.ElapsedMilliseconds);
        }

        internal static void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            try
            {
                Write("session", "schema=" + Schema + " appVersion=" + Assembly.GetExecutingAssembly().GetName().Version
                    + " appMvid=" + Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId
                    + " appPid=" + Process.GetCurrentProcess().Id + " process64=" + Environment.Is64BitProcess);
                NetworkChange.NetworkAddressChanged += AddressChanged;
                NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
                timer = new Timer(Sample, null, 0, 2000);
                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    try { Flush(); timer?.Dispose(); NetworkChange.NetworkAddressChanged -= AddressChanged;
                        NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged; } catch { }
                };
            }
            catch (Exception ex) { Write("monitor-unavailable", "exception=" + ex.GetType().Name); }
        }
        private static void AddressChanged(object sender, EventArgs e) { Interlocked.Increment(ref addressEvents); MarkEvent(); }
        private static void AvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) { Interlocked.Increment(ref availabilityEvents); Interlocked.Exchange(ref available, e.IsAvailable ? 1 : 0); MarkEvent(); }
        private static void MarkEvent()
        {
            Interlocked.Increment(ref events);
            Interlocked.Exchange(ref lastEventMs, Clock.ElapsedMilliseconds);
            Write("network-event", ActiveState);
            RequestSnapshot();
        }
        internal static void RequestSnapshot() { Interlocked.Exchange(ref requested, 1); }
        internal static string Context
        {
            get
            {
                long at = Interlocked.Read(ref snapshotAt), ev = Interlocked.Read(ref lastEventMs);
                return " session=" + Session + " monoMs=" + Clock.ElapsedMilliseconds
                    + " netEventSeq=" + EventSequence + " addressEvents=" + Interlocked.Read(ref addressEvents)
                    + " availabilityEvents=" + Interlocked.Read(ref availabilityEvents) + " available=" + Volatile.Read(ref available) + " netRevision=" + Interlocked.Read(ref revision)
                    + " snapshotAgeMs=" + (at < 0 ? -1 : Clock.ElapsedMilliseconds - at)
                    + " lastNetEventAgeMs=" + (ev < 0 ? -1 : Clock.ElapsedMilliseconds - ev);
            }
        }
        internal static void Write(string stage, string fields)
        {
            try
            {
                // Capture event time/context before queueing; disk IO never blocks UI/core readers.
                string line = "[ConnectionDiagnostic] stage=" + stage + Context
                    + " recordSeq=" + Interlocked.Increment(ref recordSequence)
                    + " eventUtc=" + DateTime.UtcNow.ToString("O")
                    + " thread=" + Thread.CurrentThread.ManagedThreadId + " " + fields;
                if (Interlocked.Increment(ref queued) > 4096)
                { Interlocked.Decrement(ref queued); Interlocked.Increment(ref lost); return; }
                pending.Enqueue(line);
                if (Interlocked.CompareExchange(ref writing, 1, 0) == 0)
                    ThreadPool.QueueUserWorkItem(_ => Drain());
            }
            catch { }
        }
        internal static void Flush() { Drain(); }
        private static void Drain()
        {
            lock (writerGate)
            {
                try
                {
                    string line;
                    while (pending.TryDequeue(out line))
                    { Interlocked.Decrement(ref queued); try { LogHelper.WriteLog(line); } catch { } }
                    int dropped = Interlocked.Exchange(ref lost, 0);
                    if (dropped > 0) try { LogHelper.WriteLog("[ConnectionDiagnostic] stage=log-overflow dropped=" + dropped); } catch { }
                }
                finally
                {
                    Interlocked.Exchange(ref writing, 0);
                    if (!pending.IsEmpty && Interlocked.CompareExchange(ref writing, 1, 0) == 0)
                        ThreadPool.QueueUserWorkItem(_ => Drain());
                }
            }
        }
        internal static string Fingerprint(string value)
        {
            try
            {
                using (var hash = new HMACSHA256(Salt))
                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")), 0, 8).Replace("-", "").ToLowerInvariant();
            }
            catch { return "unavailable"; }
        }
        private static void Sample(object state)
        {
            if (Interlocked.Exchange(ref sampling, 1) != 0) return;
            try
            {
                bool force = Interlocked.Exchange(ref requested, 0) != 0;
                long eventSequence = EventSequence;
                var rows = NetworkInterface.GetAllNetworkInterfaces().Select(n =>
                {
                    try
                    {
                        var p = n.GetIPProperties();
                        int index = -1;
                        try { index = p.GetIPv4Properties()?.Index ?? -1; } catch { }
                        return "if=" + index + ":id=" + Fingerprint(n.Id) + ":type=" + n.NetworkInterfaceType
                            + ":state=" + n.OperationalStatus
                            + ":addr=" + Fingerprint(string.Join(";", p.UnicastAddresses.Select(a => a.Address.ToString()).OrderBy(a => a)))
                            + ":gw=" + Fingerprint(string.Join(";", p.GatewayAddresses.Select(a => a.Address.ToString()).OrderBy(a => a)))
                            + ":dns=" + Fingerprint(string.Join(";", p.DnsAddresses.Select(a => a.ToString()).OrderBy(a => a)));
                    }
                    catch (Exception ex) { return "interface-read-error=" + ex.GetType().Name; }
                }).OrderBy(r => r).ToArray();
                string next = "interfaces={" + string.Join("|", rows) + "} ipv4Defaults={" + DefaultRoutes() + "} ipv6RouteTable=not-sampled";
                bool changed = next != previous;
                if (changed) { previous = next; Interlocked.Increment(ref revision); }
                snapshot = Fingerprint(next);
                Interlocked.Exchange(ref snapshotAt, Clock.ElapsedMilliseconds);
                if (changed || force)
                    Write("network-snapshot", "snapshot=" + snapshot + " observedEventSeq=" + eventSequence
                        + " changed=" + changed + " " + next);
            }
            catch (Exception ex) { Write("snapshot-error", "exception=" + ex.GetType().Name); }
            finally { SampleRuntime(false); Interlocked.Exchange(ref sampling, 0); }
        }
        private static void SampleRuntime(bool force)
        {
            try
            {
                string next = ActiveState + " " + ProxySnapshot() + " " + CoreDiagnosticMetadata.ProcessState(19810)
                    + " " + CoreDiagnosticMetadata.ListenerState();
                bool changed = next != lastRuntime;
                if (changed || force || Clock.ElapsedMilliseconds - lastRuntimeAt >= 15000)
                {
                    Write("runtime-snapshot", "changed=" + changed + " " + next);
                    lastRuntime = next; lastRuntimeAt = Clock.ElapsedMilliseconds;
                }
            }
            catch (Exception ex) { Write("runtime-snapshot-error", "exception=" + ex.GetType().Name); }
        }
        internal static string ProxySnapshot()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    string server = Convert.ToString(key?.GetValue("ProxyServer"));
                    string pac = Convert.ToString(key?.GetValue("AutoConfigURL"));
                    return "registryProxyEnable=" + Convert.ToString(key?.GetValue("ProxyEnable", -1))
                        + " proxyServerId=" + Fingerprint(server) + " proxyLoopback=" + IsLoopbackProxy(server)
                        + " pacPresent=" + !string.IsNullOrEmpty(pac) + " pacId=" + Fingerprint(pac)
                        + " bypassId=" + Fingerprint(Convert.ToString(key?.GetValue("ProxyOverride")))
                        + " " + QueryWinInet();
                }
            }
            catch (Exception ex) { return "proxyReadError=" + ex.GetType().Name; }
        }
        internal static bool IsLoopbackProxy(string value)
        {
            return Regex.IsMatch(value ?? "", @"\A(?:http://)?127\.0\.0\.1:[0-9]{1,5}\z");
        }
        [StructLayout(LayoutKind.Explicit)] private struct OptionValue
        { [FieldOffset(0)] public int Number; [FieldOffset(0)] public IntPtr Text; [FieldOffset(0)] public long FileTime; }
        [StructLayout(LayoutKind.Sequential)] private struct ProxyOption { public int Id; public OptionValue Value; }
        [StructLayout(LayoutKind.Sequential)] private struct ProxyList
        { public int Size; public IntPtr Connection; public int Count, Error; public IntPtr Options; }
        [DllImport("wininet.dll", EntryPoint="InternetQueryOptionW", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InternetQueryOption(IntPtr handle, int option, ref ProxyList list, ref int size);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
        private static string QueryWinInet()
        {
            int stride = Marshal.SizeOf(typeof(ProxyOption));
            IntPtr memory = Marshal.AllocHGlobal(stride * 2);
            try
            {
                Marshal.StructureToPtr(new ProxyOption { Id = 1 }, memory, false);
                Marshal.StructureToPtr(new ProxyOption { Id = 2 }, IntPtr.Add(memory, stride), false);
                var list = new ProxyList { Size = Marshal.SizeOf(typeof(ProxyList)), Count = 2, Options = memory };
                int size = list.Size;
                if (!InternetQueryOption(IntPtr.Zero, 75, ref list, ref size))
                    return "winInetQueryError=" + Marshal.GetLastWin32Error();
                var flags = (ProxyOption)Marshal.PtrToStructure(memory, typeof(ProxyOption));
                var server = (ProxyOption)Marshal.PtrToStructure(IntPtr.Add(memory, stride), typeof(ProxyOption));
                return "winInetFlags=" + flags.Value.Number + " winInetServerId=" + Fingerprint(Marshal.PtrToStringUni(server.Value.Text));
            }
            finally
            {
                var server = (ProxyOption)Marshal.PtrToStructure(IntPtr.Add(memory, stride), typeof(ProxyOption));
                if (server.Value.Text != IntPtr.Zero) GlobalFree(server.Value.Text);
                Marshal.FreeHGlobal(memory);
            }
        }
        internal static IDisposable ProxyMutation(string operation)
        { return new ProxyChange(operation); }
        private sealed class ProxyChange : IDisposable
        {
            private readonly string id = Guid.NewGuid().ToString("N");
            private readonly Stopwatch clock = Stopwatch.StartNew();
            internal ProxyChange(string operation)
            {
                Start();
                Write("proxy-change-begin", "operationId=" + id + " operation=" + operation
                    + " caller=" + Caller() + " " + ActiveState + " " + ProxySnapshot());
            }
            public void Dispose()
            {
                Write("proxy-change-end", "operationId=" + id + " elapsedMs=" + clock.ElapsedMilliseconds
                    + " " + ActiveState + " " + ProxySnapshot());
                RequestSnapshot();
            }
        }
        // Unknown tokens are removed, rather than trusting a blacklist to catch every credential format.
        // Keep diagnostic vocabulary and error signatures; never write raw configs/URLs/destinations.
        private static readonly HashSet<string> CoreWords = new HashSet<string>(("error warn warning info debug panic fatal runtime goroutine unexpected EOF timeout timed out context deadline exceeded canceled cancelled connection connect connecting network changed reset closed closing close refused aborted broken pipe no recent activity handshake failed failure tls certificate verify verification invalid unknown authority x509 quic hysteria hysteria2 vless vmess trojan shadowsocks xray sing-box tcp udp dns lookup read write dial accept listen bind socket address already in use cannot assign requested not available interface route tun inbound outbound proxy direct server client transport remote host forcibly was an existing by the is a to of on for from and or with without i/o operation shutdown exit signal access denied permission unreachable unreachable-host no such device resource temporarily unavailable too many open files all outbound failed no available server empty balancer leastload no qualified outbound wsarecv wsasend connectex windows system call returned success started stopped start stop network is down network is unreachable invalid argument connection refused connection reset by peer broken pipe the semaphore timeout period has expired network name is no longer available").Split(' '), StringComparer.OrdinalIgnoreCase);
        internal static string SafeCoreText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "empty";
            if (text.Length > 4096) text = text.Substring(0, 4096);
            text = Regex.Replace(text, @"[a-zA-Z][a-zA-Z0-9+.-]*://\S+|[\w.+-]+@[\w.-]+|[\w.-]+\.[a-zA-Z]{2,}[^\s]*", " REDACTED ");
            text = Regex.Replace(text, @"(?i)(password|token|authorization|uuid|secret|user|username|id|key)\s*[:=]\s*[^\s,;]+", " REDACTED ");
            var tokens = Regex.Matches(text, @"[a-zA-Z][a-zA-Z0-9_/-]*|[^\sA-Za-z]+").Cast<Match>()
                .Select(m => CoreWords.Contains(m.Value) ? m.Value : "[redacted]");
            return Regex.Replace(string.Join(" ", tokens), @"(?:\[redacted\] ?){2,}", "[redacted] ").Trim();
        }
        // Read-only IPv4 default route table, including route metrics. No gateway/address plaintext.
        private static string DefaultRoutes()
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                int status = GetIpForwardTable(IntPtr.Zero, ref size, false);
                if (status != 122 || size < 4 || size > 4 * 1024 * 1024) return "query-status=" + status;
                buffer = Marshal.AllocHGlobal(size);
                status = GetIpForwardTable(buffer, ref size, false);
                if (status != 0) return "read-status=" + status;
                int count = Math.Min(Marshal.ReadInt32(buffer), (size - 4) / 56);
                var routes = new System.Collections.Generic.List<string>();
                for (int i = 0; i < count; i++)
                {
                    int offset = 4 + i * 56;
                    int destination = Marshal.ReadInt32(buffer, offset), mask = Marshal.ReadInt32(buffer, offset + 4);
                    bool defaultRoute = destination == 0 && mask == 0;
                    bool splitDefault = (destination == 0 || destination == 128) && mask == 128;
                    if (!defaultRoute && !splitDefault) continue;
                    routes.Add("prefix=" + (defaultRoute ? "default" : destination == 0 ? "lower-half" : "upper-half") + ":if=" + Marshal.ReadInt32(buffer, offset + 16)
                        + ":metric=" + Marshal.ReadInt32(buffer, offset + 36)
                        + ":gw=" + Fingerprint(Marshal.ReadInt32(buffer, offset + 12).ToString()));
                }
                return string.Join("|", routes.OrderBy(r => r));
            }
            catch (Exception ex) { return "unavailable=" + ex.GetType().Name; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }
        [DllImport("iphlpapi.dll")]
        private static extern int GetIpForwardTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order);
    }
}
