using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class XtlsSettings
    {
        public bool allowInsecure { get; set; }
        public string serverName { get; set; }
        public List<string> alpn { get; set; }
        public string fingerprint { get; set; }
    }
}
