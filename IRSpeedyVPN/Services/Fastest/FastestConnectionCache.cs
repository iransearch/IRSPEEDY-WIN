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
            IReadOnlyCollection<FastestRoute> routes,
            string username)
        {
            if (routes == null || routes.Count == 0)
                return null;

            var storedKey = Read("WinnerKey", username);
            var storedTicks = ReadLong("WinnerTime", username);
            var storedRoute = routes.FirstOrDefault(x =>
                string.Equals(x?.RouteKey, storedKey, StringComparison.Ordinal));

            if (storedRoute != null && TryReadUtc(storedTicks, out var observedAt))
            {
                var age = DateTime.UtcNow - observedAt;
                if (age >= TimeSpan.Zero && age <= ResultTtl)
                    return storedRoute;
            }

            var freshMeasured = routes
                .Where(IsFreshMeasurement)
                .OrderBy(x => x.Url.latency)
                .FirstOrDefault();
            if (freshMeasured != null)
                return freshMeasured;

            return storedRoute ?? routes.First();
        }

        public static void RememberObserved(
            FastestRoute route,
            long latency,
            string username)
        {
            if (route?.Url == null || string.IsNullOrWhiteSpace(route.RouteKey) || latency <= 0)
                return;

            route.Url.latency = latency;
            route.Url.latencychkTime = DateTime.Now;

            Write("WinnerKey", username, route.RouteKey);
            Write("WinnerPing", username, latency.ToString(CultureInfo.InvariantCulture));
            Write("WinnerTime", username, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        public static string BuildRouteKey(int serviceId, int urlId, string link)
        {
            if (serviceId > 0 && urlId > 0)
                return serviceId.ToString(CultureInfo.InvariantCulture) + ":" +
                    urlId.ToString(CultureInfo.InvariantCulture);

            var raw = Encoding.UTF8.GetBytes(link ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(raw);
                return "sha256:" + BitConverter.ToString(hash, 0, 16).Replace("-", string.Empty);
            }
        }

        private static bool IsFreshMeasurement(FastestRoute route)
        {
            if (route?.Url == null || route.Url.latency <= 0 || route.Url.latencychkTime == default(DateTime))
                return false;

            var checkedAt = route.Url.latencychkTime.Kind == DateTimeKind.Utc
                ? route.Url.latencychkTime
                : route.Url.latencychkTime.ToUniversalTime();
            var age = DateTime.UtcNow - checkedAt;
            return age >= TimeSpan.Zero && age <= ResultTtl;
        }

        private static bool TryReadUtc(long ticks, out DateTime value)
        {
            value = default(DateTime);
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;

            try
            {
                value = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
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
