using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    // Cache only public IPv4/IPv6 answer sets, never login retry state. Reference
    // comparison prevents an old failing caller from evicting a newer answer.
    internal sealed class LoginDnsCache : ILoginPrefetchResolver
    {
        private sealed class Entry { internal string[] Values; internal DateTime Expires; }
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private readonly Func<string, int, CancellationToken, string[]> fetch;
        internal LoginDnsCache(Func<string, int, CancellationToken, string[]> fetch) { this.fetch = fetch; }
        public LoginAddresses Resolve(string host, int timeoutMs, CancellationToken cancellation, bool refresh)
            => ResolveCore(host, timeoutMs, cancellation, refresh, true);
        public LoginAddresses Prefetch(string host, int timeoutMs, CancellationToken cancellation)
            => ResolveCore(host, timeoutMs, cancellation, false, false);
        private LoginAddresses ResolveCore(string host, int timeoutMs, CancellationToken cancellation, bool refresh, bool commit)
        {
            cancellation.ThrowIfCancellationRequested();
            if (timeoutMs <= 0) return null;
            lock (gate)
            {
                Entry entry;
                if (!refresh && cache.TryGetValue(host, out entry) && entry.Expires > DateTime.UtcNow)
                    return new LoginAddresses(entry.Values, true);
            }
            var values = (fetch(host, Math.Min(LoginDnsRace.LookupBudgetMs, timeoutMs), cancellation) ?? new string[0])
                .Where(IsPublicAddress).Select(value => IPAddress.Parse(value).ToString()).Distinct().ToArray();
            cancellation.ThrowIfCancellationRequested();
            if (values.Length == 0) return null;
            var answer = new LoginAddresses(values, false);
            if (commit) Accept(host, answer);
            return answer;
        }
        public void Accept(string host, LoginAddresses answer)
        { if (!answer.FromCache) lock (gate) cache[host] = new Entry { Values = answer.Values, Expires = DateTime.UtcNow.AddMinutes(5) }; }
        public void Invalidate(string host, LoginAddresses answer)
        {
            lock (gate)
            {
                Entry entry;
                if (cache.TryGetValue(host, out entry) && ReferenceEquals(entry.Values, answer.Values)) cache.Remove(host);
            }
        }
        internal static bool IsPublicAddress(string value)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(value, out ip)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetwork) return IsPublicIPv4(value);
            if (ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.ScopeId != 0) return false;
            var b = ip.GetAddressBytes();
            return (b[0] & 0xe0) == 0x20
                && !(b[0] == 0x20 && b[1] == 1 && b[2] == 0x0d && b[3] == 0xb8);
        }
        internal static bool IsPublicIPv4(string value)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(value, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                && !(b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }
    }
}
