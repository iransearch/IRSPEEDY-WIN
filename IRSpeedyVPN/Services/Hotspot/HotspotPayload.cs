using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace IRSpeedyVPN.Services.Hotspot
{
    internal static class HotspotPayload
    {
        private static readonly string ResourceName = "IRSpeedyVPN.Hotspot." +
            (Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86") + ".zip";
        private static readonly Lazy<HotspotPayloadArchive> Archive =
            new Lazy<HotspotPayloadArchive>(() => new HotspotPayloadArchive(OpenResource));

        // Called by the UI timer: no extraction, decompression or disk writes here.
        internal static bool Included
        {
            get
            {
                using (var stream = OpenResource()) return stream != null;
            }
        }

        private static Stream OpenResource()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        }

        // HotspotPayloadArchive.Prepare() re-hashes every extracted file (the whole
        // self-contained runtime: hostfxr/hostpolicy/coreclr/dlls) from disk on every
        // single call, even though nothing can legitimately change it between two
        // hotspot starts in the same running app instance (the directory is
        // admin/system-only per EnsureProtectedDirectory, enforced once below). Cache
        // a successful Prepare() result for the lifetime of this process so repeated
        // starts (toggle off/on, reconnects) skip both the lock-file wait and the
        // full re-verification; a deleted/moved output still falls back to the normal
        // verify-and-extract path.
        private static string _cachedHelperPath;
        private static readonly object CacheLock = new object();

        internal static string Prepare()
        {
            lock (CacheLock)
            {
                if (_cachedHelperPath != null && File.Exists(_cachedHelperPath))
                    return _cachedHelperPath;
            }

            // The helper runs elevated. Never execute it from a user-writable temp
            // directory or prefer an arbitrary Hotspot folder next to the application.
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "IRSpeedyHotspotRuntime");
            EnsureProtectedDirectory(root);
            string lockPath = Path.Combine(root, "payload.lock");
            var clock = Stopwatch.StartNew();
            FileStream lease = null;
            while (lease == null)
            {
                if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Unsafe hotspot payload lock.");
                try { lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException)
                {
                    if (clock.ElapsedMilliseconds >= 30000) throw;
                    Thread.Sleep(100);
                }
            }
            string helperPath;
            using (lease) helperPath = Archive.Value.Prepare(root);
            lock (CacheLock) _cachedHelperPath = helperPath;
            return helperPath;
        }

        private static void EnsureProtectedDirectory(string path)
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.SetOwner(admins);
                foreach (var sid in new[] { admins, system })
                    acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                directory.Create(acl);
            }
            directory.Refresh();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsafe hotspot runtime directory.");
            var security = directory.GetAccessControl();
            var owner = security.GetOwner(typeof(SecurityIdentifier));
            if (!admins.Equals(owner) && !system.Equals(owner))
                throw new IOException("Unsafe hotspot runtime owner.");
            if (!security.AreAccessRulesProtected)
                throw new IOException("Unsafe hotspot runtime inheritance.");
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow &&
                    !admins.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference))
                    throw new IOException("Unsafe hotspot runtime permissions.");
        }
    }
}
