using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.SingBox
{
       public class WireGuardUnformatted
    {
        public string server { get; set; }
        public int server_port { get; set; }
        public List<string> local_address { get; set; }
        public string private_key { get; set; }
        public string peer_public_key { get; set; }
        public string pre_shared_key { get; set; }
        public string mtu { get; set; }
    }


}
