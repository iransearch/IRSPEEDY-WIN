using System;

namespace IRSpeedyVPN.Models.NewService
{
    [Flags]
    public enum VPNType
    {
        NONE = 0,
        NORMAL = 1,
        CHAIN = 4,
        OVERRIDE = 8,
        CHAINOVERRIDE = 12
    }

}
