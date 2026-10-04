using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.SingBox;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static int passed;
    private static readonly TimeZoneLocation Finland = new TimeZoneLocation { IanaId = "Europe/Helsinki", WindowsId = "FLE Standard Time" };
    private static TimeZoneSnapshot Home() => new TimeZoneSnapshot
    {
        WindowsId = "Iran Standard Time", DaylightSavingDisabled = true,
        SerializedLocalZone = TimeZoneInfo.CreateCustomTimeZone("Home", TimeSpan.FromMinutes(210), "Home", "Home").ToSerializedString()
    };
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private sealed class SystemFake : ITimeZoneSystem
    {
        internal readonly List<string> Events;
        internal int Applied, Restored;
        internal bool FailApply, FailRestore;
        internal TimeZoneSnapshot RestoredSnapshot;
        internal SystemFake(List<string> events) { Events = events; }
        public TimeZoneSnapshot Capture() { Events.Add("capture"); return Home(); }
        public void Apply(string id) { Events.Add("apply:" + id); Applied++; if (FailApply) throw new InvalidOperationException("apply"); }
        public void Restore(TimeZoneSnapshot snapshot)
        { Events.Add("restore"); Restored++; if (FailRestore) throw new InvalidOperationException("restore"); RestoredSnapshot = snapshot; }
    }
    private sealed class JournalFake : ITimeZoneJournal
    {
        internal readonly List<string> Events;
        internal TimeZoneSnapshot Saved;
        internal bool FailWrite, FailClear;
        internal JournalFake(List<string> events) { Events = events; }
        public TimeZoneSnapshot Read() { Events.Add("read"); return Saved; }
        public void Write(TimeZoneSnapshot snapshot) { Events.Add("write"); if (FailWrite) throw new IOException("write"); Saved = snapshot; }
        public void Clear() { Events.Add("clear"); if (FailClear) throw new IOException("clear"); Saved = null; }
    }
    private sealed class LookupFake : ITimeZoneLookup
    {
        internal int Calls, Port;
        internal Func<CancellationToken, Task<TimeZoneLocation>> Resolve = t => Task.FromResult(Finland);
        public Task<TimeZoneLocation> ResolveAsync(int port, CancellationToken token) { Calls++; Port = port; return Resolve(token); }
    }
    private sealed class Fixture
    {
        internal readonly object Owner = new object();
        internal readonly List<string> Events = new List<string>();
        internal readonly SystemFake System;
        internal readonly JournalFake Journal;
        internal readonly LookupFake Lookup = new LookupFake();
        internal readonly VpnTimeZoneSession Session;
        internal Fixture()
        { System = new SystemFake(Events); Journal = new JournalFake(Events); Session = new VpnTimeZoneSession(System, Journal, Lookup); Session.BeginConnection(Owner); }
        internal Task Enable() => Session.EnableAsync(Owner, 7788);
    }

    private static async Task Main()
    {
        MappingAndNetwork();
        await SessionBehavior();
        await Races();
        await FailuresAndRecovery();
        FileJournalAndNativeLayout();
        Routing();
        Console.WriteLine("Time-zone checks passed: " + passed + "; Windows mutation and WPF rendering are not executed here.");
    }
    private static void MappingAndNetwork()
    {
        var expected = new Dictionary<string, string>
        {
            ["Europe/Helsinki"] = "FLE Standard Time", ["Europe/Kyiv"] = "FLE Standard Time",
            ["Europe/Kiev"] = "FLE Standard Time", ["Asia/Tehran"] = "Iran Standard Time",
            ["Asia/Kolkata"] = "India Standard Time", ["Asia/Calcutta"] = "India Standard Time",
            ["America/Los_Angeles"] = "Pacific Standard Time", ["US/Pacific"] = "Pacific Standard Time",
            ["Etc/UTC"] = "UTC", ["Europe/London"] = "GMT Standard Time"
        };
        foreach (var pair in expected) Check(TimeZoneMapping.ToWindows(pair.Key) == pair.Value, "CLDR zone/alias " + pair.Key);
        Throws<InvalidDataException>(() => TimeZoneMapping.ToWindows("Unknown/Zone"));
        var valid = "{\"success\":true,\"ip\":\"8.8.8.8\",\"timezone\":{\"id\":\"Europe/Helsinki\"}}";
        Check(TimeZoneLookup.Parse(valid).WindowsId == Finland.WindowsId, "one GeoIP response provides both public exit IP and zone");
        Check(TimeZoneLookup.Parse(valid.Replace("8.8.8.8", "2001:4860:4860::8888")).IanaId == Finland.IanaId, "public IPv6 exit accepted");
        foreach (var ip in new[] { "127.0.0.1", "10.0.0.1", "192.168.0.1", "172.16.0.1", "169.254.1.1", "100.64.0.1", "0.0.0.0", "224.0.0.1", "::1", "::ffff:192.168.1.1", "fd00::1", "fe80::1", "bad" })
            Throws<InvalidDataException>(() => TimeZoneLookup.Parse(valid.Replace("8.8.8.8", ip)));
        Throws<InvalidDataException>(() => TimeZoneLookup.Parse(valid.Replace("true", "false")));
        Throws<InvalidDataException>(() => TimeZoneLookup.Parse(valid.Replace("Europe/Helsinki", "Unknown/Zone")));
        Throws<InvalidDataException>(() => TimeZoneLookup.Parse(new string('x', 16385)));
        Throws<Newtonsoft.Json.JsonReaderException>(() => TimeZoneLookup.Parse("{\"nested\":" + new string('[', 16) + "0" + new string(']', 16) + "}"));
        Check(true, "private/invalid exits, failed lookup, unknown zones and oversized responses rejected");
        using (var handler = TimeZoneLookup.CreateHandler(7788))
        {
            var target = new Uri("https://ipwho.is/");
            Check(handler.UseProxy && handler.Proxy.GetProxy(target).AbsoluteUri == "http://127.0.0.1:7788/"
                && !handler.Proxy.IsBypassed(target) && !handler.AllowAutoRedirect && !handler.UseCookies,
                "explicit loopback HTTP proxy without redirects or direct bypass");
        }
        Throws<ArgumentOutOfRangeException>(() => TimeZoneLookup.CreateHandler(0));
        Throws<ArgumentOutOfRangeException>(() => TimeZoneLookup.CreateHandler(65536));
    }
    private static async Task SessionBehavior()
    {
        var f = new Fixture();
        Check(f.Lookup.Calls == 0 && f.System.Applied == 0, "beginning a VPN session never looks up or changes time zone");
        await f.Enable();
        Check(f.Lookup.Port == 7788 && f.Session.Active.IanaId == Finland.IanaId, "click uses current connection listener and enables zone");
        Check(f.Events.IndexOf("capture") < f.Events.IndexOf("write") && f.Events.IndexOf("write") < f.Events.IndexOf("apply:FLE Standard Time"), "original zone is durably journaled before Windows mutation");
        await f.Enable(); f.Session.BeginConnection(f.Owner);
        Check(f.Lookup.Calls == 1 && f.System.Applied == 1 && f.System.Restored == 0, "Round-robin exits and same-connection recovery keep chosen zone fixed");
        var before = DateTime.UtcNow.AddMinutes(210); var accountNow = f.Session.AccountNow; var after = DateTime.UtcNow.AddMinutes(210);
        Check(accountNow >= before && accountNow <= after, "subscription time stays in saved home zone while VPN zone is active");
        f.Session.EndConnection(new object());
        Check(f.Session.Active != null && f.System.Restored == 0, "cleanup from a different service cannot end the current time-zone session");
        f.Session.Disable();
        Check(f.System.RestoredSnapshot.WindowsId == "Iran Standard Time" && f.System.RestoredSnapshot.DaylightSavingDisabled
            && f.Journal.Saved == null && f.Session.Active == null && ReferenceEquals(f.Owner, f.Session.Connection), "second click restores original zone and DST preference while VPN remains connected");
        await f.Enable(); f.Session.EndConnection(f.Owner);
        Check(f.Lookup.Calls == 2 && f.System.Restored == 2 && f.Session.Connection == null && f.Journal.Saved == null, "reenable uses a new lookup and disconnect restores then clears recovery journal");
        await Throws<OperationCanceledException>(() => f.Enable());
        Check(f.Lookup.Calls == 2, "disconnected click cannot start a lookup");
    }
    private static async Task Races()
    {
        var f = new Fixture();
        var late = new TaskCompletionSource<TimeZoneLocation>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Lookup.Resolve = t => late.Task; // Deliberately ignore cancellation to test the generation guard.
        var oldRequest = f.Enable();
        await Throws<InvalidOperationException>(() => f.Enable());
        var timer = System.Diagnostics.Stopwatch.StartNew(); f.Session.EndConnection();
        Check(timer.ElapsedMilliseconds < 500 && !oldRequest.IsCompleted && f.Session.Connection == null, "disconnect does not wait for a blocked GeoIP request");
        f.Session.BeginConnection(f.Owner); // Same service instance is commonly reused on reconnect.
        var newReply = new TaskCompletionSource<TimeZoneLocation>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Lookup.Resolve = t => newReply.Task;
        var newRequest = f.Enable(); late.SetResult(Finland);
        await Throws<OperationCanceledException>(() => oldRequest);
        Check(f.System.Applied == 0 && !newRequest.IsCompleted, "late reply from previous connection cannot mutate Windows or cancel the new request");
        newReply.SetResult(Finland); await newRequest;
        Check(f.System.Applied == 1, "new connection can enable after stale reply is discarded");
        f.Session.EndConnection();

        var disabled = new Fixture(); var reply = new TaskCompletionSource<TimeZoneLocation>();
        disabled.Lookup.Resolve = t => reply.Task;
        var pending = disabled.Enable(); disabled.Session.Disable(); reply.SetResult(Finland);
        await Throws<OperationCanceledException>(() => pending);
        Check(disabled.System.Applied == 0 && disabled.Journal.Saved == null, "cancelled activation cannot apply a late result");
    }
    private static async Task FailuresAndRecovery()
    {
        var lookup = new Fixture(); lookup.Lookup.Resolve = t => Task.FromException<TimeZoneLocation>(new IOException("network"));
        await Throws<IOException>(() => lookup.Enable());
        Check(lookup.System.Applied == 0 && lookup.Journal.Saved == null, "failed network lookup leaves Windows unchanged");
        var write = new Fixture(); write.Journal.FailWrite = true;
        await Throws<IOException>(() => write.Enable());
        Check(write.System.Applied == 0, "recovery journal failure prevents any Windows mutation");
        var apply = new Fixture(); apply.System.FailApply = true;
        await Throws<InvalidOperationException>(() => apply.Enable());
        Check(apply.System.Restored == 1 && apply.Journal.Saved == null && apply.Session.Active == null, "failed Windows apply rolls back and clears journal after restoration");
        var restore = new Fixture(); await restore.Enable(); restore.System.FailRestore = true;
        Throws<InvalidOperationException>(() => restore.Session.EndConnection());
        Check(restore.Session.Connection == null && restore.Journal.Saved != null, "failed restore invalidates connection while keeping the recoverable baseline");
        restore.System.FailRestore = false; restore.Session.Recover();
        Check(restore.Journal.Saved == null && restore.Session.Active == null, "failed restoration can be retried without losing original settings");
        var clear = new Fixture(); await clear.Enable(); clear.Journal.FailClear = true;
        Throws<IOException>(() => clear.Session.Disable()); clear.Journal.FailClear = false; clear.Session.Disable();
        Check(clear.Journal.Saved == null && clear.Session.Active == null && clear.System.RestoredSnapshot.DaylightSavingDisabled, "journal cleanup failure remains retryable");
        var crash = new Fixture(); await crash.Enable();
        var nextProcess = new VpnTimeZoneSession(crash.System, crash.Journal, new LookupFake()); nextProcess.Recover();
        Check(crash.System.Restored == 1 && crash.Journal.Saved == null, "next application launch recovers original settings after abrupt termination");
    }
    private static void FileJournalAndNativeLayout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "irspeedy-time-zone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new TimeZoneJournal(Path.Combine(directory, "recovery.json"));
            Check(journal.Read() == null, "missing recovery file is a normal first launch");
            journal.Write(Home()); var saved = journal.Read();
            Check(saved.WindowsId == Home().WindowsId && saved.DaylightSavingDisabled && saved.SerializedLocalZone == Home().SerializedLocalZone, "real journal round-trips zone, disabled DST and serialized home zone");
            saved.DaylightSavingDisabled = false; journal.Write(saved);
            Check(!journal.Read().DaylightSavingDisabled && Directory.GetFiles(directory).Length == 1, "atomic replacement leaves no temporary files");
            journal.Clear(); Check(journal.Read() == null, "successful restoration removes recovery file");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        var type = typeof(WindowsTimeZoneSystem);
        Check(Marshal.SizeOf(type.GetNestedType("DynamicZone", BindingFlags.NonPublic)) == 432
            && Marshal.SizeOf(type.GetNestedType("TokenPrivileges", BindingFlags.NonPublic)) == 16,
            "native Windows time-zone and token privilege layouts match Win32 ABI");
    }
    private static void Routing()
    {
        var original = JObject.Parse(@"{
            'inbounds':[{'type':'mixed','tag':'mixed-in'},{'type':'http','tag':'sharing-http'},{'type':'tun','tag':'tun-in'}],
            'outbounds':[{'type':'auto-selector','tag':'proxy'},{'type':'auto-selector','tag':'ai-proxy'},{'type':'direct','tag':'direct'}],
            'route':{'final':'direct','rules':[{'action':'hijack-dns'},{'process_path':['IRSpeedyVPN.exe'],'outbound':'direct'},
                {'domain_suffix':['openai.com'],'outbound':'ai-proxy'},{'domain_suffix':['youtube.com'],'outbound':'vod-proxy'}]}
        }");
        var result = JObject.Parse(VpnTimeZoneRouting.Apply(original.ToString()));
        var route = (JObject)result["route"]; var rules = (JArray)route["rules"]; var first = (JObject)rules[0];
        Check((string)first["outbound"] == "proxy" && (string)first["network"] == "tcp" && (int)first["port"] == 443
            && first["domain"].Values<string>().SequenceEqual(new[] { TimeZoneLookup.Host })
            && first["inbound"].Values<string>().SequenceEqual(new[] { "mixed-in", "sharing-http" }),
            "HTTPS GeoIP request through explicit listeners always selects main pool");
        rules.RemoveAt(0);
        Check(JToken.DeepEquals(route, original["route"]) && JToken.DeepEquals(result["outbounds"], original["outbounds"]),
            "AI/VOD/process exclusions, Game Mode final and existing pool membership are preserved");
        Throws<InvalidOperationException>(() => VpnTimeZoneRouting.Apply("{'inbounds':[],'route':{'rules':[]}}"));
    }
}
