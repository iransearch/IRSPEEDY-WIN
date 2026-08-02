namespace IRSpeedyVPN.Services.Xray
{
    public class DnsServer
    {
        public string address { get; set; }
        public int? port { get; set; }
        public string queryStrategy { get; set; }
        public bool? skipFallBack { get; set; }
    }
}
