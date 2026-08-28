using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace IRSpeedyVPN.Common
{
    internal static class FreePortManager
    {
        static ConcurrentQueue<int> freePorts;
        static readonly object initLock = new object();

        public static int Dequeue()
        {
            init();

            // Hand out only a port that can actually be bound. On machines where
            // Windows has reserved parts of the pool range (Hyper-V / WSL / Docker
            // dynamic ranges) binding fails with WSAEACCES, so a reserved port is
            // dropped from the pool instead of being handed to the core.
            int candidate;
            while (freePorts.TryDequeue(out candidate))
            {
                if (IsBindable(candidate))
                    return candidate;
                // candidate is permanently blocked on this machine: do not requeue it.
            }

            // Pool exhausted or fully blocked: ask the OS for a free port, which it
            // never draws from an excluded range.
            int assigned = OsAssignedPort();
            if (assigned > 0)
                return assigned;

            throw new TimeoutException("FreePortManager ran out of available ports.");
        }

        public static void Enqueue(int i)
        {
            init();
            freePorts.Enqueue(i);
        }

        static void init()
        {
            if (freePorts != null)
                return;

            lock (initLock)
            {
                if (freePorts != null)
                    return;

                var queue = new ConcurrentQueue<int>();
                foreach (int i in Enumerable.Range(7080, 200))
                    queue.Enqueue(i);
                freePorts = queue;
            }
        }

        static bool IsBindable(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { listener?.Stop(); }
                catch { }
            }
        }

        static int OsAssignedPort()
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch
            {
                return 0;
            }
            finally
            {
                try { listener?.Stop(); }
                catch { }
            }
        }
    }
}
