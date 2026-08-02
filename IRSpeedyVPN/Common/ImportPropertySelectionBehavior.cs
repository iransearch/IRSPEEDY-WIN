using SimpleInjector.Advanced;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRSpeedyVPN.Common
{
    class ImportPropertySelectionBehavior : IPropertySelectionBehavior
    {
        public bool SelectProperty(Type implementationType, PropertyInfo prop) =>
            prop.GetCustomAttributes(typeof(ImportAttribute),true).Any();
    }
}
