using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal static class FreePortManager
    {
        static ConcurrentQueue<int> freePorts;
        const int DequeueTimeoutMs = 10000;
        public static int Dequeue()
        {
            init();
            int ret;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (! freePorts.TryDequeue(out ret))
            {
                if (sw.ElapsedMilliseconds > DequeueTimeoutMs)
                    throw new TimeoutException("FreePortManager ran out of available ports.");
                Thread.Sleep(100);
            }
            return ret;
        }
        public static void Enqueue(int i)
        {
            init();
            freePorts.Enqueue(i);
        }
        static void init()
        {
            if (freePorts == null)
            {
                freePorts = new ConcurrentQueue<int>();
                foreach(int i in Enumerable.Range(7080, 200))
                freePorts.Enqueue(i);
            }
        }

    }
}
