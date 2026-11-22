using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;

class Program
{
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static byte[] Identity()
    {
        var data = new List<byte> { 8, 1 };
        Action<int, string> field = (n, value) => { var bytes = Encoding.UTF8.GetBytes(value); data.Add((byte)(n * 8 + 2)); data.Add((byte)bytes.Length); data.AddRange(bytes); };
        field(2, "1.13.0"); field(3, "26.10.1"); field(4, "abc123"); field(5, "false"); field(6, "windows/amd64");
        data.AddRange(new byte[] { 56, 123 }); field(8, "go1.26.7");
        // Unknown future fields must be ignored.
        data.AddRange(new byte[] { 72, 1 });
        return data.ToArray();
    }
    static async Task Main()
    {
        var old = LibcoreProto.DecodeCoreBuildInfo(new byte[] { 8, 1 });
        Check(old.ProcessId == 0 && old.Commit == null && old.HasPrivilege, "legacy response retains privilege without inventing a commit");
        var info = LibcoreProto.DecodeCoreBuildInfo(Identity());
        Check(info.SingBoxVersion == "1.13.0" && info.XrayVersion == "26.10.1" && info.Commit == "abc123"
            && info.Modified == "false" && info.Architecture == "windows/amd64" && info.ProcessId == 123 && info.GoVersion == "go1.26.7" && info.HasPrivilege, "identity wire contract and future-field compatibility");
        var mismatch = CoreRuntimeIdentity.Capture(info, 456);
        Check(mismatch.Pid == -1 && mismatch.Commit == "unknown", "different listener cannot receive RPC process identity");
        Check(mismatch.Privileged == "unknown" && CoreRuntimeIdentity.Capture(info, 0).Privileged == "true"
            && CoreRuntimeIdentity.Capture(LibcoreProto.DecodeCoreBuildInfo(new byte[0]), -1).Privileged == "false", "Core privilege uses the existing RPC field without guessing a different process");
        Check(CoreRuntimeIdentity.Token("abc\nforged=value") == "unknown", "untrusted metadata cannot inject report lines");
        var empty = new CoreRuntimeIdentity();
        var report = empty.Failure("config password=secret unknown outbound type: auto-selector-round-robin");
        Check(report.Contains("شروع سرویس اتصال با خطا مواجه شد.") && report.Contains("Core commit: unknown") && !report.Contains("secret"), "actionable report keeps missing identity explicit and excludes config secrets");
        Check(empty.Failure("startup-timeout").Contains("مهلت"), "timeout retains its distinct reason");
        Check(CoreRuntimeIdentity.Capture(old, 0).RpcMetadata == "not-reported"
            && CoreRuntimeIdentity.Capture(info, 0).RpcMetadata == "reported"
            && mismatch.RpcMetadata == "pid-mismatch", "identity log distinguishes a legacy reply from a mismatched or extended reply");
        PackagedIdentityChecks(info);
        StartupFailureChecks.Run(Check);
        await WireQuery(false, true);
        await WireQuery(false);
        await WireQuery(true);
    }
    static void PackagedIdentityChecks(CoreBuildInfo info)
    {
        const string executable = "SGuard64.exe";
        const string path = "V-Guard/SGuard64.exe";
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        byte[] package = CreatePackage(("SGuard64.exe", "decoy"), ("V-Guard/SGuard32.exe", "other core"), (path, "abc"));
        string packaged = CoreRuntimeIdentity.ReadPackagedCoreHash(package, executable);
        Check(packaged == expected, "expected digest hashes the selected executable bytes inside the ZIP");
        Check(CoreRuntimeIdentity.ReadPackagedCoreHash(package, "SGuard764.exe") == null,
            "a missing platform-specific executable has no expected digest");
        Check(CoreRuntimeIdentity.ReadPackagedCoreHash(CreatePackage((path, "abc"), ("v-guard\\.\\sguard64.exe", "other")), executable) == null,
            "ambiguous archive paths have no expected digest");

        var identity = CoreRuntimeIdentity.Capture(null, -1, packaged);
        Check(identity.PackageMatch == "unknown", "unavailable runtime digest leaves the comparison unknown");
        identity.Sha256 = expected.ToUpperInvariant();
        Check(identity.PackageMatch == "match" && identity.LogDetails.Contains("Packaged Core SHA256: " + expected)
            && identity.LogDetails.Contains("Core package match: match"), "matching digests and the expected hash are logged together");
        identity.Sha256 = new string('a', 64);
        Check(identity.PackageMatch == "mismatch" && identity.LogDetails.Contains("Core package match: mismatch"),
            "different runtime and packaged executables are distinguished");
        identity = CoreRuntimeIdentity.Capture(info, 456, packaged);
        Check(identity.PackagedSha256 == expected && identity.PackageMatch == "unknown",
            "PID mismatch keeps the package evidence without comparing against another process");
    }

    static byte[] CreatePackage(params (string Path, string Contents)[] files)
    {
        using (var buffer = new MemoryStream())
        {
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    byte[] contents = Encoding.UTF8.GetBytes(file.Contents);
                    using (var entry = archive.CreateEntry(file.Path).Open())
                        entry.Write(contents, 0, contents.Length);
                }
            }
            return buffer.ToArray();
        }
    }

    static async Task WireQuery(bool stall, bool health = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var accepted = listener.AcceptTcpClientAsync();
            var call = Task.Run(() => {
                var client = new LibcoreServiceClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
                return health ? (object)client.IsPrivileged() : client.QueryBuildInfo(400, CancellationToken.None);
            });
            using (var peer = await accepted)
            {
                var stream = peer.GetStream();
                // Request frames are short; fully consume both before responding.
                int length = stream.ReadByte(); var header = new byte[length];
                for (int n = 0; n < length;) n += stream.Read(header, n, length - n);
                Check(Encoding.UTF8.GetString(header).Contains("LibcoreService.IsPrivileged"), "query addresses the answering Core handshake");
                Check(stream.ReadByte() == 0, "query has empty request");
                if (!stall)
                {
                    var body = Identity(); var response = new byte[body.Length + 4]; response[0] = 2; response[1] = 24; response[2] = (byte)body.Length; response[3] = (byte)body.Length; Array.Copy(body, 0, response, 4, body.Length);
                    await stream.WriteAsync(response, 0, response.Length);
                }
                Check(await Task.WhenAny(call, Task.Delay(2000)) == call, "optional identity RPC remains bounded");
                if (stall) { try { await call; throw new Exception("stall succeeded"); } catch (IOException) { } }
                else if (health) Check(((IsPrivilegedResponse)await call).HasPrivilege, "startup health probe accepts extended identity fields");
                else Check(((CoreBuildInfo)await call).Commit == "abc123", "running Core metadata reaches client");
            }
        }
        finally { listener.Stop(); }
    }
}

