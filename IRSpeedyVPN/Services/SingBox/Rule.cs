using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SingBox
{
    public class Rule
    {
        public string strategy;

        public object domain { get; set; }
        public List<object> domain_keyword { get; set; }
        public List<object> domain_suffix { get; set; }
        public List<object> geosite { get; set; }
        public string server { get; set; }
        public object inbound { get; set; }
        public string outbound { get; set; }
        public string network { get; set; }
        public object port { get; set; }
        public List<string> ip_cidr { get; set; }
        public List<string> source_ip_cidr { get; set; }
        public List<string> process_name { get; set; }
        public object process_path { get; set; }
        public string protocol { get; set; }
        public string action { get; set; }
        public List<string> rule_set { get; set; }
        public string query_type { get; set; }
        public string rcode { get; set; }
        public string answer { get; set; }
    

    }


}
