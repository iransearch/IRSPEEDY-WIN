using System; using System.Diagnostics;
namespace IRSpeedyVPN.Common { public static class LogHelper { public static Action<string> Sink; public static void WriteLog(string s) { if (LogPolicy.ShouldWrite(s)) Sink?.Invoke(s); } } }
namespace IRSpeedyVPN.Resource { public static class RegHelper { public static string Mode="0", Game="0"; public static string GetSettingValue(string k) { return k=="VGAURDVPNMode"?Mode:Game; } } }
namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        public class Globals { public TunnelPlusService CurrentService; }
        private class Plan { public int MainMembers = 11, AiMembers = 2; }
        private Plan autoSelectorPlan;
        private Globals gInfo;
        private bool IsConnected, lastVpnMode, useSystemProxy, coreOwned, suppressCoreExit, userCancelRequested;
        private int lastListenPort, ID, CountryIndex, ProxifierRuleType, reconnecting;
        private long connectionGeneration;
        private Process coreProcess;
        private static int CorePort = 19810;

        public static TunnelPlusService SampleFixture(int port)
        {
            CorePort = port;
            var service = new TunnelPlusService { IsConnected = true, autoSelectorPlan = new Plan(), lastListenPort = 10808 };
            service.gInfo = new Globals { CurrentService = service };
            return service;
        }
        public void EndConnection() { userCancelRequested = true; IsConnected = false; System.Threading.Interlocked.Increment(ref connectionGeneration); }
        public void NextConnection() { diagnosticConnectionId = Guid.NewGuid().ToString("N"); System.Threading.Interlocked.Increment(ref connectionGeneration); }
        public static void Exercise()
        {
            var s = new TunnelPlusService(); s.gInfo = new Globals { CurrentService = s };
            s.Diagnostic("test-before"); s.IsConnected = true; s.lastVpnMode = true; s.Diagnostic("test-tun");
            s.lastVpnMode = false; s.Diagnostic("test-proxy");
            s.ObserveCoreOutput(null, "stderr", "network changed secret://private@example.com password=PRIVATE_VALUE");
            var reader = new TunnelPlusService { gInfo = s.gInfo }; s.useSystemProxy = true;
            for (int i = 0; i < 50; i++) reader.ObserveCoreOutput(null, "stderr", "route outbound proxy");
            reader.ObserveCoreOutput(null, "stderr", "[Info] [1234] app/dispatcher: taking detour [ai-proxy-2] for [tcp:showip.net:443]");
            reader.ObserveCoreOutput(null, "stderr", "error connection refused context deadline exceeded secret://private@example.com password=PRIVATE_VALUE");
            reader.ObserveCoreOutput(null, "stderr", "[CoreDiagnostic] schema=core-network-v2 event=dns-race-created pid=7 seq=10 box=2 dns=8 count=4 timeoutMs=8000 fallbackTimeoutMs=4000 current=true");
            reader.ObserveCoreOutput(null, "stderr", "[CoreDiagnostic] schema=core-network-v2 event=dns-race-result pid=7 seq=11 box=2 dns=8 count=1 wins0=0 wins1=0 wins2=1 wins3=0 wins4=0 cached=0 failures=0 index=2 source=ip-network elapsedMs=24 error=none");
            reader.ObserveCoreOutput(null, "stderr", "[CoreDiagnostic] schema=core-network-v2 event=dns-race-result pid=7 seq=12 box=2 dns=8 count=2 wins0=0 wins1=0 wins2=2 wins3=0 wins4=0 cached=1 failures=0 index=2 source=ip-cache elapsedMs=0 error=none");
            reader.ObserveCoreOutput(null, "stderr", "[CoreDiagnostic] schema=core-network-v2 event=dns-race-summary pid=7 seq=13 box=2 dns=8 count=2 wins0=0 wins1=0 wins2=2 wins3=0 wins4=0 cached=1 failures=0");
            s.EndConnection();
        }
    }
}


// SystemProxy uses this namespace only for commented-out message boxes.
namespace System.Windows.Forms { }

