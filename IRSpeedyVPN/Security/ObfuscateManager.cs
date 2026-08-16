using System;
using System.Linq;
using System.Reflection;

namespace IRSpeedyVPN.Security
{
    internal class ObfuscateManager
    {
        public static bool IsObfucated()
        {
            Assembly cur = Assembly.GetExecutingAssembly();

            if (cur.GetCustomAttributes(false).Any(IsDotfuscatorMarker))
                return true;

            foreach (Type type in cur.GetTypes())
            {
                if (type.GetCustomAttributes(false).Any(IsDotfuscatorMarker))
                    return true;
            }

            return false;
        }

        private static bool IsDotfuscatorMarker(object attribute)
        {
            return attribute.GetType().FullName.IndexOf("Dotfuscator", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
