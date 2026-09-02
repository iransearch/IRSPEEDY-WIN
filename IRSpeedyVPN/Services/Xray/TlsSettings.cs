using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class TlsSettings
    {
        public bool? allowInsecure { get; set; }
        public string pinnedPeerCertSha256 { get; set; }
        public string serverName { get; set; }
        public List<string> alpn { get; set; }
        public string fingerprint { get; set; }
    }
}
