using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using v2rayN.Handler;

namespace IRSpeedyVPN.Services.V2Ray
{
    internal class V2RayHandler
    {
        public string GetV2RayConfig(string link,int localport)
        {
            v2rayN.Mode.Config cfg = new v2rayN.Mode.Config();
            ConfigHandler.AddBatchServers(ref cfg, link, null, null);
            cfg.inbound = new List<v2rayN.Mode.InItem>();
            cfg.inbound.Add(new v2rayN.Mode.InItem()
            {
                localPort = localport,
                protocol = "socks",
            });
            LazyConfig.Instance.SetConfig(ref cfg);
            // ConfigHandler.SaveConfig(ref cfg);
            string msg;
            return V2rayConfigHandler.GetClientConfig(cfg.vmess[0], out msg);
        }
    }
}
