using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SingBox
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Dns
    {
        public List<Rule> rules { get; set; }
        public List<DnsServer> servers { get; set; }
    }


}
