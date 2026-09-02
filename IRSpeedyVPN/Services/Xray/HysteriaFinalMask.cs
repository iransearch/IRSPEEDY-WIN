using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class HysteriaFinalMask
    {
        public List<HysteriaFinalMaskLayer> udp { get; set; }
        public HysteriaQuicParams quicParams { get; set; }
    }

    public class HysteriaFinalMaskLayer
    {
        public string type { get; set; }
        public HysteriaFinalMaskLayerSettings settings { get; set; }
    }

    public class HysteriaFinalMaskLayerSettings
    {
        public string password { get; set; }
    }

    public class HysteriaQuicParams
    {
        public HysteriaUdpHop udpHop { get; set; }
    }

    public class HysteriaUdpHop
    {
        public string ports { get; set; }
        public int? interval { get; set; }
    }
}
