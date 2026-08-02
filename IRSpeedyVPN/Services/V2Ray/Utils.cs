using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing;
using System.Security.Principal;
using v2rayN.Base;
using System.Web;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using IRSpeedyVPN.Common.Json;

namespace v2rayN
{
    class Utils
    {

        #region 资源Json操作

        /// <summary>
        /// 获取嵌入文本资源
        /// </summary>
        /// <param name="res"></param>
        /// <returns></returns>
        public static string GetEmbedText(string res)
        {
            return Samples.GetResource(res);           
        }

        /// <summary>
        /// 反序列化成对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="strJson"></param>
        /// <returns></returns>
        public static T FromJson<T>(string strJson)
        {
            try
            {
                T obj = new JavaScriptSerializer().Deserialize<T>(strJson);
                return obj;
            }
            catch
            {
                return new JavaScriptSerializer().Deserialize<T>("");
            }
        }
        internal static Dictionary<string,object> ParseJson(string str)
        {
            return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(str);
        }

        /// <summary>
        /// 序列化成Json
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string ToJson(Object obj)
        {
            string result = string.Empty;
            try
            {
                //result = JsonConvert.SerializeObject(obj,
                //                         Formatting.Indented,
                //                       new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var serializer = new JavaScriptSerializer();
                serializer.RegisterConverters(new JavaScriptConverter[] { new NullPropertiesConverter() });
                result = FormatJson(serializer.Serialize(obj));
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
            }
            return result;
        }
        static string FormatJson(string json)
        {
            string INDENT_STRING = "    ";
            int indentation = 0;
            int quoteCount = 0;
            var result =
                from ch in json
                let quotes = ch == '"' ? quoteCount++ : quoteCount
                let lineBreak = ch == ',' && quotes % 2 == 0 ? ch + Environment.NewLine + String.Concat(Enumerable.Repeat(INDENT_STRING, indentation)) : null
                let openChar = ch == '{' || ch == '[' ? ch + Environment.NewLine + String.Concat(Enumerable.Repeat(INDENT_STRING, ++indentation)) : ch.ToString()
                let closeChar = ch == '}' || ch == ']' ? Environment.NewLine + String.Concat(Enumerable.Repeat(INDENT_STRING, --indentation)) + ch : ch.ToString()
                select lineBreak == null
                            ? openChar.Length > 1
                                ? openChar
                                : closeChar
                            : lineBreak;

            return String.Concat(result);
        }
        /// <summary>
        /// 保存成json文件
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static int ToJsonFile(Object obj, string filePath, bool nullValue = true)
        {
            int result;
            try
            {
               
                    JavaScriptSerializer serializer=new JavaScriptSerializer();
                    

                    File.WriteAllText(filePath, serializer.Serialize(obj));
                
                result = 0;
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
                result = -1;
            }
            return result;
        }

        
        #endregion

        #region 转换函数

        /// <summary>
        /// List<string>转逗号分隔的字符串
        /// </summary>
        /// <param name="lst"></param>
        /// <returns></returns>       
        public static List<string> String2List(string str)
        {
            try
            {
                str = str.Replace(Environment.NewLine, "");
                return new List<string>(str.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries));
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
                return new List<string>();
            }
        }

        /// <summary>
        /// Base64编码
        /// </summary>
        /// <param name="plainText"></param>
        /// <returns></returns>
        public static string Base64Encode(string plainText)
        {
            try
            {
                byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                return Convert.ToBase64String(plainTextBytes);
            }
            catch (Exception ex)
            {
                SaveLog("Base64Encode", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Base64解码
        /// </summary>
        /// <param name="plainText"></param>
        /// <returns></returns>
        public static string Base64Decode(string plainText)
        {
            try
            {
                plainText = plainText.TrimEx()
                  .Replace(Environment.NewLine, "")
                  .Replace("\n", "")
                  .Replace("\r", "")
                  .Replace(" ", "");

                if (plainText.Length % 4 > 0)
                {
                    plainText = plainText.PadRight(plainText.Length + 4 - plainText.Length % 4, '=');
                }

                byte[] data = Convert.FromBase64String(plainText);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                SaveLog("Base64Decode", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// 转Int
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static int ToInt(object obj)
        {
            try
            {
                return Convert.ToInt32(obj);
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
                return 0;
            }
        }
        public static bool ToBool(string s)
        {
            switch (s)
            {
                case "1":
                case "t":
                case "T":
                case "true":
                case "TRUE":
                case "True":
                    return true;
                case "0":
                case "f":
                case "F":
                case "false":
                case "FALSE":
                case "False":
                    return false;
                default:
                    return false;
            }
        }

        public static string ToString(object obj)
        {
            try
            {
                return (obj == null ? string.Empty : obj.ToString());
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
                return string.Empty;
            }
        }

     



        public static string UrlEncode(string url)
        {
            return Uri.EscapeDataString(url);
            //return  HttpUtility.UrlEncode(url);
        }
        public static string UrlDecode(string url)
        {
            return HttpUtility.UrlDecode(url);
        }
        public static byte[] DecodeUrlSafeBase64ToBytes(string val)
        {
            var data = val.Replace('-', '+').Replace('_', '/').PadRight(val.Length + (4 - val.Length % 4) % 4, '=');
            return Convert.FromBase64String(data);
        }
      
        public static string DecodeUrlSafeBase64(string val)
        {
            return Encoding.UTF8.GetString(DecodeUrlSafeBase64ToBytes(val));
        }

        public static string DecodeStandardSSRUrlSafeBase64(string val)
        {
            //if (val.IndexOf('=') >= 0)
            //    throw new FormatException();
            return Encoding.UTF8.GetString(DecodeUrlSafeBase64ToBytes(val));
        }
        public static Dictionary<string, string> ParseParam(string param_str)
        {
            Dictionary<string, string> params_dict = new Dictionary<string, string>();
            string[] obfs_params = param_str.Split('&');
            foreach (string p in obfs_params)
            {
                if (p.IndexOf('=') > 0)
                {
                    int index = p.IndexOf('=');
                    string key, val;
                    key = p.Substring(0, index);
                    val = p.Substring(index + 1);
                    params_dict[key] = val;
                }
            }
            return params_dict;
        }
        #endregion


        #region 数据检查


        /// <summary>
        /// 文本
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool IsNullOrEmpty(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }
            if (text.Equals("null"))
            {
                return true;
            }
            return false;
        }

        public static bool IsIpv6(string ip)
        {
            IPAddress address;
            if (IPAddress.TryParse(ip, out address))
            {
                switch (address.AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        return false;
                    case AddressFamily.InterNetworkV6:
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }

        #endregion

        #region 开机自动启动

      
        /// <summary>
        /// 获取启动了应用程序的可执行文件的路径
        /// </summary>
        /// <returns></returns>
        public static string GetPath(string fileName)
        {
            string startupPath = StartupPath();
            if (IsNullOrEmpty(fileName))
            {
                return startupPath;
            }
            return Path.Combine(startupPath, fileName);
        }



        public static string StartupPath()
        {
            return Application.StartupPath;
        }

        #endregion

        #region 杂项


        /// <summary>
        /// 深度拷贝
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static T DeepCopy<T>(T obj)
        {
            object retval;
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                //序列化成流
                bf.Serialize(ms, obj);
                ms.Seek(0, SeekOrigin.Begin);
                //反序列化成对象
                retval = bf.Deserialize(ms);
                ms.Close();
            }
            return (T)retval;
        }
      

        /// <summary>
        /// 取得GUID
        /// </summary>
        /// <returns></returns>
        public static string GetGUID(bool full = true)
        {
            try
            {
                if (full)
                {
                    return Guid.NewGuid().ToString("D");
                }
                else
                {
                    return BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0).ToString();
                }
            }
            catch (Exception ex)
            {
                SaveLog(ex.Message, ex);
            }
            return string.Empty;
        }
       
        #endregion

        #region TempPath

        // return path to store temporary files
        public static string GetTempPath(string filename = "")
        {
            string _tempPath = Path.Combine(StartupPath(), "guiTemps");
            if (!Directory.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }
            if (string.IsNullOrEmpty(filename))
            {
                return _tempPath;
            }
            else
            {
                return Path.Combine(_tempPath, filename);
            }
        }
        public static string GetConfigPath(string filename = "")
        {
            string _tempPath = Path.Combine(StartupPath(), "guiConfigs");
            if (!Directory.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }
            if (string.IsNullOrEmpty(filename))
            {
                return _tempPath;
            }
            else
            {
                return Path.Combine(_tempPath, filename);
            }
        }

        #endregion

        #region Log

        public static void SaveLog(string strTitle, Exception ex)
        {
           
        }

        #endregion
     
    }
}
