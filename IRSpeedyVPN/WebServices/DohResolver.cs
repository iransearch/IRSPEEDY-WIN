using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    /// <summary>
    /// Resolves API hostnames through DNS-over-HTTPS so a poisoned or blocked
    /// system resolver cannot stop the client from reaching the login servers.
    ///
    /// The DoH endpoints are addressed by IP literal, so they need no bootstrap
    /// DNS of their own, and their TLS certificates cover those IPs. Results are
    /// cached briefly, and every failure falls back to the system resolver by
    /// returning null, so DoH can only help the connection, never break it.
    /// </summary>
    internal static class DohResolver
    {
        // IP-literal DoH endpoints. Cloudflare and Google both serve valid certs
        // for these addresses, so no hostname (and no system DNS) is involved.
        private static readonly string[] Providers =
        {
            "https://1.1.1.1/dns-query?type=A&name=",
            "https://8.8.8.8/resolve?type=A&name="
        };

        private const int TtlSeconds = 300;
        private const int TimeoutSeconds = 6;

        private sealed class Entry
        {
            public string Ip;
            public DateTime ExpiresUtc;
        }

        private static readonly Dictionary<string, Entry> _cache =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        /// <summary>
        /// Returns an IPv4 address for <paramref name="host"/> resolved over DoH,
        /// or null when the caller should fall back to the system resolver.
        /// </summary>
        public static string Resolve(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            // Already an IP literal: nothing to resolve.
            IPAddress literal;
            if (IPAddress.TryParse(host, out literal))
                return null;

            lock (_lock)
            {
                Entry cached;
                if (_cache.TryGetValue(host, out cached) && cached.ExpiresUtc > DateTime.UtcNow)
                    return cached.Ip;
            }

            string ip = null;
            foreach (var provider in Providers)
            {
                ip = QueryProvider(provider, host);
                if (ip != null)
                    break;
            }

            if (ip != null)
            {
                lock (_lock)
                {
                    _cache[host] = new Entry { Ip = ip, ExpiresUtc = DateTime.UtcNow.AddSeconds(TtlSeconds) };
                }
            }

            return ip;
        }

        private static string QueryProvider(string providerUrl, string host)
        {
            try
            {
                var curl = new CurlHelper();
                var resp = curl.Send(
                    providerUrl + Uri.EscapeDataString(host),
                    "GET",
                    "Accept: application/dns-json\r\n",
                    null,
                    null,
                    TimeoutSeconds);

                if (resp == null || string.IsNullOrWhiteSpace(resp.Body))
                    return null;

                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(resp.Body);
                if (root == null)
                    return null;

                object answerObj;
                if (!root.TryGetValue("Answer", out answerObj))
                    return null;

                var answers = answerObj as IEnumerable;
                if (answers == null)
                    return null;

                foreach (var item in answers)
                {
                    var record = item as Dictionary<string, object>;
                    if (record == null)
                        continue;

                    object typeObj;
                    record.TryGetValue("type", out typeObj);

                    object dataObj;
                    record.TryGetValue("data", out dataObj);
                    var text = dataObj == null ? null : dataObj.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    // type 1 == A record. Some providers omit it; accept a valid
                    // IPv4 either way.
                    bool isARecord = typeObj == null || typeObj.ToString() == "1";
                    IPAddress parsed;
                    if (isARecord
                        && IPAddress.TryParse(text.Trim(), out parsed)
                        && parsed.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return text.Trim();
                    }
                }
            }
            catch
            {
                // Any failure falls back to the system resolver.
            }

            return null;
        }
    }
}
