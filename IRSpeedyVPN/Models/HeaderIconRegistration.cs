using System;

namespace IRSpeedyVPN.Models
{
    public sealed class HeaderIconRegistration
    {
        public HeaderIconRegistration(string icon, string toolTip, Action onClick)
        {
            Icon = icon;
            ToolTip = toolTip;
            OnClick = onClick;
        }

        public string Icon { get; }
        public string ToolTip { get; }
        public Action OnClick { get; }
    }
}
