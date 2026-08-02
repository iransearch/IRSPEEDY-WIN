namespace IRSpeedyVPN.Services.Hysteria
{
    public class HysteriaProfile
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public int PortEnd { get; set; }
        public string Password { get; set; }
        public string Sni { get; set; }
        public bool Insecure { get; set; }
        public string ObfsType { get; set; }
        public string ObfsPassword { get; set; }
        public string Remark { get; set; }
    }
}
