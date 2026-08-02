using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class XrayConfig
    {
        public Log log { get; set; }
        public Dns dns { get; set; }
        public List<Inbound> inbounds { get; set; }
        public List<Outbound> outbounds { get; set; }
        public Routing routing { get; set; }
    }
}
