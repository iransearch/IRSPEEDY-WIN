using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

sealed class LocalServer : IDisposable
{
    readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    readonly X509Certificate2 certificate;
    volatile bool stopped;
    internal int Posts, Gets, Status = 200, DelayMs;
    internal bool Drop;
    internal string Json = "{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"93.184.216.34\"},{\"type\":1,\"data\":\"93.184.216.35\"}]}";
    internal string LastHost, LastBody;
    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    internal LocalServer(X509Certificate2 certificate = null)
    { this.certificate = certificate; listener.Start(); _ = Accept(); }
    async Task Accept()
    {
        while (!stopped)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); } catch { return; }
            _ = Serve(client);
        }
    }
    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                Stream stream = client.GetStream();
                if (certificate != null)
                {
                    var ssl = new SslStream(stream, false);
                    await ssl.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
                    stream = ssl;
                }
                using (stream)
                {
                    var headers = new StringBuilder(); var one = new byte[1];
                    while (headers.Length < 16384 && !headers.ToString().EndsWith("\r\n\r\n"))
                    { if (await stream.ReadAsync(one, 0, 1) == 0) return; headers.Append((char)one[0]); }
                    string head = headers.ToString(); int length = 0;
                    foreach (var line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
                        if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) LastHost = line.Substring(5).Trim();
                    }
                    using (var payload = new MemoryStream())
                    {
                        while (length-- > 0)
                        { if (await stream.ReadAsync(one, 0, 1) == 0) return; payload.WriteByte(one[0]); }
                        LastBody = Encoding.UTF8.GetString(payload.ToArray());
                    }
                    if (head.StartsWith("POST ")) Interlocked.Increment(ref Posts); else Interlocked.Increment(ref Gets);
                    if (DelayMs > 0) await Task.Delay(DelayMs);
                    if (Drop) return;
                    byte[] body = Encoding.UTF8.GetBytes(certificate == null ? Json : "{\"ok\":true}");
                    byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 " + Status + " Result\r\nConnection: close\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\n\r\n");
                    await stream.WriteAsync(response, 0, response.Length); await stream.WriteAsync(body, 0, body.Length);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is AuthenticationException || ex is ObjectDisposedException) { }
        }
    }
    public void Dispose() { stopped = true; listener.Stop(); }
}

sealed class Certificates : IDisposable
{
    internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "irspeedy-login-ca-" + Guid.NewGuid().ToString("N"));
    internal readonly string CaPath;
    internal readonly X509Certificate2 Correct, Wrong;
    internal Certificates()
    {
        Directory.CreateDirectory(DirectoryPath);
        using (var key = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=Local login test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using (var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
            {
                CaPath = Path.Combine(DirectoryPath, "ca.pem"); File.WriteAllText(CaPath, ca.ExportCertificatePem());
                Correct = Leaf(ca, "login.test"); Wrong = Leaf(ca, "other.test");
            }
        }
    }
    static X509Certificate2 Leaf(X509Certificate2 ca, string host)
    {
        using (var key = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(host); request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using (var signed = request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid().ToByteArray()))
            using (var paired = signed.CopyWithPrivateKey(key)) return new X509Certificate2(paired.Export(X509ContentType.Pfx));
        }
    }
    public void Dispose() { Correct.Dispose(); Wrong.Dispose(); Directory.Delete(DirectoryPath, true); }
}
