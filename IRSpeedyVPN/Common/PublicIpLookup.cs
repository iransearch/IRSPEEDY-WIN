using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal sealed class PublicIpResult
    {
        internal IPAddress Address;
        internal string Provider;
    }

    // A bounded, display-only lookup. Every attempt uses the current VPN listener.
    internal sealed class PublicIpLookup
    {
        internal static readonly string[] Hosts =
            { "api.ipify.org", "checkip.amazonaws.com", "icanhazip.com" };
        private readonly Func<int, HttpMessageHandler> handlerFactory;
        private readonly TimeSpan attemptTimeout, totalTimeout, retryDelay;

        internal PublicIpLookup() : this(CreateHandler, TimeSpan.FromSeconds(6),
            TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(2)) { }

        internal PublicIpLookup(Func<int, HttpMessageHandler> handlerFactory,
            TimeSpan attemptTimeout, TimeSpan totalTimeout, TimeSpan retryDelay)
        {
            this.handlerFactory = handlerFactory;
            this.attemptTimeout = attemptTimeout;
            this.totalTimeout = totalTimeout;
            this.retryDelay = retryDelay;
        }

        internal static HttpClientHandler CreateHandler(int port)
        {
            ValidatePort(port);
            return new HttpClientHandler
            {
                Proxy = new WebProxy("http://127.0.0.1:" + port) { BypassProxyOnLocal = false },
                UseProxy = true, AllowAutoRedirect = false, UseCookies = false
            };
        }

        private static void ValidatePort(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        }

        internal async Task<PublicIpResult> ResolveAsync(int port, CancellationToken token,
            Action<string, string> diagnostic = null)
        {
            ValidatePort(port);
            token.ThrowIfCancellationRequested();
            Exception lastError = null;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var handler = handlerFactory(port))
            using (var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 128
            })
            {
                budget.CancelAfter(totalTimeout);
                try
                {
                    for (int round = 0; round < 2; round++)
                    {
                        if (round > 0) await Task.Delay(retryDelay, budget.Token).ConfigureAwait(false);
                        for (int index = 0; index < Hosts.Length; index++)
                        {
                            budget.Token.ThrowIfCancellationRequested();
                            string provider = Hosts[index];
                            string fields = "provider=" + provider + " round=" + (round + 1);
                            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token))
                            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://" + provider + "/"))
                            {
                                attempt.CancelAfter(attemptTimeout);
                                // Do not reuse a socket from a failed exit on the next attempt.
                                request.Headers.ConnectionClose = true;
                                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                                    { NoCache = true, NoStore = true };
                                try
                                {
                                    using (var response = await client.SendAsync(request,
                                        HttpCompletionOption.ResponseContentRead, attempt.Token).ConfigureAwait(false))
                                    {
                                        diagnostic?.Invoke("public-ip-http", fields + " status=" + (int)response.StatusCode);
                                        response.EnsureSuccessStatusCode();
                                        var address = Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                                        budget.Token.ThrowIfCancellationRequested();
                                        return new PublicIpResult { Address = address, Provider = provider };
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    budget.Token.ThrowIfCancellationRequested();
                                    lastError = new TimeoutException("Public IP provider timed out.");
                                    diagnostic?.Invoke("public-ip-attempt-failed", fields + " reason=timeout");
                                }
                                catch (Exception ex) when (ex is HttpRequestException || ex is InvalidDataException)
                                {
                                    budget.Token.ThrowIfCancellationRequested();
                                    lastError = ex;
                                    diagnostic?.Invoke("public-ip-attempt-failed", fields + " " + NetworkFailureDiagnostic.ExceptionFields(ex));
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Public IP lookup exceeded its time budget.", lastError);
                }
            }
            throw new HttpRequestException("Public IP providers did not return a valid address.", lastError);
        }

        internal static IPAddress Parse(string text)
        {
            IPAddress address;
            if (text == null || text.Length > 128 || !IPAddress.TryParse(text.Trim(), out address))
                throw new InvalidDataException("Invalid public IP response.");
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var bytes = address.GetAddressBytes();
            bool isPublic = bytes.Length == 4
                ? bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127 && bytes[0] < 224
                    && !(bytes[0] == 169 && bytes[1] == 254)
                    && !(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    && !(bytes[0] == 192 && bytes[1] == 168)
                    && !(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                : (bytes[0] & 0xe0) == 0x20;
            if (!isPublic) throw new InvalidDataException("The response is not a public IP address.");
            return address;
        }
    }
}
