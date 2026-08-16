using Ionic.Zip;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows;

namespace IRSpeedyVPN.Resource
{
    public class ResourceManager
    {
        private readonly TripleDesHelper tdes;
        private readonly object extractionLock = new object();
        private readonly string runtimeRoot;
        private readonly string versionsPath;
        private readonly string statePath;
        private readonly string activeVersionPath;
        private const string SettingFile = "seed.set";

        public string TempPath { get; set; }
        public string Password { get; set; }
        public event EventHandler onResourceExtracted;

        public ResourceManager()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "IRSpeedy");
            versionsPath = Path.Combine(runtimeRoot, "versions");
            statePath = Path.Combine(runtimeRoot, "state");
            activeVersionPath = Path.Combine(runtimeRoot, "active.version");

            Directory.CreateDirectory(runtimeRoot);
            Directory.CreateDirectory(versionsPath);
            Directory.CreateDirectory(statePath);

            TempPath = ResolveActiveVersionPath() ?? runtimeRoot;
            tdes = new TripleDesHelper(
                0x1F,
                0xaf,
                "b1",
                "8a",
                "aa",
                "d6",
                "ef",
                0xea,
                SysThumbPrint.Value());
            MigrateSettingsFile();
        }

        public void ExtractResource(bool W84Exit = false)
        {
            var ready = false;
            lock (extractionLock)
            {
                var previousVersion = ReadActiveVersion();
                try
                {
                    using (var source = Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/Files.zip"))?.Stream)
                    {
                        if (source == null)
                            throw new FileNotFoundException("Embedded Resources/Files.zip was not found.");

                        var archiveBytes = ReadAllBytes(source);
                        var versionId = ComputeVersionId(archiveBytes);
                        var versionDirectory = Path.Combine(versionsPath, versionId);

                        if (!IsValidRuntime(versionDirectory))
                            ExtractVersion(archiveBytes, versionDirectory);

                        if (!IsValidRuntime(versionDirectory))
                            throw new InvalidDataException("Extracted runtime is incomplete.");

                        WriteActiveVersion(versionId);
                        TempPath = versionDirectory;
                        MigrateSettingsFile();
                        CleanupOldVersions(versionId, previousVersion);
                        ready = true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(ex);
                    var activePath = ResolveActiveVersionPath();
                    if (IsValidRuntime(activePath))
                    {
                        TempPath = activePath;
                        ready = true;
                    }
                }
            }

            if (!W84Exit && ready)
                onResourceExtracted?.Invoke(this, EventArgs.Empty);
        }

        public void Extract(string toExtractPath, string path = "")
        {
            using (var stream = File.OpenRead(toExtractPath))
                Extract(stream, path);
        }

        public void Extract(Stream toExtractStream, string path = "")
        {
            if (toExtractStream == null)
                throw new ArgumentNullException(nameof(toExtractStream));

            var outputPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(runtimeRoot, path ?? string.Empty);
            Directory.CreateDirectory(outputPath);

            using (var zip = ZipFile.Read(toExtractStream))
            {
                ValidateEntries(zip);
                zip.ExtractAll(outputPath, ExtractExistingFileAction.OverwriteSilently);
            }
        }

        internal AccountInfoEx GetConfig()
        {
            try
            {
                MigrateSettingsFile();
                var settingsFile = Path.Combine(statePath, SettingFile);
                var acc = tdes.Decrypt(File.ReadAllBytes(settingsFile))
                    .ToUTF8String()
                    .JsonDeserilize<AccountInfoEx>();
                var userInfo = tdes.Decrypt(RegHelper.GetSettingValue("UserInfo").FromBase64String())
                    .ToUTF8String()
                    .Split('\n');
                if (userInfo.Length < 2
                    || userInfo[0] != acc.UserAccount.Username
                    || (acc.UserAccount.ExpiryDate != null && acc.UserAccount.ExpiryDate.Value < DateTime.Now))
                    return null;

                Password = userInfo[1];
                return acc;
            }
            catch
            {
                return null;
            }
        }

        internal AccountInfoEx GetLocalConfig()
        {
            try
            {
                var acc = File.ReadAllText("./account.txt").JsonDeserilize<AccountInfoEx>();
                if (File.Exists("./oneclick.txt"))
                {
                    var cfgserver = Encoding.UTF8
                        .GetString(Convert.FromBase64String(
                            new System.Net.WebClient().DownloadString(File.ReadAllText("./oneclick.txt"))))
                        .Split('\n');
                    acc.groups.Clear();
                    acc.groups.Add(new Group { title = "OneClick" });
                    var id = 1;
                    foreach (var serverLink in cfgserver)
                    {
                        if (serverLink.StartsWith("vmess:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx
                            {
                                urls = new[] { new Url { url = serverLink } }.ToList(),
                                ID = id++,
                                Country = ((Dictionary<string, object>)(
                                    new JavaScriptSerializer().DeserializeObject(
                                        Encoding.UTF8.GetString(
                                            Convert.FromBase64String(serverLink.Substring(8))))))["ps"].ToString()
                            });
                        }
                        else if (serverLink.StartsWith("trojan:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx
                            {
                                urls = new[] { new Url { url = serverLink } }.ToList(),
                                ID = id++,
                                Country = HttpUtility.UrlDecode(serverLink)
                                    .Substring(serverLink.IndexOf('#'))
                            });
                        }
                    }
                }
                return acc;
            }
            catch
            {
                return null;
            }
        }

        internal void SaveConfig(AccountInfoEx info, string password)
        {
            Directory.CreateDirectory(statePath);
            RegHelper.SetSettingValue(
                "UserInfo",
                tdes.Encrypt(string.Format("{0}\n{1}", info.UserAccount.Username, password).ToUTF8Bytes())
                    .ToBase64String());
            File.WriteAllBytes(
                Path.Combine(statePath, SettingFile),
                tdes.Encrypt(info.JsonSerilize().ToUTF8Bytes()));
        }

        internal void RemoveConfig()
        {
            RegHelper.SetSettingValue("UserInfo", "");
        }

        private void ExtractVersion(byte[] archiveBytes, string versionDirectory)
        {
            var stagingDirectory = Path.Combine(
                versionsPath,
                ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                using (var memory = new MemoryStream(archiveBytes, false))
                using (var zip = ZipFile.Read(memory))
                {
                    ValidateEntries(zip);
                    // ExtractAll with OverwriteSilently still throws "Cannot create a
                    // file when that file already exists" from MoveFileInPlace when the
                    // archive contains duplicate entries; extract one entry at a time
                    // and clear the target (attributes included) before each one.
                    foreach (var entry in zip.Entries)
                    {
                        var targetPath = Path.Combine(stagingDirectory, entry.FileName ?? string.Empty);
                        if (File.Exists(targetPath))
                        {
                            File.SetAttributes(targetPath, FileAttributes.Normal);
                            File.Delete(targetPath);
                        }
                        entry.Extract(stagingDirectory, ExtractExistingFileAction.OverwriteSilently);
                    }
                }

                if (!IsValidRuntime(stagingDirectory))
                    throw new InvalidDataException("The embedded runtime archive is missing required core files.");

                if (Directory.Exists(versionDirectory))
                    SafeDeleteDirectory(versionDirectory);
                if (Directory.Exists(versionDirectory))
                    throw new IOException(
                        "Unable to replace the runtime directory (files may be in use): " + versionDirectory);
                Directory.Move(stagingDirectory, versionDirectory);
            }
            finally
            {
                SafeDeleteDirectory(stagingDirectory);
            }
        }

        private static void ValidateEntries(ZipFile zip)
        {
            foreach (var entry in zip.Entries)
            {
                var name = (entry.FileName ?? string.Empty).Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (name.StartsWith("/", StringComparison.Ordinal)
                    || name.Contains(":")
                    || name.Split('/').Any(x => x == ".."))
                {
                    throw new InvalidDataException("Unsafe path in ZIP archive: " + name);
                }
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static string ComputeVersionId(byte[] archiveBytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(archiveBytes ?? new byte[0]);
                return BitConverter.ToString(hash, 0, 16).Replace("-", string.Empty);
            }
        }

        private bool IsValidRuntime(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            var throne = Path.Combine(path, "V-Guard", "Throne.exe");
            var coreName = (Tools.IsWin7OrLower() ? "SGuard7" : "SGuard")
                + (Environment.Is64BitOperatingSystem ? "64.exe" : "32.exe");
            var core = Path.Combine(path, "V-Guard", coreName);
            return File.Exists(throne) && File.Exists(core);
        }

        private string ResolveActiveVersionPath()
        {
            var version = ReadActiveVersion();
            if (string.IsNullOrWhiteSpace(version))
                return null;
            var path = Path.Combine(versionsPath, version);
            return IsValidRuntime(path) ? path : null;
        }

        private string ReadActiveVersion()
        {
            try
            {
                return File.Exists(activeVersionPath)
                    ? File.ReadAllText(activeVersionPath).Trim()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void WriteActiveVersion(string versionId)
        {
            Directory.CreateDirectory(runtimeRoot);
            var temporary = activeVersionPath + ".new";
            File.WriteAllText(temporary, versionId ?? string.Empty, Encoding.ASCII);
            if (File.Exists(activeVersionPath))
                File.Replace(temporary, activeVersionPath, null);
            else
                File.Move(temporary, activeVersionPath);
        }

        private void CleanupOldVersions(string activeVersion, string previousVersion)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                activeVersion
            };
            if (!string.IsNullOrWhiteSpace(previousVersion))
                keep.Add(previousVersion);

            try
            {
                foreach (var directory in Directory.GetDirectories(versionsPath))
                {
                    var name = Path.GetFileName(directory);
                    if (name.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase)
                        || !keep.Contains(name))
                    {
                        SafeDeleteDirectory(directory);
                    }
                }
            }
            catch
            {
            }
        }

        private void MigrateSettingsFile()
        {
            try
            {
                Directory.CreateDirectory(statePath);
                var destination = Path.Combine(statePath, SettingFile);
                if (File.Exists(destination))
                    return;

                var candidates = new[]
                {
                    Path.Combine(runtimeRoot, SettingFile),
                    string.IsNullOrWhiteSpace(TempPath) ? null : Path.Combine(TempPath, SettingFile)
                };
                var source = candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x));
                if (source != null)
                    File.Copy(source, destination, false);
            }
            catch
            {
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                // Hidden/read-only files make the recursive delete throw, which
                // would later turn Directory.Move into "file already exists".
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch { }
                }
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
