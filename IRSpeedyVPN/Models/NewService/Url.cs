using System;
using System.Drawing.Drawing2D;

namespace IRSpeedyVPN.Models.NewService
{
    public partial class Url
    {

        // Properties
        public int id { get; set; }

        public int server_id { get; set; }

        public string url { get; set; }

        public string extra_field_1 { get; set; }

        public object extra_field_2 { get; set; }

        public int status { get; set; }

        public DateTime created_at { get; set; }

        public DateTime updated_at { get; set; }

        public int is_subscription { get; set; }

        public int irancell { get; set; }

        public int chainproxy { get; set; }
        public long latency { get; set; }
        public DateTime latencychkTime { get; set; }

        public VPNType VPNType =>
            (this.chainproxy != 1) ? ((this.irancell != 1) ? VPNType.NORMAL : VPNType.VOD) : VPNType.CHAIN;
    }

}
