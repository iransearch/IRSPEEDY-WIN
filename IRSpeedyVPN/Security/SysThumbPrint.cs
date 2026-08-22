using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace IRSpeedyVPN.Security
{
    /// <summary>
    /// Generates the legacy device fingerprint used by existing accounts/settings.
    /// The fingerprint algorithm is intentionally unchanged; initialization is merely
    /// serialized so deferred startup and a very fast manual login cannot run the same
    /// expensive WMI scan twice in parallel.
    /// </summary>
    public class SysThumbPrint
    {
        private static byte[] fingerPrint = null;
        private static readonly object fingerPrintLock = new object();

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

        private static string identifier(string wmiClass, string wmiProperty, string wmiMustBeTrue)
        {
            string result = "";
            using (System.Management.ManagementClass mc = new System.Management.ManagementClass(wmiClass))
            using (System.Management.ManagementObjectCollection moc = mc.GetInstances())
            {
                foreach (System.Management.ManagementObject mo in moc)
                {
                    if (mo[wmiMustBeTrue].ToString() == "True")
                    {
                        if (result == "")
                        {
                            try
                            {
                                result = mo[wmiProperty].ToString();
                                break;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            return result;
        }

        private static string identifier(string wmiClass, string wmiProperty)
        {
            string result = "";
            using (System.Management.ManagementClass mc = new System.Management.ManagementClass(wmiClass))
            using (System.Management.ManagementObjectCollection moc = mc.GetInstances())
            {
                foreach (System.Management.ManagementObject mo in moc)
                {
                    if (result == "")
                    {
                        try
                        {
                            result = mo[wmiProperty].ToString();
                            break;
                        }
                        catch
                        {
                        }
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
