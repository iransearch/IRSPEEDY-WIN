using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SingBox
{
    public class Outbound
    {
        public int? alter_id { get; set; }
        public string domain_strategy { get; set; }
        public string security { get; set; }
        public string flow { get; set; }
        public string server { get; set; }
        public int? server_port { get; set; }
        public List<string> server_ports { get; set; }
        public string tag { get; set; }
        public Tls tls { get; set; }
        public Transport transport { get; set; }
        public string type { get; set; }
        public string uuid { get; set; }
        public bool? udp_fragment { get; set; }
        public Multiplex multiplex { get; set; }
        public string method { get; set; }
        public string password { get; set; }
        public bool? udp_over_tcp { get; set; }
        public string network { get; set; }
        public string username { get; set; }
        public string user { get; set; }
        public string version { get; set; }
        public object obfs { get; set; }
        public string congestion_control { get; set; }
        public string udp_relay_mode { get; set; }
        public string protocol { get; set; }
        public string obfs_param { get; set; }
        public string protocol_param { get; set; }
        public string private_key { get; set; }
        public string peer_public_key { get; set; }
        public string pre_shared_key { get; set; }
        public List<int> reserved { get; set; }
        public int? workers { get; set; }
        public bool? system_interface { get; set; }
        public List<string> local_address { get; set; }
        public string interface_name { get; set; }
        public string client_version { get; set; }
        public string packet_encoding { get; set; }
        public string detour { get; set; }



    }

    public class HysteriaObfs
    {
        public string password { get; set; }
        public string type { get; set; }
    }

}
