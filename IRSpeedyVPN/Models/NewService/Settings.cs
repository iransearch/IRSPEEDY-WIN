using System;

namespace IRSpeedyVPN.Models.NewService
{
    public class Settings
    {
        public int id { get; set; }
        public string support_url { get; set; }
        public string shop_url { get; set; }
        public string openvpnProfile_url { get; set; }
        public string url_test { get; set; }
        public object created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
}
