// Inert existing service/account boundaries. New production files themselves
// compile against real WPF + .NET Framework 4.8 references, never fake WPF APIs.
using System;
using System.Collections.Generic;
namespace IRSpeedyVPN.Interfaces {
    internal interface IVPNService {
        List<IRSpeedyVPN.Models.NewService.Url> GetServerUrls();
        bool IsUrlTestSupported {get;} long UrlTestSpeed {get;}
        IRSpeedyVPN.Models.NewService.Url SelectedServerUrl {get;set;}
        int ID {get;} string Name {get;set;} string Country {get;} string CountryCode {get;}
    }
    internal interface ISmartFastConnection {bool IsSmartFast {get;}}
}
namespace IRSpeedyVPN.Models { internal class GlobalInfo { public string Username { get; set; } public IRSpeedyVPN.Interfaces.IVPNService CurrentService; } }
namespace IRSpeedyVPN.Models.NewService {
    internal class Url { public string url,extra_field_1; public object extra_field_2; public int status,chainproxy,is_subscription; public long latency,LastSuccessfulLatency; public DateTime latencychkTime,LastSuccessfulCheckTime; }
    internal class ServerEx { public int ID,status; public string Country; public List<Url> urls; }
    internal class UserAccount {
        public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn;
        public object GroupID,group_id,pid; public string GroupName,group_name;
    }
    internal class AccountInfoEx { public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn; public UserAccount UserAccount; }
}
namespace IRSpeedyVPN.Common { internal static class LogHelper { public static void WriteExLog(string message) {} } }
namespace IRSpeedyVPN.Services {
    internal class TunnelPlusService:IRSpeedyVPN.Interfaces.IVPNService,IRSpeedyVPN.Interfaces.ISmartFastConnection {
        readonly IRSpeedyVPN.Models.NewService.ServerEx server;
        public string Name {get;set;} public int ID=>server.ID; public string Country=>server.Country; public string CountryCode=>server.Country;
        public IRSpeedyVPN.Models.NewService.Url SelectedServerUrl {get;set;}
        public bool IsUrlTestSupported=>true; public long UrlTestSpeed=>0;
        public bool IsSmartFast {get;private set;}
        public TunnelPlusService(IRSpeedyVPN.Models.NewService.ServerEx server, IRSpeedyVPN.Models.GlobalInfo global) { this.server=server; }
        public void SetSmartFastUrls(string[] urls) {IsSmartFast=urls.Length>0;}
        public List<IRSpeedyVPN.Models.NewService.Url> GetServerUrls()=>server.urls;
    }
}

