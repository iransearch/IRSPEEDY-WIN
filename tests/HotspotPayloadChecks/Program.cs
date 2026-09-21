using System.IO.Compression;
using IRSpeedyVPN.Services.Hotspot;

class Program
{
    private static int checks;
    private static readonly string[] Required = { "IRSpeedyHotspotHelper.exe", "IRSpeedyHotspotHelper.dll",
        "IRSpeedyHotspotHelper.deps.json", "IRSpeedyHotspotHelper.runtimeconfig.json", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll" };

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    static byte[] Payload(string revision, string extra = "subdirectory/dependency.dll", bool complete = true)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (string name in (complete ? Required : Required.Take(2)).Append(extra))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(revision + ":" + name);
            }
        return output.ToArray();
    }

    static HotspotPayloadArchive Archive(byte[] bytes) => new(() => new MemoryStream(bytes, false));
    static void Rejected(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "HotspotPayloadChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Archive(Payload("x64-v1"));
            string helper = archive.Prepare(root);
            string directory = Path.GetDirectoryName(helper);
            Check(File.ReadAllText(helper) == "x64-v1:IRSpeedyHotspotHelper.exe", "standalone extraction");
            Check(File.Exists(Path.Combine(directory, "subdirectory", "dependency.dll")), "nested dependencies");
            var sentinelTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(helper, sentinelTime);
            Check(archive.Prepare(root) == helper && File.GetLastWriteTimeUtc(helper) == sentinelTime, "reuse without rewriting helper");
            File.Delete(Path.Combine(directory, "hostfxr.dll"));
            archive.Prepare(root);
            Check(File.Exists(Path.Combine(directory, "hostfxr.dll")), "repair missing native runtime");
            File.WriteAllText(helper, "x64-v0:IRSpeedyHotspotHelper.exe");
            archive.Prepare(root);
            Check(File.ReadAllText(helper) == "x64-v1:IRSpeedyHotspotHelper.exe", "repair same-size corruption");
            File.WriteAllText(Path.Combine(directory, "unexpected.dll"), "stale dependency");
            archive.Prepare(root);
            Check(!File.Exists(Path.Combine(directory, "unexpected.dll")), "remove unbundled dependency");
            string updated = Archive(Payload("x64-v2")).Prepare(root);
            Check(updated != helper && File.Exists(helper), "upgrade does not overwrite previous runtime");
            string x86 = Archive(Payload("x86-v1")).Prepare(root);
            Check(x86 != helper && File.ReadAllText(x86).StartsWith("x86-v1:"), "architectures have separate content caches");
            foreach (string unsafePath in new[] { "../escape.exe", "..\\escape.exe", "/absolute.exe", "C:/drive.exe",
                "x.dll:stream", "trailing./x.exe", "trailing /x.exe", "CON.txt", "x//y.exe", "IRSpeedyHotspotHelper.EXE" })
                Rejected(() => Archive(Payload("bad", unsafePath)), "accepted unsafe/duplicate path: " + unsafePath);
            Rejected(() => Archive(Payload("bad", complete: false)), "accepted incomplete self-contained payload");
            Check(!Directory.EnumerateDirectories(root).Any(p => Path.GetFileName(p).Contains(".staging-")), "no staging directories after success");
            Console.WriteLine($"{checks} hotspot payload checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }
}
