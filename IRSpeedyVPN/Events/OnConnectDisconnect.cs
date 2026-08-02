using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Events
{
    public delegate void OnConnectDisconnect(IVPNService Service, bool connected, int listenport, string message);
}
