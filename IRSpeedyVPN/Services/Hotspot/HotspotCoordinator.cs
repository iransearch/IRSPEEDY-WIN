using System;

namespace IRSpeedyVPN.Services.Hotspot
{
    internal sealed class TunContext
    {
        internal Guid Id;
        internal int Pid;
        internal long StartedUtcTicks;
        internal long Generation;
        internal bool Same(TunContext other) => other != null && Id == other.Id && Pid == other.Pid
            && StartedUtcTicks == other.StartedUtcTicks && Generation == other.Generation;
    }
    internal interface IHotspotSource { TunContext CaptureTun(); }
    internal interface IHotspotChannel : IDisposable
    {
        void Start(TunContext tun, string ssid, string password);
        int Poll();
        void Stop();
    }
    internal sealed class HotspotView
    {
        internal readonly string State, Ssid, Password, Error;
        internal readonly int Clients;
        internal HotspotView(string state, string ssid = "", string password = "", int clients = 0, string error = "")
        { State = state; Ssid = ssid; Password = password; Clients = clients; Error = error; }
    }
    // No dispatcher calls under this gate. Core lifecycle hooks wait for cleanup,
    // including a concurrent start, before the caller may mutate the tunnel.
    internal sealed class HotspotCoordinator
    {
        private readonly object gate = new object();
        private readonly Func<IHotspotChannel> create;
        private IHotspotChannel channel;
        private IHotspotSource owner;
        private IHotspotSource readyCandidate;
        private bool requested;
        private string ssid, password;
        private TunContext bound;
        private volatile bool coreChanging;
        internal bool CoreChanging => coreChanging;
        private volatile HotspotView view = new HotspotView("off");
        internal HotspotView View => view;
        internal HotspotCoordinator(Func<IHotspotChannel> create) { this.create = create; }
        internal static bool ValidPassword(string value)
        {
            if (value == null || value.Length != 10) return false;
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return true;
        }
        internal void Start(IHotspotSource source, string name, string secret)
        {
            lock (gate)
            {
                if (coreChanging) throw new InvalidOperationException("tun-required");
                if (channel != null || requested) throw new InvalidOperationException("hotspot-busy");
                if (!ValidPassword(secret)) throw new InvalidOperationException("invalid-password");
                owner = source ?? throw new InvalidOperationException("tun-required");
                ssid = name; password = secret; requested = true;
                StartLocked();
            }
        }
        private void StartLocked()
        {
            view = new HotspotView("starting");
            try
            {
                var tun = owner.CaptureTun();
                if (tun == null) throw new InvalidOperationException("tun-required");
                channel = create();
                channel.Start(tun, ssid, password);
                if (!tun.Same(owner.CaptureTun())) throw new InvalidOperationException("tun-changed-during-start");
                bound = tun;
                view = new HotspotView("active", ssid, password);
            }
            catch (Exception ex)
            {
                requested = false;
                string code = ErrorCode(ex);
                if (!CloseLocked()) code = "cleanup-not-confirmed";
                view = new HotspotView("error", error: code);
            }
        }
        internal bool Pause(IHotspotSource source, bool preserveRequest, Action invalidate = null, bool sharedCore = false)
        {
            lock (gate)
            {
                invalidate?.Invoke();
                if (sharedCore) { coreChanging = true; readyCandidate = null; }
                bool sameOwner = ReferenceEquals(owner, source);
                if (!sameOwner && !sharedCore) return true;
                requested = sameOwner && preserveRequest && requested;
                if (!CloseLocked())
                {
                    requested = false;
                    view = new HotspotView("error", error: "cleanup-not-confirmed");
                    return false;
                }
                view = new HotspotView(requested ? "paused" : "off");
                return true;
            }
        }
        internal void Ready(IHotspotSource source)
        {
            // RPC Start can return before the adapter is visible to NetworkInterface.
            // The background poll waits for a real TUN snapshot, without blocking WPF.
            lock (gate) readyCandidate = source;
        }
        internal void Resume(IHotspotSource source)
        {
            lock (gate)
            {
                if (source.CaptureTun() == null) return;
                coreChanging = false;
                if (ReferenceEquals(owner, source) && requested && channel == null) StartLocked();
            }
        }
        internal void Stop()
        {
            lock (gate)
            {
                requested = false;
                view = CloseLocked() ? new HotspotView("off") : new HotspotView("error", error: "cleanup-not-confirmed");
            }
        }
        internal void Poll()
        {
            lock (gate)
            {
                if (readyCandidate != null && readyCandidate.CaptureTun() != null)
                {
                    var candidate = readyCandidate; readyCandidate = null;
                    Resume(candidate);
                }
                if (channel == null || view.State != "active") return;
                try
                {
                    if (!bound.Same(owner.CaptureTun())) throw new InvalidOperationException("tun-lost");
                    int count = channel.Poll();
                    view = new HotspotView("active", ssid, password, count);
                }
                catch (Exception ex)
                {
                    // The watchdog may observe an exited core before TryReconnect's hook.
                    // Preserve intent for that ordering, but wait for a new successful core
                    // start signal; never restart directly from a failing health poll.
                    bool tunnelLost = (ex is InvalidOperationException && ex.Message == "tun-lost") ||
                        (ex is HotspotChannelException && ((HotspotChannelException)ex).TunnelLost);
                    requested = tunnelLost && requested;
                    string code = ErrorCode(ex);
                    if (!CloseLocked()) { requested = false; code = "cleanup-not-confirmed"; }
                    view = requested ? new HotspotView("paused") : new HotspotView("error", error: code);
                }
            }
        }
        private bool CloseLocked()
        {
            if (channel == null) return true;
            try { channel.Stop(); channel.Dispose(); }
            catch { return false; } // Retain the channel for explicit retry/recovery; don't claim Off.
            channel = null; bound = null;
            return true;
        }
        private static string ErrorCode(Exception ex)
        {
            var error = ex as HotspotChannelException;
            if (error != null) return error.Code;
            // Only our fixed codes may reach the UI. Never serialize arbitrary exceptions.
            if (ex is InvalidOperationException && (ex.Message == "tun-required" ||
                ex.Message == "tun-changed-during-start" || ex.Message == "tun-lost")) return ex.Message;
            return "hotspot-operation-failed";
        }
    }
    internal sealed class HotspotChannelException : Exception
    {
        internal readonly string Code;
        internal readonly bool TunnelLost;
        internal HotspotChannelException(string code, bool tunnelLost = false) : base(code)
        { Code = code; TunnelLost = tunnelLost; }
    }
}
