namespace IRSpeedyVPN.Interfaces
{
    /// <summary>
    /// Optional capability for VPN services that support the smart fast connection.
    /// The ui hands every successfully-tested url to the service and then continues
    /// through the normal OnConnectRequest -> Connect -> RunV2ray flow. RunV2ray keeps
    /// native sing-box protocols such as Hysteria2 in core_config and uses Xray only for
    /// the links that belong in xray_config.
    /// </summary>
    public interface ISmartFastConnection
    {
        bool IsSmartFast { get; }
        void SetSmartFastUrls(string[] successUrls);
    }
}
