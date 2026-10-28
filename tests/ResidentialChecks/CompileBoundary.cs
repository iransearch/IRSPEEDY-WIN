// Inert existing service/account boundaries. New production files themselves
// compile against real WPF + .NET Framework 4.8 references, never fake WPF APIs.
using System;
using System.Collections.Generic;
namespace IRSpeedyVPN.Interfaces { internal interface IVPNService { List<IRSpeedyVPN.Models.NewService.Url> GetServerUrls(); } }
namespace IRSpeedyVPN.Models { internal class GlobalInfo { public string Username { get; set; } } }
namespace IRSpeedyVPN.Models.NewService {
    internal class Url { public string url; public object extra_field_2; public int status; public long latency,LastSuccessfulLatency; public DateTime latencychkTime,LastSuccessfulCheckTime; }
    internal class ServerEx { public int ID,status; public string Country; public List<Url> urls; }
    internal class UserAccount {
        public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn;
        public object GroupID,group_id,pid; public string GroupName,group_name;
    }
    internal class AccountInfoEx { public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn; public UserAccount UserAccount; }
}
namespace IRSpeedyVPN.Common { internal static class LogHelper { public static void WriteExLog(string message) {} } }
namespace IRSpeedyVPN.Services {
    internal class TunnelPlusService:IRSpeedyVPN.Interfaces.IVPNService {
        readonly IRSpeedyVPN.Models.NewService.ServerEx server;
        public string Name; public IRSpeedyVPN.Models.NewService.Url SelectedServerUrl;
        public TunnelPlusService(IRSpeedyVPN.Models.NewService.ServerEx server, IRSpeedyVPN.Models.GlobalInfo global) { this.server=server; }
        public void SetSmartFastUrls(string[] urls) {}
        public List<IRSpeedyVPN.Models.NewService.Url> GetServerUrls()=>server.urls;
    }
}
