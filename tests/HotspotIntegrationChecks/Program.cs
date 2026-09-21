using IRSpeedyVPN.Services.Hotspot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

class Source : IHotspotSource
{
    internal TunContext Context = new TunContext { Id = Guid.NewGuid(), Pid = 42, StartedUtcTicks = 123, Generation = 1 };
    public TunContext CaptureTun() => Context;
}
class Channel : IHotspotChannel
{
    internal Action OnStart, OnStop;
    internal bool FailStop, FailPoll, CoreLost;
    internal int Starts, Stops;
    internal Guid Bound;
    public void Start(TunContext tun, string ssid, string password) { Starts++; Bound = tun.Id; OnStart?.Invoke(); }
    public int Poll() { if (FailPoll) throw new HotspotChannelException("session-health-or-lease-lost", CoreLost); return 2; }
    public void Stop() { Stops++; OnStop?.Invoke(); if (FailStop) throw new Exception(); }
    public void Dispose() { }
}
class Program
{
    static int checks;
    static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); checks++; }
    static void Main()
    {
        Check(HotspotCoordinator.ValidPassword("0000000000"), "leading zeros preserved");
        Check(HotspotCoordinator.ValidPassword("1234567890"), "ten digits");
        foreach (var bad in new[] { "123456789", "12345678901", "۱۲۳۴۵۶۷۸۹۰", "12345a7890", "123456789\n", null })
            Check(!HotspotCoordinator.ValidPassword(bad), "reject non-ASCII/length");
        var source = new Source();
        var channels = new List<Channel>();
        HotspotCoordinator c = null;
        c = new HotspotCoordinator(() => {
            var channel = new Channel { OnStart = () => {
                Check(c.View.State == "starting", "not active before startup completes");
                Check(c.View.Ssid == "IRSPEEDY-TEST" && c.View.Password == "0123456789",
                    "credentials available before startup completes");
            } };
            channels.Add(channel); return channel;
        });
        c.Start(source, "IRSPEEDY-TEST", "0123456789");
        Check(c.View.State == "active", "start verified");
        c.Poll(); Check(c.View.Clients == 2, "client count");
        var old = source.Context;
        Check(c.Pause(source, true, () => source.Context = null), "pause before core mutation");
        Check(channels[0].Stops == 1 && c.View.State == "paused", "pause drains ICS");
        Check(c.View.Password == "", "hide credentials while paused");
        c.Pause(source, true); // Reentrant lifecycle hooks must not lose resume intent.
        source.Context = new TunContext { Id = Guid.NewGuid(), Pid = 43, StartedUtcTicks = 456, Generation = 2 };
        c.Resume(source);
        Check(channels.Count == 2 && channels[1].Bound == source.Context.Id && channels[1].Bound != old.Id, "fresh GUID on silent reconnect");
        Check(c.View.Password == "0123456789", "stable credentials on rebind");
        c.Pause(source, false); c.Resume(source);
        Check(c.View.State == "off" && channels.Count == 2, "explicit disconnect cancels resume");
        c.Start(source, "IRSPEEDY-TEST", "0123456789");
        c.Pause(new Source(), false);
        Check(c.View.State == "active", "unrelated service cannot stop owner");
        channels[2].FailStop = true;
        Check(!c.Pause(source, true) && c.View.Error == "cleanup-not-confirmed", "failed cleanup blocks restart");
        c.Resume(source); Check(channels.Count == 3, "no auto resume after cleanup failure");
        channels[2].FailStop = false; c.Stop(); Check(c.View.State == "off", "explicit cleanup retry");
        c.Start(source, "IRSPEEDY-TEST", "0123456789");
        channels[3].FailPoll = true; c.Poll();
        Check(c.View.State == "error" && c.View.Password == "" && channels[3].Stops == 1, "watchdog failure stops and hides credentials");
        c.Resume(source); Check(channels.Count == 4, "no retry loop on watchdog failure");
        var changed = new HotspotCoordinator(() => new Channel { OnStart = () => source.Context = null });
        changed.Start(source, "IRSPEEDY-TEST", "0123456789");
        Check(changed.View.State == "error", "post-start TUN revalidation");
        var absent = new HotspotCoordinator(() => throw new Exception("must not launch"));
        absent.Start(source, "IRSPEEDY-TEST", "0123456789");
        Check(absent.View.Error == "tun-required", "no helper launch in proxy/disconnected mode");
        source = new Source();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var ordered = new List<string>();
        var racing = new HotspotCoordinator(() => new Channel {
            OnStart = () => { entered.Set(); release.Wait(); }, OnStop = () => ordered.Add("cleanup") });
        var startTask = Task.Run(() => racing.Start(source, "IRSPEEDY-TEST", "0123456789"));
        if (!entered.Wait(3000)) throw new Exception("start not entered");
        var pauseTask = Task.Run(() => { racing.Pause(source, true, () => source.Context = null); ordered.Add("core-stop"); });
        release.Set(); Task.WaitAll(startTask, pauseTask);
        Check(string.Join(",", ordered) == "cleanup,core-stop", "concurrent start drained before RPC stop");
        Check(racing.View.State == "paused", "resume intent survives atomic invalidation");
        source = new Source();
        var shared = new HotspotCoordinator(() => new Channel());
        shared.Start(source, "IRSPEEDY-TEST", "0123456789");
        var nextService = new Source();
        shared.Pause(nextService, true, sharedCore: true);
        Check(shared.CoreChanging && shared.View.State == "off", "another service using same RPC core drains old owner");
        bool refused = false;
        try { shared.Start(source, "IRSPEEDY-TEST", "0123456789"); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused, "no start during shared-core mutation");
        shared.Resume(nextService);
        Check(!shared.CoreChanging && shared.View.State == "off", "new service readiness does not revive old sharing intent");
        shared.Start(nextService, "IRSPEEDY-TEST", "0123456789");
        shared.Pause(nextService, true, () => nextService.Context = null, sharedCore: true);
        shared.Resume(nextService);
        Check(shared.CoreChanging && shared.View.State == "paused", "stale resume cannot clear core-change barrier");
        nextService.Context = new TunContext { Id = Guid.NewGuid(), Pid = 55, StartedUtcTicks = 900 };
        shared.Resume(nextService);
        Check(shared.View.State == "active" && !shared.CoreChanging, "successful reconnect reopens start barrier");
        shared.Pause(nextService, true, () => nextService.Context = null, sharedCore: true);
        shared.Ready(nextService); shared.Poll();
        Check(shared.View.State == "paused", "RPC readiness waits for adapter enumeration");
        nextService.Context = new TunContext { Id = Guid.NewGuid(), Pid = 55, StartedUtcTicks = 900 };
        shared.Poll();
        Check(shared.View.State == "active", "delayed adapter rebinds on background poll");
        shared.Stop();
        source = new Source();
        var lateChannels = new List<Channel>();
        var lateHook = new HotspotCoordinator(() => { var x = new Channel(); lateChannels.Add(x); return x; });
        lateHook.Start(source, "IRSPEEDY-TEST", "0123456789");
        source.Context = null; lateHook.Poll();
        Check(lateHook.View.State == "paused", "core loss before lifecycle hook retains reconnect intent");
        lateHook.Pause(source, true, sharedCore: true);
        source.Context = new TunContext { Id = Guid.NewGuid(), Pid = 99, StartedUtcTicks = 1000 };
        lateHook.Ready(source); lateHook.Poll();
        Check(lateHook.View.State == "active" && lateChannels.Count == 2, "late reconnect hook still rebinds fresh session");
        lateChannels[1].FailPoll = true; lateChannels[1].CoreLost = true; lateHook.Poll();
        Check(lateHook.View.State == "paused", "helper-reported core loss also preserves intent");
        lateHook.Poll(); Check(lateChannels.Count == 2, "health failure alone never restarts sharing");
        lateHook.Stop();
        Console.WriteLine($"{checks} hotspot integration checks passed.");
    }
}
