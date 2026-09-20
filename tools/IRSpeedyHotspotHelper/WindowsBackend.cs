using System.Collections;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace IRSpeedy.Hotspot;

internal sealed class WindowsBackend : IHotspotBackend
{
    private NetworkOperatorTetheringManager? manager;
    private Guid? preparedId;
    public Guid? BootstrapId { get; private set; }
    public bool IsOn => Step("winrt.read-state", () => Manager.TetheringOperationalState != TetheringOperationalState.Off);
    public uint ClientCount => Manager.ClientCount;
    private NetworkOperatorTetheringManager Manager => manager ?? throw new HotspotException("not-prepared");

    public void PrepareBootstrap(Guid publicId)
    {
        Safety.RequireTun(ReadAdapters(), publicId);
        var profile = Step("winrt.find-bootstrap-profile", NetworkInformation.GetInternetConnectionProfile);
        var id = profile?.NetworkAdapter?.NetworkAdapterId;
        if (id is null || id == Guid.Empty) throw new HotspotException("internet-profile-unavailable");
        Prepare(id.Value);
        BootstrapId = id.Value;
    }

    public void Prepare(Guid publicId)
    {
        if (preparedId == publicId && manager is not null) return;
        var profiles = Step("winrt.find-profile", () => NetworkInformation.GetConnectionProfiles()
            .Where(p => p.NetworkAdapter?.NetworkAdapterId == publicId).ToArray());
        if (profiles.Length != 1) throw new HotspotException("winrt-profile-unavailable");
        var capability = Step("winrt.check-capability", () => NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profiles[0]));
        if (capability != TetheringCapability.Enabled)
            throw new HotspotException("tethering-" + capability);
        // Exact recorded profile: recovery must never choose a new default upstream.
        manager = Step("winrt.create-manager", () => NetworkOperatorTetheringManager.CreateFromConnectionProfile(profiles[0]));
        preparedId = publicId;
    }

    public async Task Start(string ssid, string password)
    {
        if (IsOn)
            throw new HotspotException("hotspot-not-off");
        await StepAsync("winrt.configure-ap", async () =>
        {
            await Manager.ConfigureAccessPointAsync(new NetworkOperatorTetheringAccessPointConfiguration
            {
                Ssid = ssid,
                Passphrase = password
            });
            return true;
        });
        var result = await StepAsync("winrt.start-tethering", async () => await Manager.StartTetheringAsync());
        if (result.Status != TetheringOperationStatus.Success)
            throw new HotspotException("hotspot-start-" + result.Status);
        // No password, SSID, MAC addresses or AdditionalErrorMessage in diagnostics.
    }

    public async Task Stop()
    {
        if (Manager.TetheringOperationalState == TetheringOperationalState.Off) return;
        var result = await StepAsync("winrt.stop-tethering", async () => await Manager.StopTetheringAsync());
        if (result.Status != TetheringOperationStatus.Success)
            throw new HotspotException("hotspot-stop-" + result.Status);
        if (Manager.TetheringOperationalState != TetheringOperationalState.Off)
            throw new HotspotException("hotspot-stop-not-confirmed");
    }

    public IReadOnlyList<Adapter> ReadAdapters() => Step("ics.enumerate", ReadAdaptersCore);

    private IReadOnlyList<Adapter> ReadAdaptersCore()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces().ToDictionary(a => Guid.Parse(a.Id));
        var result = new List<Adapter>();
        Visit((id, configuration) =>
        {
            Marshal.ThrowExceptionForHR(configuration.get_SharingEnabled(out bool enabled));
            int? role = null;
            if (enabled)
            {
                Marshal.ThrowExceptionForHR(configuration.get_SharingConnectionType(out int value));
                role = value;
            }
            // Include unknown/disconnected COM connections so conflict checks cannot
            // silently miss sharing on an adapter not present in NetworkInterface.
            nics.TryGetValue(id, out var nic);
            result.Add(new Adapter(id, nic?.Name ?? "", nic?.Description ?? "",
                nic?.OperationalStatus == OperationalStatus.Up, role));
        });
        return result;
    }

    public void Bind(Guid publicId, Guid privateId)
    {
        var state = ReadAdapters();
        Safety.RequireTun(state, publicId);
        if (publicId == privateId || !state.Any(a => a.Id == privateId && a.Up &&
            a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)))
            throw new HotspotException("invalid-private-adapter");
        Safety.RequireRebindPair(state, publicId, privateId, BootstrapId);
        if (Safety.PairMatches(state, publicId, privateId)) return;
        if (BootstrapId is Guid bootstrapId && bootstrapId != publicId &&
            state.Any(a => a.Id == bootstrapId && a.SharingRole == 0))
        {
            // Only the pair created after our empty baseline can be transferred.
            Step("ics.disable-bootstrap-public", () => { Disable(bootstrapId, 0); return true; });
            if (state.Any(a => a.Id == privateId && a.SharingRole == 1))
                Step("ics.disable-bootstrap-private", () => { Disable(privateId, 1); return true; });
        }
        state = ReadAdapters();
        Safety.RequireTun(state, publicId);
        if (state.Any(a => a.SharingRole.HasValue && !
            (a.Id == publicId && a.SharingRole == 0 || a.Id == privateId && a.SharingRole == 1)))
            throw new HotspotException("ics-ownership-conflict");
        // Enabling public ICS can disable someone else's public ICS, so refuse all
        // conflicts above. No global DisableSharing loop, regsvr32 or service reset.
        if (!state.Any(a => a.Id == publicId && a.SharingRole == 0)) Enable(publicId, 0);
        if (!state.Any(a => a.Id == privateId && a.SharingRole == 1)) Enable(privateId, 1);
    }

    private static void Enable(Guid id, int role) => Step(role == 0 ? "ics.enable-public" : "ics.enable-private", () =>
    {
        WithConfiguration(id, c => Marshal.ThrowExceptionForHR(c.EnableSharing(role)));
        return true;
    });

    private static T Step<T>(string stage, Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) { if (!ex.Data.Contains("hotspot.stage")) ex.Data["hotspot.stage"] = stage; throw; }
    }

    private static async Task<T> StepAsync<T>(string stage, Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) { if (!ex.Data.Contains("hotspot.stage")) ex.Data["hotspot.stage"] = stage; throw; }
    }

    public void Disable(Guid id, int expectedRole) => WithConfiguration(id, c =>
    {
        Marshal.ThrowExceptionForHR(c.get_SharingEnabled(out bool enabled));
        if (!enabled) return;
        Marshal.ThrowExceptionForHR(c.get_SharingConnectionType(out int role));
        if (role != expectedRole) throw new HotspotException("ics-ownership-conflict");
        Marshal.ThrowExceptionForHR(c.DisableSharing());
    });

    private static void WithConfiguration(Guid id, Action<IcsConfiguration> action)
    {
        bool found = false;
        Visit((current, c) => { if (current == id) { found = true; action(c); } });
        if (!found) throw new HotspotException("ics-adapter-disappeared");
    }

    private static void Visit(Action<Guid, IcsConfiguration> action)
    {
        object root = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.HNetShare", true)!)!;
        object? collection = null;
        IEnumerator? enumerator = null;
        try
        {
            dynamic sharing = root;
            collection = sharing.EnumEveryConnection;
            enumerator = ((IEnumerable)collection).GetEnumerator();
            while (enumerator.MoveNext())
            {
                object connection = enumerator.Current!;
                object? properties = null;
                object? configuration = null;
                try
                {
                    properties = sharing.NetConnectionProps(connection);
                    Guid id = Guid.Parse((string)((dynamic)properties).Guid);
                    configuration = sharing.INetSharingConfigurationForINetConnection(connection);
                    action(id, (IcsConfiguration)configuration);
                }
                finally { Release(configuration); Release(properties); Release(connection); }
            }
        }
        finally { Release(enumerator); Release(collection); Release(root); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
