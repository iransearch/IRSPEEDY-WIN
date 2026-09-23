// Adapted from Moonlight Tunneling 5268937 (MIT); see docs/third-party.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace IRSpeedyVPN.Services.SplitTunneling
{
internal sealed class ShortcutInfo
{
    public string Name, LinkPath, TargetPath, Arguments;
    public ShortcutInfo(string name, string link, string target, string arguments)
    { Name = name; LinkPath = link; TargetPath = target; Arguments = arguments; }
}

internal static class ShellLinks
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    public static ShortcutInfo Read(string lnkPath)
    {
        try
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 0);
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var args = new StringBuilder(2048);
                link.GetArguments(args, args.Capacity);
                var t = Environment.ExpandEnvironmentVariables(target.ToString());
                if (string.IsNullOrWhiteSpace(t)) return null;
                return new ShortcutInfo(Path.GetFileNameWithoutExtension(lnkPath), lnkPath, t, args.ToString());
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        catch
        {
            return null;
        }
    }

}
}
