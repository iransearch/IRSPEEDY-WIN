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
        internal static TunnelPlusService RecoveryFixture(int port, bool residential = false)
        {
            var service = residential ? new ResidentialRecoveryFixture() : new TunnelPlusService();
            service.CorePort=port;service.IsConnected=true;service.autoSelectorPlan=new object();
            service.lastListenPort=1;service.gInfo=new Info();
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
    internal sealed class ResidentialRecoveryFixture : TunnelPlusService { }
}
