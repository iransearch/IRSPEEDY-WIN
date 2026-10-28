using System;

namespace IRSpeedyVPN.Models.Services
{
    public class UserAccount
    {    
        public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn { get; set; }
        public object GroupID { get; set; }
        public object group_id { get; set; }
        public object pid { get; set; }
        public string group_name { get; set; }
        public string Trafic { get; set; }
        public string ServerTime { get; set; }
        public string UserID { get; set; }
        public string GroupName { get; set; }
        public string Status { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string ExpireTime { get; set; }
        public string FirstLogin { get; set; }
        public string Username { get; set; }
        public string ExpireAt { get; set; }
        
    }

}
