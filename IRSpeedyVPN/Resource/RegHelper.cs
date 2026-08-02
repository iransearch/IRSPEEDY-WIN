using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;

namespace IRSpeedyVPN.Resource
{
    internal class RegHelper
    {
        private static String RegAddress = "IRSpeedy\\IRSpeedyVpn";

        internal static string GetSettingValue(String SettingName, string RelativePath)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(Path.Combine(RegAddress, RelativePath));
            String retVal = "";
            if (key != null)
            {
                try
                {
                    retVal = (String)key.GetValue(SettingName).ToString();
                }
                catch
                {
                }
                key.Close();
                return retVal;
            }

            return retVal;
        }
        public static String GetSettingValue(String SettingName)
        {
            return GetSettingValue(SettingName, "");

        }
        public static void SetSettingValue(String SettingName, String Value)
        {
            SetSettingValue(SettingName, Value, "");
        }
        internal static void SetSettingValue(String SettingName, String Value, string RelativePath)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(Path.Combine(RegAddress, RelativePath), true);

            if (key == null)
            {

                key = Registry.CurrentUser.CreateSubKey(Path.Combine(RegAddress, RelativePath));
            }
            key.SetValue(SettingName, Value, RegistryValueKind.String);
            key.Close();

        }
   
        internal static void DeleteKey(string RelativePath, string SubKey)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(Path.Combine(RegAddress, RelativePath), true);

            if (key != null)
            {

                key.DeleteSubKeyTree(SubKey);
            }

            key.Close();
        }


    }
}
