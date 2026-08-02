using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class Routing
    {
        public string domainStrategy { get; set; }
        public string domainMatcher { get; set; }
        public List<Rule> rules { get; set; }
    }
}
