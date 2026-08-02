using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Services
{
    
    internal class ServiceFactory
    {
        internal List<IVPNService> Services  => services;
        List<IVPNService> services;

        [Import]
        GlobalInfo globalInfo { get; set; }       
        public ServiceFactory()
        {       

        }
        public void RenewServiceList(List<Group> groups)
        {
            services = new List<IVPNService>();
            foreach(var g in groups)
            {
                foreach(var s in g.servers)
                {
                  //  File.AppendAllLines("./link.txt", s.urls.Select(x=>x.url));
                    IVPNService vpnervice;
                    if (s.urls.Count() > 0)
                    {
                        if (s.urls.Any(x => x.url.StartsWith("https://")|| x.url.StartsWith("cisco://")))
                        {
                            vpnervice = new CiscoService(s, globalInfo);
                            vpnervice.Name = g.title;
                        }
                        else
                        {
                            vpnervice = new TunnelPlusService(s, globalInfo);
                            vpnervice.Name = g.title;
                        }
                        services.Add((IVPNService)vpnervice);
                    }
                }
            }

        }
        public void RenewServiceList(List<Server> servers)
        {
            services = new List<IVPNService>();

            foreach (Server server in servers)
            {
                
                Type type = Type.GetType(string.Format("IRSpeedyVPN.Services.{0}Service", server.Service));
                
               if (type != null)
                {
                    IVPNService vpnervice = (IVPNService)Activator.CreateInstance(type, server, globalInfo);                    
                    services.Add((IVPNService)vpnervice);
                }
            }
          

        }
    }
}
