using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Residential;

internal static class HttpChecks
{
    // A temporary local CA is trusted only by this test host, then removed.
    // The production GET still performs normal hostname/chain validation.
    internal static async Task Run()
    {
        using (var authorityKey = RSA.Create(2048))
        using (var serverKey = RSA.Create(2048))
        using (var trust = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
        {
            var authority = new CertificateRequest("CN=Residential offline " + Guid.NewGuid(), authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            authority.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            authority.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using (var ca = authority.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
            {
                var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost");
                request.CertificateExtensions.Add(san.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {new Oid("1.3.6.1.5.5.7.3.1")}, true));
                using (var signed = request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), new byte[]{1,2,3,4}))
                using (var certificate = signed.CopyWithPrivateKey(serverKey))
                {
                    trust.Open(OpenFlags.ReadWrite); trust.Add(ca);
                    try { await DownloadCompressed(certificate); await CancelDownload(certificate); }
                    finally { trust.Remove(ca); }
                }
            }
        }
    }
    private static async Task DownloadCompressed(X509Certificate2 certificate)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string body = "\uFEFFhysteria2://test@one.example:443#Frankfurt, Hesse, DE / ISP A#private\n"
            + "hysteria2://test@two.example:443#Berlin, Berlin, DE / ISP B#other\n"
            + "vless://id@nl.example:443#Amsterdam, North Holland, NL / ISP#private";
        byte[] compressed;
        using (var bytes = new MemoryStream())
        {
            using (var zip = new GZipStream(bytes, CompressionMode.Compress, true))
            { var data = Encoding.UTF8.GetBytes(body); zip.Write(data, 0, data.Length); }
            compressed = bytes.ToArray();
        }
        string incoming = "";
        var server = Task.Run(async () =>
        {
            using (var client = await listener.AcceptTcpClientAsync())
            using (var tls = new SslStream(client.GetStream(), false))
            {
                await tls.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
                var buffer = new byte[1024]; int read;
                while (!incoming.Contains("\r\n\r\n") && (read = await tls.ReadAsync(buffer,0,buffer.Length))>0)
                    incoming += Encoding.ASCII.GetString(buffer,0,read);
                byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Encoding: gzip\r\nContent-Length: "
                    +compressed.Length+"\r\nsubscription-userinfo: upload=10; download=20; total=100\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(headers,0,headers.Length); await tls.WriteAsync(compressed,0,compressed.Length);
            }
        });
        try
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var loaded = new TaskCompletionSource<bool>();
                var vm = new ResidentialViewModel(new IRSpeedyVPN.Models.GlobalInfo());
                vm.CatalogChanged += () => { if (vm.Services.Length>0) loaded.TrySetResult(true); };
                vm.Configure(new IRSpeedyVPN.Models.NewService.AccountInfoEx { residential_vpn = new ResidentialConfig {
                    enabled=true, subscription_url="https://localhost:"+port+"/subscription/test-token" } });
                using (deadline.Token.Register(() => loaded.TrySetCanceled())) await loaded.Task;
                if (vm.Services.Length!=2 || vm.Services[0].GetServerUrls().Count!=2 || vm.VisibleTraffic.Remaining!=70)
                    throw new Exception("HTTPS compressed Android subscription did not load");
                vm.Configure(null);
            }
            await server;
            if (!incoming.StartsWith("GET ") || incoming.IndexOf("Authorization:",StringComparison.OrdinalIgnoreCase)>=0
                || incoming.IndexOf("Accept-Encoding: gzip",StringComparison.OrdinalIgnoreCase)<0) throw new Exception("Credential-free compressed GET contract");
            Console.WriteLine("PASS real loopback HTTPS + verified certificate + gzip/BOM Android subscription loads two countries and quota");
        }
        finally { listener.Stop(); }
    }
    private static async Task CancelDownload(X509Certificate2 certificate)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var ready = new TaskCompletionSource<bool>();
        var server = Task.Run(async () =>
        {
            using (var client=await listener.AcceptTcpClientAsync())
            using (var tls=new SslStream(client.GetStream(),false))
            {
                await tls.AuthenticateAsServerAsync(certificate,false,SslProtocols.Tls12,false);
                var bytes=new byte[4096];await tls.ReadAsync(bytes,0,bytes.Length);ready.SetResult(true);
                // Wait for the aborted request to close the socket, not a sleeping worker.
                try { await tls.ReadAsync(bytes,0,bytes.Length); } catch (IOException) { }
            }
        });
        try
        {
            using (var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var download=ResidentialViewModel.DownloadAsync("https://localhost:"+port+"/subscription/test-token",cancellation.Token);
                await ready.Task; cancellation.Cancel(); bool failed=false;
                try { await download; } catch (WebException) { failed=true; } catch (OperationCanceledException) { failed=true; }
                if (!failed) throw new Exception("Canceled GET returned a subscription");
                await server;
                Console.WriteLine("PASS canceled real HTTPS subscription GET closes its socket and drains server worker");
            }
        }
        finally { listener.Stop(); }
    }
}
