using System;
using System.IO;
using System.Security.Cryptography;

namespace IRSpeedyVPN.Services.Xray
{
    // A single-file distribution still needs a real file for Windows LoadLibrary.
    // Materialize only the current PROCESS architecture, once on first gRPC use.
    // There is no download and no dependency on the executable's writable folder.
    internal static class NativeGrpcRuntime
    {
        private static readonly Lazy<string> prepared = new Lazy<string>(() =>
        {
            bool is64Bit = Environment.Is64BitProcess;
            string architecture = is64Bit ? "x64" : "x86";
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IRSpeedy", "grpc");
            string path = Extract(() => OpenResource(architecture), root, is64Bit);
            // Grpc.Core 2.46.6 reads this before initializing its native extension.
            // Called before constructing any Channel/Server, including when the
            // managed Grpc.Core assembly was loaded from memory by Costura/SmartAssembly.
            Environment.SetEnvironmentVariable("GRPC_CSHARP_EXT_OVERRIDE_LOCATION", path);
            IRSpeedyVPN.Common.LogHelper.WriteExLog("[StartupRoute] stage=native-ready architecture=" + architecture);
            return path;
        });

        internal static string Prepare() => prepared.Value;

        private static Stream OpenResource(string architecture)
        {
            var stream = typeof(NativeGrpcRuntime).Assembly.GetManifestResourceStream("IRSpeedy.NativeGrpc." + architecture);
            if (stream == null)
                throw new FileNotFoundException("Embedded gRPC resource missing for " + architecture
                    + ". Preserve IRSpeedy.NativeGrpc resources in the protected executable.");
            return stream;
        }

        internal static string Extract(Func<Stream> openResource, string root, bool is64Bit)
        {
            string hash;
            using (var source = openResource()) hash = Hash(source);
            string directory = Path.Combine(root, hash);
            string path = Path.Combine(directory, is64Bit ? "grpc_csharp_ext.x64.dll" : "grpc_csharp_ext.x86.dll");
            if (Matches(path, hash)) return path;

            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var source = openResource())
                using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                    destination.Flush(true);
                }
                if (!Matches(temporary, hash)) throw new InvalidDataException("Embedded gRPC extraction hash mismatch.");
                try { File.Move(temporary, path); }
                catch (IOException)
                {
                    // Another process may have completed extraction while we copied.
                    // A damaged cache is replaced atomically; never load a partial DLL.
                    if (!Matches(path, hash))
                    {
                        try { File.Replace(temporary, path, null); }
                        catch (IOException) { if (!Matches(path, hash)) throw; }
                    }
                }
                if (!Matches(path, hash)) throw new InvalidDataException("Cached gRPC native library hash mismatch.");
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool Matches(string path, string expected)
        {
            if (!File.Exists(path)) return false;
            using (var stream = File.OpenRead(path)) return Hash(stream) == expected;
        }

        private static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
