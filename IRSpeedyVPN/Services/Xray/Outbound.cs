namespace IRSpeedyVPN.Services.Xray
{
    public class Outbound
    {
        public string tag { get; set; }
        public string protocol { get; set; }
        public OutboundSettings settings { get; set; }
        public StreamSettings streamSettings { get; set; }
        public Mux mux { get; set; }
    }
}
