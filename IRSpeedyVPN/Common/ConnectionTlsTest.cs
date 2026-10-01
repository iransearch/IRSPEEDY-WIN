using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal static class ConnectionTlsTest
    {
        internal static async Task<bool> CheckAsync(string host, int timeoutMs, int? httpPort,
            CancellationToken cancellation)
        {
            if (Uri.CheckHostName(host) != UriHostNameType.Dns)
                throw new ArgumentException("A DNS hostname is required.", nameof(host));
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (httpPort.HasValue && (httpPort.Value <= 0 || httpPort.Value > 65535))
                throw new ArgumentOutOfRangeException(nameof(httpPort));

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = new TcpClient())
            {
                deadline.CancelAfter(timeoutMs);
                // Closing the socket also interrupts .NET Framework's connect and TLS
                // APIs, which do not accept a CancellationToken.
                using (deadline.Token.Register(() => client.Close()))
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    await client.ConnectAsync(httpPort.HasValue ? "127.0.0.1" : host,
                        httpPort ?? 443).ConfigureAwait(false);
                    var stream = client.GetStream();
                    if (httpPort.HasValue)
                    {
                        // Resolve the destination through the active proxy, not local
                        // DNS. Never fall back to a direct connection if CONNECT fails.
                        var request = Encoding.ASCII.GetBytes("CONNECT " + host + ":443 HTTP/1.1\r\nHost: "
                            + host + ":443\r\n\r\n");
                        await stream.WriteAsync(request, 0, request.Length, deadline.Token).ConfigureAwait(false);
                        await ReadConnectResponseAsync(stream, deadline.Token).ConfigureAwait(false);
                    }

                    // SslStream validates the certificate chain and hostname itself;
                    // no global HttpWebRequest certificate callbacks are used.
                    using (var tls = new SslStream(stream, false))
                    {
                        await tls.AuthenticateAsClientAsync(host, new X509CertificateCollection(),
                            SslProtocols.Tls12, true).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        LogHelper.WriteExLog("[ConnectionTest] host=" + host + " test=tcp-tls route="
                            + (httpPort.HasValue ? "http-proxy:" + httpPort.Value : "system-tunnel")
                            + " result=confirmed");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    cancellation.ThrowIfCancellationRequested();
                    LogHelper.WriteExLog("[ConnectionTest] host=" + host + " test=tcp-tls route="
                        + (httpPort.HasValue ? "http-proxy:" + httpPort.Value : "system-tunnel")
                        + " result=failed error=" + (deadline.IsCancellationRequested ? "Timeout" : ex.GetType().Name));
                    return false;
                }
            }
        }

        private static async Task ReadConnectResponseAsync(Stream stream, CancellationToken cancellation)
        {
            var header = new StringBuilder();
            var buffer = new byte[1];
            // Read only the HTTP header; consuming tunnel bytes would break TLS.
            while (header.Length < 8192)
            {
                if (await stream.ReadAsync(buffer, 0, 1, cancellation).ConfigureAwait(false) == 0)
                    throw new IOException("Proxy closed before completing CONNECT.");
                header.Append((char)buffer[0]);
                int length = header.Length;
                if (length < 4 || header[length - 4] != '\r' || header[length - 3] != '\n'
                    || header[length - 2] != '\r' || header[length - 1] != '\n') continue;

                var status = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)[0]
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int code;
                if (status.Length < 2 || (status[0] != "HTTP/1.1" && status[0] != "HTTP/1.0")
                    || !int.TryParse(status[1], out code) || code < 200 || code >= 300)
                    throw new IOException("Proxy rejected CONNECT.");
                return;
            }
            throw new IOException("Proxy CONNECT header is too large.");
        }
    }
}
