using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal static class TimeZoneMapping
    {
        private static readonly Lazy<Dictionary<string, string>> Zones = new Lazy<Dictionary<string, string>>(Read);
        internal static string ToWindows(string ianaId)
        {
            string result;
            if (ianaId == null || !Zones.Value.TryGetValue(ianaId, out result))
                throw new InvalidDataException("Unknown IP time zone.");
            return result;
        }
        private static Dictionary<string, string> Read()
        {
            var zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IRSpeedyVPN.TimeZones.tsv"))
            using (var reader = new StreamReader(stream ?? throw new InvalidDataException("Time-zone mapping is unavailable.")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var fields = line.Split('\t');
                    if (fields.Length != 2) throw new InvalidDataException("Invalid time-zone mapping.");
                    zones.Add(fields[0], fields[1]);
                }
            }
            return zones;
        }
    }

    internal sealed class TimeZoneLookup : ITimeZoneLookup
    {
        internal const string Host = "ipwho.is";
        internal static HttpClientHandler CreateHandler(int proxyPort)
        {
            if (proxyPort < 1 || proxyPort > 65535) throw new ArgumentOutOfRangeException(nameof(proxyPort));
            return new HttpClientHandler
            {
                Proxy = new WebProxy("http://127.0.0.1:" + proxyPort), UseProxy = true,
                AllowAutoRedirect = false, UseCookies = false
            };
        }
        public async Task<TimeZoneLocation> ResolveAsync(int proxyPort, CancellationToken token)
        {
            using (var handler = CreateHandler(proxyPort))
            using (var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 16384
            })
            using (var response = await client.GetAsync("https://" + Host + "/?fields=success,ip,timezone.id", token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
        internal static TimeZoneLocation Parse(string json)
        {
            if (json == null || json.Length > 16384) throw new InvalidDataException("Invalid GeoIP response.");
            JObject root;
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8 })
                root = JObject.Load(reader);
            IPAddress address;
            // IP and zone come from one response: a later Round-robin request may exit elsewhere.
            if (root["success"]?.Type != JTokenType.Boolean || (bool)root["success"] != true
                || root["ip"]?.Type != JTokenType.String || !IPAddress.TryParse((string)root["ip"], out address)
                || !IsPublic(address) || root["timezone"]?["id"]?.Type != JTokenType.String)
                throw new InvalidDataException("GeoIP did not identify a VPN exit time zone.");
            var id = (string)root["timezone"]["id"];
            return new TimeZoneLocation { IanaId = id, WindowsId = TimeZoneMapping.ToWindows(id) };
        }
        private static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return false;
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
                return bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127 && bytes[0] < 224
                    && !(bytes[0] == 169 && bytes[1] == 254)
                    && !(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    && !(bytes[0] == 192 && bytes[1] == 168)
                    && !(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
            return (bytes[0] & 0xe0) == 0x20; // Globally routable IPv6 unicast (2000::/3).
        }
    }
}
