using System;

namespace IRSpeedyVPN.Models
{
    public sealed class HeaderIconRegistration
    {
        public HeaderIconRegistration(string icon, string toolTip, Action onClick, bool isPrimary = false)
        {
            Icon = icon;
            ToolTip = toolTip;
            OnClick = onClick;
            IsPrimary = isPrimary;
        }

        public string Icon { get; }
        public string ToolTip { get; }
        public Action OnClick { get; }
        public bool IsPrimary { get; }
    }
}
