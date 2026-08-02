using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SingBox
{


    public class Transport
    {
        public Transport()
        {
            headers = new Dictionary<string, object>();
        }

        public string type { get; set; }
        public string path { get; set; }
        public List<string> host { get; set; }
        public Dictionary<string,object> headers { get; set; }
        public int? max_early_data { get; set; }
        public string early_data_header_name { get; set; }
        public string service_name { get; set; }
        public string method { get; set; }
        public string mode { get; set; }
        public object extra { get; set; }
    }


}
