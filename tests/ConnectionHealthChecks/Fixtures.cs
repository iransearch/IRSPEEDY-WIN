using System;
using System.Threading;
namespace IRSpeedyVPN.Common { internal static class Placeholder { } }
namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private bool userCancelRequested, applicationExiting, useSystemProxy, lastVpnMode;
        private long connectionGeneration;
        private int lastListenPort;
        private readonly object connectionLifecycleGate = new object();
        private readonly Info gInfo = new Info();
        private sealed class Info { internal object CurrentService; }
        private Action<TunnelPlusService, bool, int, string> onConnectDisconnect;
        private void TryReconnect() { }
        private void Diagnostic(string stage, string detail) { }
    }
    internal static class WinINet
    {
        internal static void SetIEProxy(bool enable, bool global, string server, string pac) { }
    }
}
