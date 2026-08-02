using IRSpeedyVPN.Resource;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IRSpeedyVPN.Services.Fastest
{
    internal static class FastestConnectionCache
    {
        private static readonly TimeSpan ResultTtl = TimeSpan.FromMinutes(2);

        public static FastestRoute SelectFallback(
            IReadOnlyList<FastestPreparedRoute> routes,
            string username)
        {
            if (routes == null || routes.Count == 0)
                return null;

            var storedUrl = Read("WinnerUrl", username);
            var storedTicks = ReadLong("WinnerTime", username);
            var stored = FindByUrl(routes, storedUrl);
            if (stored != null && storedTicks > 0)
            {
                var timestamp = new DateTime(storedTicks, DateTimeKind.Utc);
                if (DateTime.UtcNow - timestamp <= ResultTtl)
                    return stored;
            }

            var freshMeasured = routes
                .Select(x => x.Route)
                .Where(IsFreshMeasurement)
                .OrderBy(x => x.Url.latency)
                .FirstOrDefault();
            if (freshMeasured != null)
                return freshMeasured;

            if (stored != null)
                return stored;

            return routes[0].Route;
        }

        public static void Remember(FastestRoute route, long latency, string username)
        {
            if (route?.Url == null || string.IsNullOrWhiteSpace(route.Link) || latency <= 0)
                return;

            route.Url.latency = latency;
            route.Url.latencychkTime = DateTime.Now;

            Write("WinnerUrl", username, route.Link);
            Write("WinnerPing", username, latency.ToString(CultureInfo.InvariantCulture));
            Write("WinnerTime", username, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static bool IsFreshMeasurement(FastestRoute route)
        {
            if (route?.Url == null || route.Url.latency <= 0)
                return false;
            if (route.Url.latencychkTime == default(DateTime))
                return false;

            var checkedAt = route.Url.latencychkTime.Kind == DateTimeKind.Utc
                ? route.Url.latencychkTime
                : route.Url.latencychkTime.ToUniversalTime();
            var age = DateTime.UtcNow - checkedAt;
            return age >= TimeSpan.Zero && age <= ResultTtl;
        }

        private static FastestRoute FindByUrl(
            IEnumerable<FastestPreparedRoute> routes,
            string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            return routes
                .Select(x => x.Route)
                .FirstOrDefault(x => string.Equals(x.Link, url, StringComparison.OrdinalIgnoreCase));
        }

        private static long ReadLong(string name, string username)
        {
            var value = Read(name, username);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0L;
        }

        private static string Read(string name, string username)
        {
            try
            {
                return RegHelper.GetSettingValue(Key(name, username));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void Write(string name, string username, string value)
        {
            try
            {
                RegHelper.SetSettingValue(Key(name, username), value ?? string.Empty);
            }
            catch
            {
            }
        }

        private static string Key(string name, string username)
        {
            return "FastestConnection_" + Scope(username) + "_" + name;
        }

        private static string Scope(string username)
        {
            var raw = Encoding.UTF8.GetBytes(username ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(raw);
                return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
            }
        }
    }
}
