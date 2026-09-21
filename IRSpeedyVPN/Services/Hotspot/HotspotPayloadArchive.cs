using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace IRSpeedyVPN.Services.Hotspot
{
    // Independent of WPF and the helper protocol so packaging can be tested separately.
    // The caller holds the cross-process lock and supplies an administrator-only root.
    internal sealed class HotspotPayloadArchive
    {
        private const string Executable = "IRSpeedyHotspotHelper.exe";
        private readonly Func<Stream> openArchive;
        private readonly string version;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private sealed class Entry
        {
            internal long Length;
            internal string Hash;
        }

        internal HotspotPayloadArchive(Func<Stream> openArchive)
        {
            this.openArchive = openArchive;
            using (var stream = openArchive())
            {
                if (stream == null) throw new FileNotFoundException("Embedded hotspot payload is missing.");
                version = Hash(stream);
            }
            using (var stream = openArchive())
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string relative = RelativePath(entry.FullName);
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    if (entries.ContainsKey(relative)) throw new InvalidDataException("Duplicate hotspot payload entry.");
                    using (var source = entry.Open())
                        entries.Add(relative, new Entry { Length = entry.Length, Hash = Hash(source) });
                }
            }
            foreach (string required in new[] { Executable, "IRSpeedyHotspotHelper.dll",
                "IRSpeedyHotspotHelper.deps.json", "IRSpeedyHotspotHelper.runtimeconfig.json",
                "hostfxr.dll", "hostpolicy.dll", "coreclr.dll" })
                if (!entries.ContainsKey(required)) throw new InvalidDataException("Incomplete self-contained hotspot payload.");
        }

        internal string Prepare(string root)
        {
            string destination = Path.Combine(root, version);
            if (Valid(destination)) return Path.Combine(destination, Executable);

            string staging = Path.Combine(root, version + ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using (var stream = openArchive())
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        string relative = RelativePath(entry.FullName);
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                        string path = Path.Combine(staging, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var source = entry.Open())
                        using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            source.CopyTo(target);
                            target.Flush(true);
                        }
                    }
                }
                if (!Valid(staging)) throw new InvalidDataException("Hotspot extraction verification failed.");
                // Never patch an active runtime in place. Missing or altered files are
                // repaired by publishing a completely verified replacement directory.
                if (Directory.Exists(destination))
                {
                    RejectReparsePoint(destination);
                    string obsolete = destination + ".invalid-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, obsolete);
                    TryDelete(obsolete);
                }
                Directory.Move(staging, destination);
                return Path.Combine(destination, Executable);
            }
            finally { TryDelete(staging); }
        }

        private bool Valid(string directory)
        {
            if (!Directory.Exists(directory)) return false;
            RejectReparsePoint(directory);
            int count = 0;
            if (!ValidDirectory(directory, "", ref count)) return false;
            return count == entries.Count;
        }

        private bool ValidDirectory(string directory, string prefix, ref int count)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectReparsePoint(path);
                string relative = Path.Combine(prefix, Path.GetFileName(path));
                if (Directory.Exists(path))
                {
                    if (!ValidDirectory(path, relative, ref count)) return false;
                    continue;
                }
                Entry expected;
                if (!entries.TryGetValue(relative, out expected)) return false;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (file.Length != expected.Length || Hash(file) != expected.Hash) return false;
                count++;
            }
            return true;
        }

        private static string RelativePath(string name)
        {
            // Validate Windows paths even when the portable tests run on Linux.
            string normalized = name.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException("Unsafe hotspot payload path.");
            string trimmed = normalized.TrimEnd('/');
            foreach (string part in trimmed.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                    throw new InvalidDataException("Unsafe hotspot payload path.");
                foreach (char c in part) if (char.IsControl(c)) throw new InvalidDataException("Unsafe hotspot payload path.");
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] >= '0' && stem[3] <= '9'))
                    throw new InvalidDataException("Unsafe hotspot payload path.");
            }
            return trimmed.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string Hash(Stream source)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsafe hotspot runtime link.");
        }

        private static void TryDelete(string path)
        {
            // Only remove our staging/quarantined directories; never follow links.
            try
            {
                if (!Directory.Exists(path)) return;
                RejectReparsePoint(path);
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                {
                    RejectReparsePoint(entry);
                    if (Directory.Exists(entry)) TryDelete(entry);
                    else File.Delete(entry);
                }
                Directory.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
