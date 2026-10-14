using System;
using IRSpeedyVPN.Services;
namespace IRSpeedyVPN.Common {
    internal static class LogHelper { internal static void WriteExLog(string text) { } }
}
namespace IRSpeedyVPN.Services {
    internal static class InternetConnectivityMonitor { internal static bool? LastKnownOnline => null; }
    internal partial class TunnelPlusService {
        bool userCancelRequested, IsConnected;
        long connectionGeneration;
        int lastListenPort;
        const int CorePort = 19810;
        object autoSelectorPlan;
        Info gInfo;
        sealed class Info { internal object CurrentService; }
        void Diagnostic(string stage, string fields) { }
        void RequestConnectionState(string reason) { }
    }
}
