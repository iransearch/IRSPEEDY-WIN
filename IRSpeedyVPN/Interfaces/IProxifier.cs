using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Interfaces
{
    interface IProxifier
    {

        event OnResult onResult;
        ProxifierType ProxyType { get; }
        void Attach(string ip, int port, string username, string password,ProxyType proxyType, ProxifierType RouteType);
        void Detach();
        bool IsAttached();

    }
}
