using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Models.Services
{
    public class AccountInfo
    {
             
            public UserAccount UserAccount { get; set; }
            public List<Setting> Settings { get; set; }
            public List<Server> Servers { get; set; }
            public string id { get; set; }        
    }

}
