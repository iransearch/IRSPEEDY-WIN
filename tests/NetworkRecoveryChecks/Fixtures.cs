using System;
using IRSpeedyVPN.Services;
namespace IRSpeedyVPN.Common {
    internal static class LogHelper { internal static void WriteExLog(string text) { } }
}
namespace IRSpeedyVPN.Services {
    internal partial class TunnelPlusService {
        bool userCancelRequested, IsConnected;
        long connectionGeneration;
        int lastListenPort;
        int CorePort;
        object autoSelectorPlan;
        Info gInfo;
        sealed class Info { internal object CurrentService; }
        void Diagnostic(string stage, string fields) { }
        void RequestConnectionState(string reason) { }
        internal static TunnelPlusService RecoveryFixture(int port)
        {
            var service = new TunnelPlusService { CorePort=port, IsConnected=true, autoSelectorPlan=new object(),
                lastListenPort=1, gInfo=new Info() };
            service.gInfo.CurrentService=service;
            service.BeginNetworkRecoverySession();
            return service;
        }
        internal void StopFixture()
        {
            userCancelRequested=true;IsConnected=false;connectionGeneration++;
            EndNetworkRecoverySession();
        }
    }
}
