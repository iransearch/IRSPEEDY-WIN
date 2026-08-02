
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace IRSpeedyVPN.Common.Json
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class JsonPropertyAttribute : Attribute
    {
        public JsonPropertyAttribute(string name)
        {
            Name = name;
        }

        public string Name
        {
            get;
            set;
        }
    }
}
