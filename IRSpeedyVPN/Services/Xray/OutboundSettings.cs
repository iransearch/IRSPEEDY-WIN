using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class OutboundSettings
    {
        public string address { get; set; }
        public int? port { get; set; }
        public string id { get; set; }
        public string encryption { get; set; }
        public string flow { get; set; }
        public int? alterId { get; set; }
        public string security { get; set; }
        public string password { get; set; }
        public string method { get; set; }
        public string domainStrategy { get; set; }
        public List<SocksUser> users { get; set; }
    }
}
