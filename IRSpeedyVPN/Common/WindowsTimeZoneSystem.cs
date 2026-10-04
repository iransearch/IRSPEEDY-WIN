using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IRSpeedyVPN.Common
{
    internal sealed class WindowsTimeZoneSystem : ITimeZoneSystem
    {
        public TimeZoneSnapshot Capture()
        {
            DynamicZone current;
            if (GetDynamicTimeZoneInformation(out current) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            TimeZoneInfo.ClearCachedData();
            var local = TimeZoneInfo.Local;
            var key = string.IsNullOrEmpty(current.KeyName) ? local.Id : current.KeyName;
            TimeZoneInfo.FindSystemTimeZoneById(key); // A custom/unrestorable zone cannot be overridden.
            return new TimeZoneSnapshot
            {
                WindowsId = key, DaylightSavingDisabled = current.DaylightDisabled,
                SerializedLocalZone = local.ToSerializedString()
            };
        }

        public void Apply(string windowsId) { Set(windowsId, false); }
        public void Restore(TimeZoneSnapshot snapshot) { Set(snapshot.WindowsId, snapshot.DaylightSavingDisabled); }

        private static void Set(string id, bool disableDaylight)
        {
            var installed = TimeZoneInfo.FindSystemTimeZoneById(id);
            DynamicZone zone;
            // Read Windows' installed data; no guessed UTC offsets or shell commands.
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones\" + installed.Id))
            {
                var tzi = key?.GetValue("TZI") as byte[];
                if (tzi == null || tzi.Length != 44) throw new TimeZoneNotFoundException("Windows time-zone data is unavailable.");
                zone = new DynamicZone
                {
                    Bias = BitConverter.ToInt32(tzi, 0), StandardBias = BitConverter.ToInt32(tzi, 4),
                    DaylightBias = BitConverter.ToInt32(tzi, 8), StandardDate = ReadDate(tzi, 12),
                    DaylightDate = ReadDate(tzi, 28), StandardName = TrimName(installed.StandardName),
                    DaylightName = TrimName(installed.DaylightName), KeyName = installed.Id,
                    DaylightDisabled = disableDaylight
                };
            }
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x28, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            bool enabled = false;
            TokenPrivileges previous = default(TokenPrivileges);
            try
            {
                Luid luid;
                if (!LookupPrivilegeValue(null, "SeTimeZonePrivilege", out luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var privilege = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                uint length;
                if (!AdjustTokenPrivileges(token, false, ref privilege, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), out previous, out length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);
                enabled = true;
                if (!SetDynamicTimeZoneInformation(ref zone)) throw new Win32Exception(Marshal.GetLastWin32Error());
                TimeZoneInfo.ClearCachedData();
            }
            finally
            {
                if (enabled) RestoreTokenPrivileges(token, false, ref previous, 0, IntPtr.Zero, IntPtr.Zero);
                CloseHandle(token);
            }
        }

        private static string TrimName(string name) => name.Length > 31 ? name.Substring(0, 31) : name;
        private static SystemDate ReadDate(byte[] bytes, int offset)
        {
            return new SystemDate
            {
                Year = BitConverter.ToUInt16(bytes, offset), Month = BitConverter.ToUInt16(bytes, offset + 2),
                DayOfWeek = BitConverter.ToUInt16(bytes, offset + 4), Day = BitConverter.ToUInt16(bytes, offset + 6),
                Hour = BitConverter.ToUInt16(bytes, offset + 8), Minute = BitConverter.ToUInt16(bytes, offset + 10),
                Second = BitConverter.ToUInt16(bytes, offset + 12), Milliseconds = BitConverter.ToUInt16(bytes, offset + 14)
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemDate { internal ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DynamicZone
        {
            internal int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string StandardName;
            internal SystemDate StandardDate;
            internal int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DaylightName;
            internal SystemDate DaylightDate;
            internal int DaylightBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string KeyName;
            [MarshalAs(UnmanagedType.U1)] internal bool DaylightDisabled;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Luid { internal uint Low; internal int High; }
        [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { internal uint Count; internal Luid Luid; internal uint Attributes; }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetDynamicTimeZoneInformation(out DynamicZone zone);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetDynamicTimeZoneInformation(ref DynamicZone zone);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, out TokenPrivileges previous, out uint resultLength);
        [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr resultLength);
    }
}
