using Windows.Devices.WiFiDirect;
using Windows.Security.Credentials;

namespace IRSpeedy.Hotspot;

// Desktop legacy AP: no NetworkOperatorTetheringManager or Internet profile.
// ICS remains a separate step, using the existing guarded COM implementation.
internal sealed class WiFiDirectBackend(Guid? recoveryPrivateId = null) : IHotspotBackend
{
    private readonly WindowsBackend sharing = new();
    private readonly object sync = new();
    private readonly List<WiFiDirectDevice> clients = new();
    private WiFiDirectAdvertisementPublisher? publisher;
    private WiFiDirectConnectionListener? listener;
    private bool accepting;
    private int pending;
    private int generation;
    private string status = "NotStarted";
    private string publisherError = "None";
    private string? connectionError;
    public string Kind => "wifi-direct";
    public bool AutomaticSharing => false;
    public Guid? BootstrapId => null;
    public bool IsOn => publisher?.Status == WiFiDirectAdvertisementPublisherStatus.Started;
    public uint ClientCount { get { lock (sync) return (uint)clients.Count; } }
    public object Diagnostics { get { lock (sync) return new { mode = Kind, publisherStatus = status,
        publisherError, connectionError, pendingConnections = pending, ics = sharing.Diagnostics }; } }

    public IReadOnlyList<Adapter> ReadAdapters() => sharing.ReadAdapters();
    public void Prepare(Guid publicId) { } // Recovery needs no TUN/Internet profile.
    public void PrepareBootstrap(Guid publicId)
    {
        var adapters = ReadAdapters();
        Safety.RequireTun(adapters, publicId);
        if (adapters.Any(a => a.Up && a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)))
            throw Failure("wifi-direct-already-active", "wfd.preflight");
    }

    public async Task Start(string ssid, string password)
    {
        try
        {
            if (publisher is not null) throw Failure("publisher-already-created", "wfd.create");
            publisher = new WiFiDirectAdvertisementPublisher();
            publisher.StatusChanged += StatusChanged;
            publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
            publisher.Advertisement.LegacySettings.IsEnabled = true;
            publisher.Advertisement.LegacySettings.Ssid = ssid;
            publisher.Advertisement.LegacySettings.Passphrase = new PasswordCredential { Password = password };
            listener = new WiFiDirectConnectionListener();
            listener.ConnectionRequested += ConnectionRequested;
            publisher.Start();
            for (int poll = 0; poll <= 40; poll++)
            {
                var current = publisher.Status;
                lock (sync) status = current.ToString();
                if (current == WiFiDirectAdvertisementPublisherStatus.Started) return;
                if (current is WiFiDirectAdvertisementPublisherStatus.Aborted or WiFiDirectAdvertisementPublisherStatus.Stopped)
                    throw Failure("publisher-" + current, "wfd.start");
                if (poll < 40) await Task.Delay(250);
            }
            throw Failure("publisher-start-timeout", "wfd.start");
        }
        catch (Exception ex) { ex.Data["hotspot.stage"] ??= "wfd.start"; throw; }
    }

    private void StatusChanged(WiFiDirectAdvertisementPublisher sender, WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        lock (sync)
        {
            status = args.Status.ToString(); publisherError = args.Error.ToString();
            if (args.Status != WiFiDirectAdvertisementPublisherStatus.Started) accepting = false;
        }
    }

    // Password-authenticated legacy clients need no app or pairing UI. Retain the
    // WiFiDirectDevice until disconnect; disposing it terminates that association.
    private async void ConnectionRequested(WiFiDirectConnectionListener sender, WiFiDirectConnectionRequestedEventArgs args)
    {
        WiFiDirectConnectionRequest? request = null;
        WiFiDirectDevice? device = null;
        bool counted = false;
        try
        {
            request = args.GetConnectionRequest();
            int epoch;
            lock (sync)
            {
                if (!accepting || clients.Count + pending >= 16) return;
                epoch = generation; pending++; counted = true;
            }
            device = await WiFiDirectDevice.FromIdAsync(request.DeviceInformation.Id);
            if (device is null) throw Failure("client-connect-null", "wfd.accept-client");
            lock (sync)
            {
                if (!accepting || epoch != generation) return;
                device.ConnectionStatusChanged += ClientChanged;
                if (device.ConnectionStatus == WiFiDirectConnectionStatus.Connected)
                {
                    clients.Add(device); device = null; // list now owns the handle
                }
                else device.ConnectionStatusChanged -= ClientChanged;
            }
        }
        catch (Exception ex)
        {
            lock (sync) connectionError = ex.GetType().Name + ":" + ex.HResult.ToString("X8");
        }
        finally
        {
            if (counted) { lock (sync) pending--; }
            // No exception may escape an async event callback.
            try { device?.Dispose(); } catch { }
            try { request?.Dispose(); } catch { }
        }
    }

    private void ClientChanged(WiFiDirectDevice sender, object args)
    {
        try
        {
            lock (sync)
            {
                if (sender.ConnectionStatus != WiFiDirectConnectionStatus.Disconnected) return;
                if (!clients.Remove(sender)) return;
                sender.ConnectionStatusChanged -= ClientChanged;
                sender.Dispose();
            }
        }
        catch (Exception ex) { lock (sync) connectionError = ex.GetType().Name + ":" + ex.HResult.ToString("X8"); }
    }

    public void Bind(Guid publicId, Guid privateId)
    {
        sharing.Bind(publicId, privateId);
    }

    public void EnableClients()
    {
        if (!IsOn) throw Failure("publisher-not-started", "wfd.enable-clients");
        lock (sync) accepting = true;
    }

    public async Task Stop()
    {
        try
        {
            lock (sync)
            {
                accepting = false; generation++;
                foreach (var device in clients)
                {
                    try { device.ConnectionStatusChanged -= ClientChanged; device.Dispose(); }
                    catch (Exception ex) { connectionError = ex.GetType().Name + ":" + ex.HResult.ToString("X8"); }
                }
                clients.Clear();
            }
            if (listener is not null) { listener.ConnectionRequested -= ConnectionRequested; listener = null; }
            if (publisher is null)
            {
                // A publisher from another process cannot be stopped by this instance.
                // Refuse takeover if the recorded adapter is still active after a crash.
                if (ReadAdapters().Any(a => a.Up && (recoveryPrivateId is Guid id ? a.Id == id :
                    a.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))))
                    throw Failure("wifi-direct-recovery-adapter-still-active", "wfd.recover");
                return;
            }
            publisher.Stop();
            for (int poll = 0; poll <= 40; poll++)
            {
                var current = publisher.Status;
                if (current is WiFiDirectAdvertisementPublisherStatus.Stopped or WiFiDirectAdvertisementPublisherStatus.Aborted or WiFiDirectAdvertisementPublisherStatus.Created)
                {
                    lock (sync) status = current.ToString();
                    publisher.StatusChanged -= StatusChanged;
                    publisher = null;
                    return;
                }
                if (poll < 40) await Task.Delay(250);
            }
            throw Failure("publisher-stop-timeout", "wfd.stop");
        }
        catch (Exception ex) { ex.Data["hotspot.stage"] ??= "wfd.stop"; throw; }
    }

    public void Disable(Guid id, int expectedRole) => sharing.Disable(id, expectedRole);
    private static HotspotException Failure(string code, string stage)
    {
        var ex = new HotspotException(code); ex.Data["hotspot.stage"] = stage; return ex;
    }
}
