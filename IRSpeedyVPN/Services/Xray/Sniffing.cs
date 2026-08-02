using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class Sniffing
    {
        public bool enabled { get; set; }
        public List<string> destOverride { get; set; }
    }
}
