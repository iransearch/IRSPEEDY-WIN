using System.Windows;
using System.Windows.Controls;

namespace IRSpeedyVPN.Components.ServerListControl
{
    public class CountryPickerSelector : DataTemplateSelector
    {
        public DataTemplate SmartTemplate { get; set; }
        public DataTemplate GroupTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (((item is SmartItem))) return SmartTemplate;
            if (((item is GroupItem))) return GroupTemplate;
            return base.SelectTemplate(item, container);
        }
    }
}