using IRSpeedy.Hotspot;

internal static class Program
{
    private static int passed;
    private static readonly Guid Tun = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Private = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Physical = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Other = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static async Task Main()
    {
        await Test("opt-in required before writes", async () =>
        {
            var (s, b, j) = New();
            await Reject("experimental-opt-in-required", () => s.Start(Tun, "Test", "12345678", false));
            Check(b.Starts == 0 && j.Value is null);
        });
        foreach (var password in new[] { "short", new string('x', 64), "1234567\n", "1234567é" })
            await Test("reject invalid passphrase", async () =>
                await Reject("invalid-passphrase", () => New().s.Start(Tun, "Test", password, true)));
        foreach (var ssid in new[] { "", "   ", new string('x', 33), new string('ش', 17), "bad\nssid" })
            await Test("reject invalid SSID", async () =>
                await Reject("invalid-ssid", () => New().s.Start(Tun, ssid, "12345678", true)));
        await Test("valid UTF8 SSID at 32 bytes", async () =>
        {
            var (s, _, _) = New(); await s.Start(Tun, new string('ش', 16), "12345678", true); await s.Stop();
        });
        await Test("physical upstream rejected", async () =>
        {
            var (s, b, _) = New();
            await Reject("active-irspeedy-tun-required", () => s.Start(Physical, "Test", "12345678", true));
            Check(b.Starts == 0);
        });
        await Test("down TUN rejected", async () =>
        {
            var (s, b, _) = New(); b.Set(Tun, a => a with { Up = false });
            await Reject("active-irspeedy-tun-required", () => s.Start(Tun, "Test", "12345678", true));
        });
        await Test("renamed TUN rejected", async () =>
        {
            var (s, b, _) = New(); b.Set(Tun, a => a with { Name = "Ethernet" });
            await Reject("active-irspeedy-tun-required", () => s.Start(Tun, "Test", "12345678", true));
        });
        await Test("existing ICS preserved", async () =>
        {
            var (s, b, j) = New(); b.Set(Physical, a => a with { SharingRole = 0 });
            await Reject("existing-ics-conflict", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.Stops == 0 && b.Starts == 0 && j.Value is null && b.Adapters.Single(a => a.Id == Physical).SharingRole == 0);
        });
        await Test("existing hotspot preserved", async () =>
        {
            var (s, b, _) = New(); b.IsOn = true;
            await Reject("existing-hotspot-conflict", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.Stops == 0);
        });
        await Test("missing TUN WinRT profile is not replaced by physical profile", async () =>
        {
            var (s, b, j) = New(); b.FailPrepare = true;
            await Reject("tun-winrt-profile-unavailable", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.Starts == 0 && j.Value is null);
        });
        await Test("happy path durable journal before mutation, verified pair, idempotent stop", async () =>
        {
            var (s, b, j) = New(); await s.Start(Tun, "Test", "12345678", true);
            Check(s.Active && s.Healthy() && j.Value?.PrivateId == Private);
            await s.Stop(); await s.Stop(); Check(!s.Active && j.Value is null && b.Stops == 1);
        });
        await Test("repeat start rejected", async () =>
        {
            var (s, b, _) = New(); await s.Start(Tun, "Test", "12345678", true);
            await Reject("recovery-required", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.Starts == 1); await s.Stop();
        });
        await Test("journal failure prevents start", async () =>
        {
            var (s, b, j) = New(); j.FailWrite = true;
            await Reject("journal-write-failed", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.Starts == 0);
        });
        await Test("partial start rolls back", async () =>
        {
            var (s, b, j) = New(); b.FailStart = true;
            await Reject("start-failed", () => s.Start(Tun, "Test", "12345678", true));
            Check(!s.Active && !b.IsOn && j.Value is null && b.Stops == 1);
        });
        await Test("bind failure rolls back", async () =>
        {
            var (s, b, j) = New(); b.FailBind = true;
            await Reject("bind-failed", () => s.Start(Tun, "Test", "12345678", true));
            Check(j.Value is null && !b.IsOn);
        });
        await Test("verification failure is not reported as success", async () =>
        {
            var (s, b, j) = New(); b.SkipBind = true;
            await Reject("ics-verification-failed", () => s.Start(Tun, "Test", "12345678", true));
            Check(!s.Active && j.Value is null);
        });
        await Test("failed rollback retains journal", async () =>
        {
            var (s, b, j) = New(); b.FailStart = true; b.FailStop = true;
            await Reject("start-failed-recovery-required", () => s.Start(Tun, "Test", "12345678", true));
            Check(j.Value?.StartAttempted == true);
            await Reject("recovery-required", () => s.Start(Tun, "Test", "12345678", true));
        });
        await Test("recovery across helper instance", async () =>
        {
            var (s, b, j) = New(); await s.Start(Tun, "Test", "12345678", true);
            var recovered = new Session(b, j); await recovered.Stop();
            Check(j.Value is null && !b.IsOn);
        });
        await Test("missing profile during recovery retains journal", async () =>
        {
            var (s, b, j) = New(); await s.Start(Tun, "Test", "12345678", true); b.FailPrepare = true;
            await Reject("tun-winrt-profile-unavailable", () => new Session(b, j).Stop());
            Check(j.Value is not null);
        });
        await Test("adapter disappearance detected", async () =>
        {
            var (s, b, _) = New(); await s.Start(Tun, "Test", "12345678", true);
            b.Set(Tun, a => a with { Up = false }); Check(!s.Healthy()); await s.Stop();
        });
        await Test("upstream takeover detected and not disabled by rollback", async () =>
        {
            var (s, b, j) = New(); await s.Start(Tun, "Test", "12345678", true);
            b.Set(Physical, a => a with { SharingRole = 0 }); Check(!s.Healthy());
            await Reject("cleanup-verification-failed", () => s.Stop());
            Check(j.Value is not null && b.Adapters.Single(a => a.Id == Physical).SharingRole == 0);
        });
        await Test("ambiguous private adapters rejected", async () =>
        {
            var (s, b, _) = New(); b.AmbiguousPrivate = true;
            await Reject("ambiguous-hotspot-adapter", () => s.Start(Tun, "Test", "12345678", true));
        });
        await Test("unknown journal version rejected", async () =>
        {
            var (s, b, j) = New(); j.Value = new Journal(2, Tun, Private, true);
            await Reject("invalid-recovery-journal", () => s.Stop()); Check(b.Stops == 0);
        });
        await Test("WinRT-ready pair avoids all Bind writes", async () =>
        {
            var (s, b, _) = New();
            b.OnRead = x => { x.Set(Tun, a => a with { SharingRole = 0 }); x.Set(Private, a => a with { SharingRole = 1 }); };
            await s.Start(Tun, "Test", "12345678", true);
            Check(s.Active && b.BindCalls == 0); await s.Stop();
        });
        await Test("delayed private adapter and WinRT roles settle without Bind", async () =>
        {
            var (s, b, _) = New(); int reads = 0;
            b.OnRead = x =>
            {
                reads++;
                x.Set(Private, a => a with { Up = reads >= 3, SharingRole = reads >= 3 ? 1 : null });
                if (reads >= 3) x.Set(Tun, a => a with { SharingRole = 0 });
            };
            await s.Start(Tun, "Test", "12345678", true);
            Check(b.BindCalls == 0 && reads == 3 && s.Active); await s.Stop();
        });
        await Test("bounded settle performs exactly one fallback bind", async () =>
        {
            var (s, b, _) = New(); await s.Start(Tun, "Test", "12345678", true);
            Check(b.BindCalls == 1 && s.Observations.Count(x => x.Phase == "winrt-settle") == 17);
            await s.Stop();
        });
        await Test("bind verification waits for delayed private role", async () =>
        {
            var (s, b, _) = New(); b.SkipBind = true; int reads = 0;
            b.OnRead = x => { if (x.BindCalls > 0 && ++reads >= 3) x.Set(Private, a => a with { SharingRole = 1 }); };
            await s.Start(Tun, "Test", "12345678", true);
            Check(s.Active && b.BindCalls == 1 && reads == 3); await s.Stop();
        });
        await Test("bind error preserves pre-cleanup roles", async () =>
        {
            var (s, b, _) = New(); b.FailBind = true;
            try { await s.Start(Tun, "Test", "12345678", true); throw new Exception("Expected failure"); }
            catch (HotspotException ex) when (ex.Code == "bind-failed")
            {
                var snapshots = (SharingObservation[])ex.Data["hotspot.observations"]!;
                Check(snapshots.Last().Phase == "failure-before-cleanup" &&
                    snapshots.Last().Adapters.Any(a => a.Id == Tun && a.Role == 0));
                Check(b.Adapters.All(a => a.SharingRole is null) && !s.Active && b.BindCalls == 1);
            }
        });
        await Test("missing private adapter times out without ICS writes", async () =>
        {
            var (s, b, _) = New(); b.OnRead = x => x.Set(Private, a => a with { Up = false });
            await Reject("hotspot-adapter-not-ready", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.BindCalls == 0 && b.Stops == 1);
        });
        await Test("TUN lost during settling aborts without Bind", async () =>
        {
            var (s, b, _) = New(); b.OnRead = x => x.Set(Tun, a => a with { Up = false });
            await Reject("active-irspeedy-tun-required", () => s.Start(Tun, "Test", "12345678", true));
            Check(b.BindCalls == 0 && !b.IsOn);
        });
        Console.WriteLine($"PASS: {passed} hotspot safety checks; no Windows/network mutation performed.");
    }

    private static (Session s, FakeBackend b, MemoryJournal j) New()
    {
        var j = new MemoryJournal(); var b = new FakeBackend(j); return (new Session(b, j, () => Task.CompletedTask), b, j);
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); } catch (HotspotException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected rejection: " + code);
    }
    private static async Task Test(string name, Func<Task> action)
    { await action(); passed++; Console.WriteLine("PASS " + name); }

    private sealed class MemoryJournal : IJournal
    {
        public Journal? Value;
        public bool FailWrite;
        public Journal? Read() => Value;
        public void Write(Journal state) { if (FailWrite) throw new HotspotException("journal-write-failed"); Value = state; }
        public void Clear() => Value = null;
    }
    private sealed class FakeBackend(MemoryJournal journal) : IHotspotBackend
    {
        public List<Adapter> Adapters = [new(Tun, "irspeedy-tun", "Wintun", true, null),
            new(Private, "Local Area Connection* 1", "Microsoft Wi-Fi Direct Virtual Adapter", false, null),
            new(Physical, "Wi-Fi", "Wi-Fi", true, null)];
        public bool FailPrepare, FailStart, FailStop, FailBind, SkipBind, AmbiguousPrivate;
        public int Starts, Stops, BindCalls;
        public Action<FakeBackend>? OnRead;
        public bool IsOn { get; set; }
        public uint ClientCount => 0;
        public IReadOnlyList<Adapter> ReadAdapters() { if (IsOn) OnRead?.Invoke(this); return Adapters.ToArray(); }
        public void Set(Guid id, Func<Adapter, Adapter> update) => Adapters = Adapters.Select(a => a.Id == id ? update(a) : a).ToList();
        public void Prepare(Guid id) { Check(id == Tun); if (FailPrepare) throw new HotspotException("tun-winrt-profile-unavailable"); }
        public Task Start(string ssid, string password)
        {
            Check(journal.Value?.StartAttempted == true); Starts++; IsOn = true;
            Set(Private, a => a with { Up = true });
            if (AmbiguousPrivate) Adapters.Add(new(Other, "Other", "Microsoft Wi-Fi Direct Virtual Adapter #2", true, null));
            if (FailStart) throw new HotspotException("start-failed");
            return Task.CompletedTask;
        }
        public Task Stop()
        {
            if (FailStop) throw new HotspotException("stop-failed");
            Stops++; IsOn = false; return Task.CompletedTask;
        }
        public void Bind(Guid publicId, Guid privateId)
        {
            BindCalls++;
            Check(publicId == Tun && privateId == Private && journal.Value?.PrivateId == Private);
            Set(Tun, a => a with { SharingRole = 0 });
            if (FailBind) throw new HotspotException("bind-failed");
            if (!SkipBind) Set(Private, a => a with { SharingRole = 1 });
        }
        public void Disable(Guid id, int expectedRole)
        {
            Check(id == Tun || id == Private);
            Set(id, a => a with { SharingRole = null });
        }
    }
}
