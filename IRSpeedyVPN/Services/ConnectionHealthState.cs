using System;

namespace IRSpeedyVPN.Services
{
    // A probe belongs to one network revision. A success from the previous Wi-Fi
    // must never turn the new network green or restore its old public IP.
    internal sealed class ConnectionHealthState
    {
        private readonly object gate = new object();
        private long revision;
        private int failures;
        private string address;
        private string status = "در حال بررسی اتصال";
        internal long Revision { get { lock (gate) return revision; } }
        internal string Address { get { lock (gate) return address; } }
        internal string Status { get { lock (gate) return status; } }
        internal void Invalidate(string message)
        {
            lock (gate) { revision++; failures = 0; address = null; status = message; }
        }
        internal bool Complete(long expected, string ip, bool networkAvailable)
        {
            lock (gate)
            {
                if (expected != revision) return false;
                address = ip;
                if (ip != null) { failures = 0; status = "متصل"; return false; }
                status = networkAvailable ? "در حال بررسی و بازیابی اتصال" : "در انتظار اتصال شبکه";
                if (!networkAvailable) { failures = 0; return false; }
                return ++failures >= 2;
            }
        }
    }
}
