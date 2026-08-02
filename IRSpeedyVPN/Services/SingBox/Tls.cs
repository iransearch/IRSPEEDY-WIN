using System.Collections.Generic;
using v2rayN;

namespace IRSpeedyVPN.Services.SingBox
{
    public class Tls
    {
        public bool? enabled { get; set; }
        public bool? insecure { get; set; }
        public string server_name { get; set; }
        public List<string> alpn { get; set; }
        public Utls utls { get; set; }
        public Reality reality { get; set; }
    }
}
