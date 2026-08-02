using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SingBox
{
    public class Route
    {
        public bool? auto_detect_interface { get; set; }
        public string final { get; set; }
        public bool? find_process { get; set; }
        public DefaultDomainResolver default_domain_resolver { get; set; }
        public Geoip geoip { get; set; }
        public Geosite geosite { get; set; }
        public List<Rule> rules { get; set; }
        public List<RuleSet> rule_set { get; set; }
    }


}
