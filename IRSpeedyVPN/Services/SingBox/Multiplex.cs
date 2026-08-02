using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Services.SingBox
{
    
    public class Multiplex
    {
        public bool enabled { get; set; }
        public string protocol { get; set; }
        public int? max_connections { get; set; }
        public int? min_streams { get; set; }
        public int? max_streams { get; set; }
    }


}
