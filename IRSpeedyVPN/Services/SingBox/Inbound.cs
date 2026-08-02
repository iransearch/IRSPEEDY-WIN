namespace IRSpeedyVPN.Services.SingBox
{
    public class Inbound
    {
        public string listen { get; set; }
        public int? listen_port { get; set; }
        public bool? sniff { get; set; }
        public bool? sniff_override_destination { get; set; }
        public string tag { get; set; }
        public string type { get; set; }
        public string interface_name { get; set; }
        public string inet4_address { get; set; }
        public int? mtu { get; set; }
        public bool? auto_route { get; set; }
        public bool? strict_route { get; set; }
        public string stack { get; set; }
        public bool? endpoint_independent_nat { get; set; }
        public string[] address { get; set; }
        public string[] route_exclude_address { get; set; }
    }



}
