using IRSpeedyVPN.Events;
using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Services
{
    internal class OpenConnectService : IVPNService
    {
        public byte Order => throw new NotImplementedException();

        public int ID => throw new NotImplementedException();

        public string Name => throw new NotImplementedException();

        public string Country => throw new NotImplementedException();

        public string[] Protocols => throw new NotImplementedException();

        public string SelectedProtocol => throw new NotImplementedException();

        public bool IsUsingProxifire => throw new NotImplementedException();

        public Type SettingType => throw new NotImplementedException();
        public bool ShowSpeedyShieldSetting => false;
        public bool IsShareActive { get; set; }
        public int? HttpPort => null;
        public int? SocksPort => null;

        public event OnConnectDisconnect onConnectDisconnect;

        public void Connect(string protocol)
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void DisconnectAll()
        {
            throw new NotImplementedException();
        }

        public bool IsRequirementAvailable()
        {
            throw new NotImplementedException();
        }

        public void ApplyShareSetting()
        {
        }
    }

}
