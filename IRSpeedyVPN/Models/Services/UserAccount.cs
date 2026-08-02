using System;

namespace IRSpeedyVPN.Models.Services
{
    public class UserAccount
    {    
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