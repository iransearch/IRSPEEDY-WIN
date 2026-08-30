using Ionic.Crc;
using IRSpeedyVPN.Common.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.Common
{
    internal static class Tools
    {
        internal static byte[] ToAsciiBytes(this string data)
        {
            return Encoding.UTF8.GetBytes(data);
        }
        internal static byte[] ToUTF8Bytes(this string data)
        {
            return Encoding.UTF8.GetBytes(data);
        }
        internal static string ToUTF8String(this byte[] data)
        {
            return Encoding.UTF8.GetString(data);
        }
        internal static string ToUTF8String(this byte[] data, int index, int count)
        {
            return Encoding.UTF8.GetString(data, index, count);
        }
        internal static String ToBase64String(this byte[] data)
        {
            return Convert.ToBase64String(data);
        }
        internal static byte[] FromBase64String(this String data)
        {
            return Convert.FromBase64String(data);
        }
        public static byte[] ReadAllBytes(this BinaryReader reader)
        {
            const int bufferSize = 4096;
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }

        }
        public static string GetCountryName(this string countryCode)
        {
            try
            {
                return AppServices.PersianIsoNames.GetName(countryCode);
            }
            catch
            {
                return countryCode;
            }
        }

        public static T JsonDeserilize<T>(this string data)
        {
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[] { AppServices.JsonConverter });
            return (T)(serializer.Deserialize<T>(data));
        }
        public static string JsonSerilize(this object data)
        {
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[] { AppServices.JsonConverter });
            return serializer.Serialize(data);
        }
        public static string ToPresianDate(this DateTime d)
        {
            PersianCalendar pc = new PersianCalendar();
            return string.Format("{0}/{1:00}/{2:00}", pc.GetYear(d), pc.GetMonth(d), pc.GetDayOfMonth(d));
        }
        public static string ToPresianDateTime(this DateTime d)
        {
            return String.Format("{0} {1}", d.ToPresianDate(), d.ToString("T", CultureInfo.CreateSpecificCulture("de-DE")));
        }
        public static string TotalDays(this DateTime d)
        {
            StringBuilder sb = new StringBuilder();
            double days = (d - DateTime.Now).TotalDays;
            if (days / 365.0 > 1)
                sb.Append(string.Format("{0}سال", (int)(days / 365)));
            if (days % 365 > 0)
            {
                if (sb.Length > 0)
                    sb.Append(" و ");
                sb.Append(string.Format("{0} روز", (int)(days % 365)));
            }
            return sb.ToString();


        }
        public static bool IsWinXpOrLower()
        {
            return Environment.OSVersion.Version.Major <= 5;
        }
        public static bool IsWin7OrLower()
        {
            var v = Environment.OSVersion.Version;
            // Win7 = 6.1, Vista = 6.0, XP/2003 = 5.x, etc.
            return v.Major < 6 || (v.Major == 6 && v.Minor <= 1);
        }
        public static int ToInt32(this string str)
        {
            int ret = 0;
            int.TryParse(str, out ret);
            return ret;

        }
        public static string GetDescription(this Enum value)
        {
            FieldInfo fi = value.GetType().GetField(value.ToString());

            DescriptionAttribute[] attributes = fi.GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[];

            if (attributes != null && attributes.Any())
            {
                return attributes.First().Description;
            }

            return value.ToString();
        }
        public static string GetCRC32(Stream data)
        {
            CRC32 crc32 = new CRC32();
            int crc = crc32.GetCrc32(data);
            return crc.ToString("X");

        }
        public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                try
                {
                    string targetFilePath = Path.Combine(destinationDir, file.Name);
                    file.CopyTo(targetFilePath, true);
                }
                catch { }
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> source)
        {
            Random rnd = new Random();
            return source.OrderBy<T, int>((item) => rnd.Next());
        }

        /// <summary>
        /// Puts hysteria2 servers at the front of a smart pool. The balancer sends
        /// everything through its fallback - the first outbound in the pool - until its own
        /// probes produce data, and hy2 endpoints come up fastest, so leading with them
        /// makes the opening seconds of a connection usable. Ordering only: nothing is
        /// dropped, and the sort is stable so every other server keeps the order it
        /// arrived in.
        /// </summary>
        public static IEnumerable<Models.NewService.Url> OrderByHysteriaFirst(
            this IEnumerable<Models.NewService.Url> source)
        {
            if (source == null)
                return Enumerable.Empty<Models.NewService.Url>();

            return source.OrderBy(u => IsHysteria2Link(u?.url) ? 0 : 1);
        }

        private static bool IsHysteria2Link(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            return url.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase);
        }
        public static string GetJsonString(this string jsonString, string path)
        {
            try
            {
                JToken token = JToken.Parse(jsonString);
                JToken valueToken = token.SelectToken(path);
                return valueToken?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        public static bool IsValidTimeFormat(this string timeString)
        {
            if(string.IsNullOrEmpty( timeString))
                return false; ;
            Regex TimeRegex = new Regex(@"^\d{4}[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01]) (0[0-9]|1\d|2[0-3]):[0-5]\d:[0-5]\d$");
            return TimeRegex.IsMatch(timeString);
        }

    }
}
