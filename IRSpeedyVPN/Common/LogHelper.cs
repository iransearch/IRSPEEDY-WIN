using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Common
{
    public static class LogHelper
    {
        public static void WriteExLog(string Message)
        {
            // Kept as a separate entry point, but writes to the single log.txt so
            // there is only one log file to collect.
            lock (Locks.Log)
                File.AppendAllText(".\\log.txt", string.Format("{0:s} : {1}\n", DateTime.Now, Message.Replace("api1.isdm.ir", "[ServerUrl]").Replace("apichcek-p.isdm.ir", "[ProxyUrl]")));

        }
        public static void WriteLog(string Message)
        {
            lock (Locks.Log)
                File.AppendAllText(".\\log.txt", string.Format("{0:s} : {1}\n", DateTime.Now, Message.Replace("api1.isdm.ir", "[ServerUrl]").Replace("apichcek-p.isdm.ir", "[ProxyUrl]")));
            
        }
        public static void WriteLog(Exception ex,bool isAppCrash=false)
        {
            File.AppendAllText(".\\log.txt", string.Format("{0:s} :{3} {1}\n{2}\n", DateTime.Now, ex.Message.Replace("api1.isdm.ir", "[ServerUrl]").Replace("apichcek-p.isdm.ir", "[ProxyUrl]"), ex.StackTrace, isAppCrash ? "[Crashed]" : ""));
        }
    }
}
