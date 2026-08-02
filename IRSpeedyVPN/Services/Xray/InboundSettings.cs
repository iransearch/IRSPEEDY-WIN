using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class InboundSettings
    {
        public string auth { get; set; }
        public bool udp { get; set; }
        public List<Account> accounts { get; set; }
    }
}
