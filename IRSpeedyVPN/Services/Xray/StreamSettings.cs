namespace IRSpeedyVPN.Services.Xray
{
    public class StreamSettings
    {
        public string network { get; set; }
        public string method { get; set; }
        public string security { get; set; }
        public TlsSettings tlsSettings { get; set; }
        public RealitySettings realitySettings { get; set; }
        public XtlsSettings xtlsSettings { get; set; }
        public TcpSettings tcpSettings { get; set; }
        public KcpSettings kcpSettings { get; set; }
        public WsSettings wsSettings { get; set; }
        public HttpSettings httpSettings { get; set; }
        public QuicSettings quicSettings { get; set; }
        public GrpcSettings grpcSettings { get; set; }
        public XhttpSettings xhttpSettings { get; set; }
        public HttpupgradeSettings httpupgradeSettings { get; set; }
        public HysteriaSettings hysteriaSettings { get; set; }
        public HysteriaFinalMask finalmask { get; set; }
        public Sockopt sockopt { get; set; }
    }
}
