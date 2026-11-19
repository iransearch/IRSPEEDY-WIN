using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Services.SingBox;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using v2rayN.Handler;

namespace IRSpeedyVPN.Services
{
    
    internal class ServiceFactory
    {
        internal List<IVPNService> Services  => services;
        List<IVPNService> services;

        GlobalInfo globalInfo => AppServices.GlobalInfo;
        public ServiceFactory()
        {       

        }
        public void RenewServiceList(List<Group> groups)
        {
            var cached = (services ?? new List<IVPNService>())
                .SelectMany(service => service.GetServerUrls() ?? new List<Url>())
                .Where(u => u != null && !string.IsNullOrEmpty(u.url))
                .GroupBy(u => u.url, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.latencychkTime).First(), StringComparer.Ordinal);
            foreach (var url in groups.SelectMany(g => g.servers).SelectMany(s => s.urls))
            {
                if (url != null && url.url != null && cached.TryGetValue(url.url, out var previous))
                {
                    url.latency = previous.latency;
                    url.latencychkTime = previous.latencychkTime;
                }
            }
            var refreshedServices = new List<IVPNService>();
            foreach(var g in groups)
            {
                foreach(var s in g.servers)
                {
                    if (s.urls.Count == 0) continue;
                    var service = CreateGroupService(s);
                    service.Name = g.title;
                    refreshedServices.Add(service);
                }
            }
            // A malformed response/config must leave the previous usable list intact.
            services = refreshedServices;
        }

        private IVPNService CreateGroupService(ServerEx server)
        {
            if (!server.urls.Any(IsCiscoLink)) return new TunnelPlusService(server, globalInfo);

            var tunnelLinks = server.urls.Where(url => !IsCiscoLink(url)).ToList();
            if (!tunnelLinks.Any(IsUsableWireGuardLink)) return new CiscoService(server, globalInfo);

            // A compatible WireGuard peer belongs in the native country pool.
            // Keep Cisco gateways out of its display tests and connection roster.
            return new TunnelPlusService(new ServerEx
            {
                ID = server.ID,
                Country = server.Country,
                Protocol = server.Protocol,
                urls = tunnelLinks
            }, globalInfo);
        }

        private static bool IsCiscoLink(Url link) => link?.url != null
            && (link.url.StartsWith("https://", StringComparison.Ordinal)
                || link.url.StartsWith("cisco://", StringComparison.Ordinal));

        private static bool IsUsableWireGuardLink(Url link)
        {
            if (!WireGuardEndpoint.IsLink(link?.url)) return false;
            var profile = ShareHandler.ImportFromConfigLink(link.url, out _);
            if (profile == null) return false;
            try
            {
                WireGuardEndpoint.Build(profile, "proxy");
                return true;
            }
            catch (FormatException) { return false; }
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
