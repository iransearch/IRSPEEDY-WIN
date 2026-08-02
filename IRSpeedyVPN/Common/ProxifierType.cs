using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Common
{
    public enum ProxifierType : byte
    {
        [Description("")]
        Normal = 0,
        [Description("System Proxy")]
        None = 1,
        [Description("Telegram")]
        Telegram = 2,       
        [Description("Global")]
        Global = 3

    }
}
