namespace IRSpeedyVPN.Services.SingBox
{
    public class DnsServer
    {
        public string type { get; set; }
        public string server { get; set; }
        public int? server_port { get; set; }
        public string domain_resolver { get; set; }
        public string detour { get; set; }
        public string tag { get; set; }
    }


}
