namespace IRSpeedyVPN.Services.SingBox
{
    public class DnsServer
    {
        public string type { get; set; }
        public System.Collections.Generic.List<DnsServer> servers { get; set; }
        public string server { get; set; }
        public int? server_port { get; set; }
        public string path { get; set; }
        public Tls tls { get; set; }
        public string domain_resolver { get; set; }
        public string detour { get; set; }
        public string tag { get; set; }
    }


}
