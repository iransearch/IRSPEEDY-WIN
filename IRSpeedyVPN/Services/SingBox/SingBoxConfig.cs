using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.SingBox
{

    public class SingBoxConfig
    {
        public Dns dns { get; set; }
        public Experimental experimental { get; set; }
        public List<Inbound> inbounds { get; set; }
        public Log log { get; set; }
        public List<Outbound> outbounds { get; set; }
        public Route route { get; set; }
    }

}
