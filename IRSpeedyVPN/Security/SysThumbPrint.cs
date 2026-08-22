using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace IRSpeedyVPN.Security
{
    /// <summary>
    /// Generates the legacy device fingerprint used by existing accounts/settings.
    /// The fingerprint byte-for-byte input format is intentionally unchanged. WMI class
    /// results are cached so repeated property reads do not enumerate the same class over
    /// and over during startup.
    /// </summary>
    public class SysThumbPrint
    {
        private static byte[] fingerPrint = null;
        private static readonly object fingerPrintLock = new object();
        private static readonly object wmiCacheLock = new object();
        private static readonly Dictionary<string, List<Dictionary<string, object>>> wmiCache
            = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        public static string GetComputerName()
        {
            return GetSystemModel();
        }

        public static string GetSystemModel()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Model FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return mo["Model"]?.ToString();
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        public static string ValueString()
        {
            byte[] value = Value();
            return BitConverter.ToString(value).Replace("-", "");
        }

        public static byte[] Value()
        {
            if (fingerPrint != null)
                return fingerPrint;

            lock (fingerPrintLock)
            {
                if (fingerPrint == null)
                {
                    // Keep this exact field order/content for backward compatibility
                    // with encrypted seed.set/UserInfo and server-side device tokens.
                    string data = "CPU >> " + cpuId() + "\nBIOS >> " +
                        biosId() + "\nBASE >> " + baseId() +
                        videoId();
                    fingerPrint = GetHash(data);
                }
                return fingerPrint;
            }
        }

        private static byte[] GetHash(string s)
        {
            using (MD5 sec = new MD5CryptoServiceProvider())
            {
                ASCIIEncoding enc = new ASCIIEncoding();
                byte[] bt = enc.GetBytes(s);
                return sec.ComputeHash(bt);
            }
        }

        #region Original Device ID Getting Code

        /// <summary>
        /// Snapshot one WMI class once. The old code opened the same class separately for
        /// every requested property (BIOS alone did six full enumerations). We still use
        /// each property's original ToString() value and first-instance ordering, so the
        /// fingerprint input remains unchanged while startup performs far fewer WMI calls.
        /// </summary>
        private static List<Dictionary<string, object>> GetWmiSnapshot(string wmiClass)
        {
            lock (wmiCacheLock)
            {
                if (wmiCache.TryGetValue(wmiClass, out var cached))
                    return cached;

                var rows = new List<Dictionary<string, object>>();
                using (var mc = new ManagementClass(wmiClass))
                using (var moc = mc.GetInstances())
                {
                    foreach (ManagementObject mo in moc)
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (PropertyData property in mo.Properties)
                        {
                            try
                            {
                                row[property.Name] = property.Value;
                            }
                            catch
                            {
                                // The legacy identifier ignored failures while reading the
                                // requested property and continued to the next instance.
                            }
                        }
                        rows.Add(row);
                    }
                }

                wmiCache[wmiClass] = rows;
                return rows;
            }
        }

        private static string identifier(string wmiClass, string wmiProperty, string wmiMustBeTrue)
        {
            string result = "";
            foreach (var row in GetWmiSnapshot(wmiClass))
            {
                if (!row.TryGetValue(wmiMustBeTrue, out var required)
                    || required == null
                    || required.ToString() != "True")
                {
                    continue;
                }

                if (result == "")
                {
                    try
                    {
                        if (!row.TryGetValue(wmiProperty, out var value) || value == null)
                            continue;
                        result = value.ToString();
                        break;
                    }
                    catch
                    {
                    }
                }
            }
            return result;
        }

        private static string identifier(string wmiClass, string wmiProperty)
        {
            string result = "";
            foreach (var row in GetWmiSnapshot(wmiClass))
            {
                if (result == "")
                {
                    try
                    {
                        if (!row.TryGetValue(wmiProperty, out var value) || value == null)
                            continue;
                        result = value.ToString();
                        break;
                    }
                    catch
                    {
                    }
                }
            }
            return result;
        }

        private static string cpuId()
        {
            string retVal = identifier("Win32_Processor", "UniqueId");
            if (retVal == "")
            {
                retVal = identifier("Win32_Processor", "ProcessorId");
                if (retVal == "")
                {
                    retVal = identifier("Win32_Processor", "Name");
                    if (retVal == "")
                    {
                        retVal = identifier("Win32_Processor", "Manufacturer");
                    }
                    retVal += identifier("Win32_Processor", "MaxClockSpeed");
                }
            }
            return retVal;
        }

        private static string biosId()
        {
            return identifier("Win32_BIOS", "Manufacturer")
                + identifier("Win32_BIOS", "SMBIOSBIOSVersion")
                + identifier("Win32_BIOS", "IdentificationCode")
                + identifier("Win32_BIOS", "SerialNumber")
                + identifier("Win32_BIOS", "ReleaseDate")
                + identifier("Win32_BIOS", "Version");
        }

        private static string diskId()
        {
            return identifier("Win32_DiskDrive", "Model")
                + identifier("Win32_DiskDrive", "Manufacturer")
                + identifier("Win32_DiskDrive", "Signature")
                + identifier("Win32_DiskDrive", "TotalHeads");
        }

        private static string baseId()
        {
            return identifier("Win32_BaseBoard", "Model")
                + identifier("Win32_BaseBoard", "Manufacturer")
                + identifier("Win32_BaseBoard", "Name")
                + identifier("Win32_BaseBoard", "SerialNumber");
        }

        private static string videoId()
        {
            return identifier("Win32_VideoController", "DriverVersion")
                + identifier("Win32_VideoController", "Name");
        }

        private static string macId()
        {
            return identifier("Win32_NetworkAdapterConfiguration", "MACAddress", "IPEnabled");
        }

        #endregion
    }
}
