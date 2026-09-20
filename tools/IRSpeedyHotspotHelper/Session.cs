using System.Text;

namespace IRSpeedy.Hotspot;

public sealed class HotspotException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record Adapter(Guid Id, string Name, string Description, bool Up, int? SharingRole);
public sealed record Journal(int Version, Guid PublicId, Guid? PrivateId, bool StartAttempted);

public interface IJournal
{
    Journal? Read();
    void Write(Journal state);
    void Clear();
}

public interface IHotspotBackend
{
    IReadOnlyList<Adapter> ReadAdapters();
    void Prepare(Guid publicId);
    Task Start(string ssid, string password);
    Task Stop();
    void Bind(Guid publicId, Guid privateId);
    void Disable(Guid id, int expectedRole);
    bool IsOn { get; }
    uint ClientCount { get; }
}

public static class Safety
{
    public static void ValidateCredentials(string ssid, string password)
    {
        if (string.IsNullOrWhiteSpace(ssid) || Encoding.UTF8.GetByteCount(ssid) > 32 || ssid.Any(char.IsControl))
            throw new HotspotException("invalid-ssid");
        if (password.Length is < 8 or > 63 || password.Any(c => c < 32 || c > 126))
            throw new HotspotException("invalid-passphrase");
    }

    public static Adapter RequireTun(IEnumerable<Adapter> adapters, Guid id)
    {
        var a = adapters.SingleOrDefault(a => a.Id == id);
        // Exact application-owned interface name; NEVER choose the default route.
        if (a is null || !a.Up || !string.Equals(a.Name, "irspeedy-tun", StringComparison.OrdinalIgnoreCase))
            throw new HotspotException("active-irspeedy-tun-required");
        return a;
    }

    public static Guid SelectPrivate(IReadOnlyList<Adapter> before, IReadOnlyList<Adapter> after, Guid publicId)
    {
        var candidates = after.Where(a => a.Id != publicId && a.Up &&
            a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)).ToArray();
        var shared = candidates.Where(a => a.SharingRole == 1).ToArray();
        if (shared.Length == 1) return shared[0].Id;
        if (shared.Length > 1) throw new HotspotException("ambiguous-hotspot-adapter");
        var activated = candidates.Where(a => !before.Any(b => b.Id == a.Id && b.Up)).ToArray();
        if (activated.Length != 1) throw new HotspotException("ambiguous-hotspot-adapter");
        return activated[0].Id;
    }

    public static bool PairMatches(IEnumerable<Adapter> adapters, Guid publicId, Guid privateId)
    {
        var shared = adapters.Where(a => a.SharingRole.HasValue).ToArray();
        return shared.Length == 2 && shared.Any(a => a.Id == publicId && a.Up && a.SharingRole == 0) &&
            shared.Any(a => a.Id == privateId && a.Up && a.SharingRole == 1);
    }
}

// Commands are serialized by the host. No thread is allowed to start/stop concurrently.
public sealed class Session(IHotspotBackend backend, IJournal journal)
{
    public bool Active { get; private set; }
    public Journal? State { get; private set; }

    public async Task Start(Guid publicId, string ssid, string password, bool experimental)
    {
        if (!experimental) throw new HotspotException("experimental-opt-in-required");
        if (Active || journal.Read() is not null) throw new HotspotException("recovery-required");
        Safety.ValidateCredentials(ssid, password);
        var before = backend.ReadAdapters();
        Safety.RequireTun(before, publicId);
        // Existing ICS is deliberately NOT taken over in this PoC. Its empty baseline
        // is the snapshot, so rollback never needs to disturb another application's pair.
        if (before.Any(a => a.SharingRole.HasValue)) throw new HotspotException("existing-ics-conflict");
        backend.Prepare(publicId);
        if (backend.IsOn) throw new HotspotException("existing-hotspot-conflict");
        State = new Journal(1, publicId, null, false);
        journal.Write(State);
        try
        {
            State = State with { StartAttempted = true };
            journal.Write(State); // durable BEFORE any networking mutation
            await backend.Start(ssid, password);
            var after = backend.ReadAdapters();
            Safety.RequireTun(after, publicId);
            State = State with { PrivateId = Safety.SelectPrivate(before, after, publicId) };
            journal.Write(State);
            // Windows normally establishes this pair itself. Validate/reconcile only
            // the selected pair; never disable all ICS connections.
            backend.Bind(publicId, State.PrivateId.Value);
            if (!Safety.PairMatches(backend.ReadAdapters(), publicId, State.PrivateId.Value))
                throw new HotspotException("ics-verification-failed");
            Active = true;
        }
        catch
        {
            try { await Stop(); }
            catch { throw new HotspotException("start-failed-recovery-required"); }
            throw;
        }
    }

    public bool Healthy()
    {
        if (!Active || State?.PrivateId is not Guid privateId) return false;
        var adapters = backend.ReadAdapters();
        return backend.IsOn && Safety.PairMatches(adapters, State.PublicId, privateId) &&
            adapters.Any(a => a.Id == State.PublicId && a.Name.Equals("irspeedy-tun", StringComparison.OrdinalIgnoreCase));
    }

    public async Task Stop()
    {
        Active = false;
        var state = State ?? journal.Read();
        if (state is null) return;
        if (state.Version != 1 || state.PublicId == Guid.Empty || state.PrivateId == state.PublicId)
            throw new HotspotException("invalid-recovery-journal");
        State = state;
        // If the original TUN profile is gone, Prepare fails. Keep the journal and
        // require manual hotspot shutdown; never start or select a physical upstream.
        backend.Prepare(state.PublicId);
        if (state.StartAttempted) await backend.Stop();
        var afterStop = backend.ReadAdapters();
        if (state.PrivateId is Guid privateId && afterStop.Any(a => a.Id == privateId))
            backend.Disable(privateId, 1);
        if (afterStop.Any(a => a.Id == state.PublicId)) backend.Disable(state.PublicId, 0);
        // The accepted baseline had NO ICS; any remaining sharing means restoration
        // is not proven. Keep the journal, but do not disable an unrelated pair.
        if (backend.IsOn || backend.ReadAdapters().Any(a => a.SharingRole.HasValue))
            throw new HotspotException("cleanup-verification-failed");
        journal.Clear();
        State = null;
    }
}
