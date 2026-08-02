using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    public static class Locks
    {
        public static object UrlTest =new object();
        public static object Log = new object();
    }
}
