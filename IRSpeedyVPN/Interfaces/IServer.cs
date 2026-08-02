using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Interfaces
{
    internal interface IServer
    {
        int ID { get; }
        string Country { get; set; }
         List<Url> urls { get; }
         string Protocol { get; set; }
        
    }
}
