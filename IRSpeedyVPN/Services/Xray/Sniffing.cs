using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Xray
{
    public class Sniffing
    {
        public bool enabled { get; set; }
        public List<string> destOverride { get; set; }

        /// <summary>
        /// Keep the original destination address and use the sniffed hostname only to
        /// pick a route. Left at Xray's default (false) the sniffer *replaces* the
        /// destination with the hostname, so the remote server has to resolve a name
        /// on every connection - websites paid an extra lookup each time while
        /// IP-addressed traffic such as Telegram did not. The Android client sets this
        /// too (ExclaveBalancerConfig.inbounds).
        /// </summary>
        public bool routeOnly { get; set; }

        /// <summary>
        /// False means sniff the first payload bytes rather than metadata alone, which
        /// is what recovers the TLS SNI. Sent explicitly to match the Android config.
        /// </summary>
        public bool metadataOnly { get; set; }
    }
}
