using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal static class ConnectionAddressReader
    {
        // Display-only snapshot from before this app's connection. Never bypass the VPN
        // to discover a physical IP while connected, and never log the returned address.
        internal static string BeforeConnection { get; set; }
        internal static async Task<string> ReadAsync(int? proxyPort, CancellationToken cancellation)
        {
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseProxy = proxyPort.HasValue;
                    if (proxyPort.HasValue)
                        handler.Proxy = new WebProxy("http://127.0.0.1:" + proxyPort.Value);
                    using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) })
                    using (var response = await client.GetAsync("https://api.ipify.org", cancellation).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var value = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                        return !cancellation.IsCancellationRequested && IPAddress.TryParse(value, out var address)
                            ? address.ToString() : null;
                    }
                }
            }
            catch { return null; } // Optional display metadata must never affect connectivity.
        }
    }
}
