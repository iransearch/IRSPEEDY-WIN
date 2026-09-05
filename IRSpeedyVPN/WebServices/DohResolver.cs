using IRSpeedyVPN.Common;
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
    /// cached briefly, timed-out providers enter a short cooldown, and every
    /// failure ultimately falls back to the system resolver by returning null.
    /// </summary>
    internal static class DohResolver
    {
        // IP-literal DoH endpoint. Google serves a valid cert for this address,
        // so no hostname (and no system DNS) is involved.
        private static readonly string[] Providers =
        {
            "https://8.8.8.8/resolve?type=A&name="
        };

        private const int TtlSeconds = 300;
        private const int TimeoutSeconds = 2;
        private const int ProviderCooldownSeconds = 300;

        private sealed class Entry
        {
            public string Ip;
            public DateTime ExpiresUtc;
        }

        private sealed class ProviderQueryResult
        {
            public string Ip;
            public bool TimedOut;
        }

        private static readonly Dictionary<string, Entry> _cache =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> _providerCooldownUntilUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
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
            foreach (string provider in Providers)
            {
                int cooldownRemainingSeconds;
                if (TryGetProviderCooldown(provider, out cooldownRemainingSeconds))
                {
                    WriteProviderDiagnostic(
                        provider,
                        "cooldown-skip",
                        "cooldownRemainingSeconds=" + cooldownRemainingSeconds);
                    continue;
                }

                ProviderQueryResult result = QueryProvider(provider, host);
                if (result.TimedOut)
                {
                    SetProviderCooldown(provider);
                    WriteProviderDiagnostic(
                        provider,
                        "timeout",
                        "cooldownSeconds=" + ProviderCooldownSeconds);
                    continue;
                }

                if (result.Ip != null)
                {
                    ClearProviderCooldown(provider);
                    ip = result.Ip;
                    break;
                }

                // If a provider returns no answer and did not time out, its failure is
                // likely definitive for the current environment. Avoid burning extra
                // seconds on the next provider during login startup.
                if (!result.TimedOut)
                    break;
            }

            if (ip != null)
            {
                lock (_lock)
                {
                    _cache[host] = new Entry
                    {
                        Ip = ip,
                        ExpiresUtc = DateTime.UtcNow.AddSeconds(TtlSeconds)
                    };
                }
            }

            return ip;
        }

        private static ProviderQueryResult QueryProvider(string providerUrl, string host)
        {
            var result = new ProviderQueryResult();

            try
            {
                var curl = new CurlHelper();
                var resp = curl.Send(
                    providerUrl + Uri.EscapeDataString(host),
                    "GET",
                    "Accept: application/dns-json\r\n",
                    null,
                    null,
                    TimeoutSeconds,
                    null,
                    true);

                if (resp == null || string.IsNullOrWhiteSpace(resp.Body))
                    return result;

                var root = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(resp.Body);
                if (root == null)
                    return result;

                object answerObj;
                if (!root.TryGetValue("Answer", out answerObj))
                    return result;

                var answers = answerObj as IEnumerable;
                if (answers == null)
                    return result;

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
                        result.Ip = text.Trim();
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.TimedOut = IsTimeout(ex);
            }

            return result;
        }

        private static bool IsTimeout(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is TimeoutException)
                    return true;

                var webException = current as WebException;
                if (webException != null && webException.Status == WebExceptionStatus.Timeout)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static bool TryGetProviderCooldown(
            string providerUrl,
            out int remainingSeconds)
        {
            remainingSeconds = 0;

            lock (_lock)
            {
                DateTime cooldownUntilUtc;
                if (!_providerCooldownUntilUtc.TryGetValue(
                    providerUrl, out cooldownUntilUtc))
                {
                    return false;
                }

                TimeSpan remaining = cooldownUntilUtc - DateTime.UtcNow;
                if (remaining.TotalSeconds <= 0)
                {
                    _providerCooldownUntilUtc.Remove(providerUrl);
                    return false;
                }

                remainingSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                return true;
            }
        }

        private static void SetProviderCooldown(string providerUrl)
        {
            lock (_lock)
            {
                _providerCooldownUntilUtc[providerUrl] =
                    DateTime.UtcNow.AddSeconds(ProviderCooldownSeconds);
            }
        }

        private static void ClearProviderCooldown(string providerUrl)
        {
            lock (_lock)
            {
                _providerCooldownUntilUtc.Remove(providerUrl);
            }
        }

        private static void WriteProviderDiagnostic(
            string providerUrl,
            string outcome,
            string detail)
        {
            try
            {
                string providerHost = new Uri(providerUrl).Host;
                LogHelper.WriteExLog("[DohDiagnostic] provider=" + providerHost
                    + " outcome=" + outcome + " " + detail);
            }
            catch
            {
                // Diagnostics must not change resolver behavior.
            }
        }
    }
}
