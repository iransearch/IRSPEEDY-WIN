using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRSpeedyVPN.Security
{
    internal class ObfuscateManager
    {
        public static bool IsObfucated()
        {
          
          
            File.WriteAllText(".\\types.txt", "");
            foreach (object[] attributeList in GetAttributes())
            {
                foreach (object attribute in attributeList)
                {
                    File.AppendAllText(".\\types.txt", attribute.GetType().FullName + "\n");
                    if (attribute.GetType().FullName == "SmartAssembly.Attributes.PoweredByAttribute")
                    {
                        return  true;
                        
                    }
                }
            }
            return false;
        }
        static IEnumerable<object> GetAttributes()
        {
            List<object> attr = new List<object>();
            Assembly cur = Assembly.GetExecutingAssembly();
            foreach (Type type in cur.GetTypes())
            {
                yield return type.GetCustomAttributes(false);
            }
            

        }
    }
}
