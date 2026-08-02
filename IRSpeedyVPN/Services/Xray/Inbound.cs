namespace IRSpeedyVPN.Services.Xray
{
    public class Inbound
    {
        public string listen { get; set; }
        public int port { get; set; }
        public string protocol { get; set; }
        public string tag { get; set; }
        public InboundSettings settings { get; set; }
        public Sniffing sniffing { get; set; }
        public StreamSettings streamSettings { get; set; }
    }
}
