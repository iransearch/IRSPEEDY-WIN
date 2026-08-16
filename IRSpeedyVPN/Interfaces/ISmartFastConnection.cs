namespace IRSpeedyVPN.Interfaces
{
    /// <summary>
    /// Optional capability for VPN services that support the smart fast connection.
    /// The ui hands every successfully-tested url to the service and then continues
    /// through the normal OnConnectRequest -> Connect -> RunV2ray flow; RunV2ray builds
    /// a single Xray balancer config.
    /// </summary>
    public interface ISmartFastConnection
    {
        bool IsSmartFast { get; }
        void SetSmartFastUrls(string[] successUrls);
    }
}
