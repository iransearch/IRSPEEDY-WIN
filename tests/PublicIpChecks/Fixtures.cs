using IRSpeedyVPN.Common;
using System;
using System.Collections.Concurrent;

namespace IRSpeedyVPN.Interfaces
{
    internal interface IVPNService { int? HttpPort { get; } }
}
namespace IRSpeedyVPN.Services
{
    internal class TunnelPlusService : Interfaces.IVPNService
    {
        internal string DiagnosticIdentity => "service=fixture";
        internal void RequestConnectionState(string reason, string request = null, int delayMs = 0, string expectedIdentity = null)
            => ConnectionDiagnostics.Records.Enqueue("snapshot-request cause=" + reason);
        public int? HttpPort { get; set; } = 7788;
    }
}
namespace IRSpeedyVPN.Common
{
    internal static class ConnectionDiagnostics
    {
        internal static readonly ConcurrentQueue<string> Records = new ConcurrentQueue<string>();
        internal static void Write(string stage, string fields) => Records.Enqueue(stage + " " + fields);
        internal static string Fingerprint(string value) => "fixture-fingerprint";
    }
    internal static class TimeZoneLookup { internal const string Host = "ipwho.is"; }
}
namespace IRSpeedyVPN.UserControls
{
    public partial class UCUserInfo
    {
        internal sealed class TextBlock { internal string Text; internal object ToolTip; }
        private sealed class Globals { internal Interfaces.IVPNService CurrentService; internal DateTime ConnectionTime; }
        private readonly Globals globalInfo;
        private readonly TextBlock txtReceivedIp = new TextBlock { Text = "—" };
        private bool IsLoaded = true, IsVisible = true;
        internal UCUserInfo(PublicIpLookup lookup)
        {
            publicIpLookup = lookup;
            globalInfo = new Globals { CurrentService = new Services.TunnelPlusService(), ConnectionTime = DateTime.UtcNow };
        }
        internal void Refresh() => RefreshPublicIp();
        internal void Hide() { IsVisible = false; CancelPublicIpRequest(); }
        internal void Show() { IsVisible = true; RefreshPublicIp(); }
        internal void Reconnect() { globalInfo.ConnectionTime = globalInfo.ConnectionTime.AddSeconds(1); RefreshPublicIp(); }
        internal void ChangePort(int port) { ((Services.TunnelPlusService)globalInfo.CurrentService).HttpPort = port; RefreshPublicIp(); }
        internal string Displayed => txtReceivedIp.Text;
        internal bool Pending => publicIpRequest != null;
    }
}
