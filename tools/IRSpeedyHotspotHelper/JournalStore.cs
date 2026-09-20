using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace IRSpeedy.Hotspot;

internal sealed class JournalStore : IJournal
{
    private readonly string path;

    public JournalStore()
    {
        var parent = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = new DirectoryInfo(Path.Combine(parent, "IRSpeedyHotspotPoC"));
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
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
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new HotspotException("unsafe-journal-directory");
        var security = directory.GetAccessControl();
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!admins.Equals(owner) && !system.Equals(owner)) throw new HotspotException("unsafe-journal-owner");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !admins.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference))
                throw new HotspotException("unsafe-journal-acl");
        }
        path = Path.Combine(directory.FullName, "session.json");
        CheckFile();
    }

    private void CheckFile()
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new HotspotException("unsafe-journal-file");
    }

    public Journal? Read()
    {
        CheckFile();
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4096) throw new HotspotException("invalid-recovery-journal");
        return JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ??
            throw new HotspotException("invalid-recovery-journal");
    }

    public void Write(Journal state)
    {
        CheckFile();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Clear() { CheckFile(); File.Delete(path); }
}
