using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Models.Services
{
    public class PermissionUser
    {
        public String username { get; set; }
        public String password { get; set; }
        public string seed { get; set; }
        public PermissionUser (string username,string password,string seed=null)
        {
            this.username = username;
            this.password = password;
            this.seed = seed;
        }
    }
}
