using IRSpeedyVPN.Common;
using IRSpeedyVPN.Events;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Interfaces
{
   public interface IVPNService
    {
        event OnConnectDisconnect onConnectDisconnect;
        byte Order { get; }
        int ID { get; }
        String Name { get; set; }
        String Country { get; }
        String CountryCode { get; }
        byte CountryIndex { get; set; }
        String[] Protocols { get; }
        String SelectedProtocol { get; }
        bool IsUsingProxifire { get; }
        ProxifierType ProxifierRuleType { get; }
        bool ProxifierWithPassword { get; }
        ProxyType ProxyType{get;}
        Type SettingType { get; }
        bool ShowSpeedyShieldSetting { get; }
        bool IsShareActive { get; set; }
        int? HttpPort { get; }
        int? SocksPort { get; }
        void ApplyShareSetting();
        //event OnProxifireRequest onProxifireRequest;
        void Connect(string protocol);
        void Disconnect();
        void DisconnectAll();
        bool IsRequirementAvailable();
        long UrlTest();
        bool IsUrlTestSupported { get; }
        long UrlTestSpeed { get; }
        Url SelectedServerUrl { get; set; }
        List<Url> GetServerUrls();


    }
}
