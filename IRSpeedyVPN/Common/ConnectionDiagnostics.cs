using System;
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
        internal const string Schema = "network-core-v1";
        private static readonly string Session = Guid.NewGuid().ToString("N");
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly byte[] Salt = Guid.NewGuid().ToByteArray();
        private static Timer timer;
        private static int started, sampling, requested;
        private static long addressEvents, availabilityEvents;
        private static int available = -1;
        private static long events, lastEventMs = -1, revision, snapshotAt = -1;
        private static string previous = "", snapshot = "pending";
        internal static long EventSequence => Interlocked.Read(ref events);

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
                    try { timer?.Dispose(); NetworkChange.NetworkAddressChanged -= AddressChanged;
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
            try { LogHelper.WriteLog("[ConnectionDiagnostic] stage=" + stage + Context + " " + fields); } catch { }
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
            finally { Interlocked.Exchange(ref sampling, 0); }
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
