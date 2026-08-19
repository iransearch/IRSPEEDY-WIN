using Ionic.Zip;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows;

namespace IRSpeedyVPN.Resource
{
    public class ResourceManager
    {
        TripleDesHelper tdes;
        public string TempPath { get; set; }
        String SettingFile = "seed.set";
        public string Password { get; set; }
        public event EventHandler onResourceExtracted;
        public ResourceManager()
        {
            TempPath =  Path.Combine(Path.GetTempPath(), "IRSpeedy");
            Directory.CreateDirectory(TempPath);            
            tdes = new TripleDesHelper(0x1F, 0xaf, "b1", "8a", "aa", "d6", "ef", 0xea, SysThumbPrint.Value());
            //new Action(ExtractResource).BeginInvoke(null,null);
        }
        public void ExtractResource(bool W84Exit = false)
        {
            void extraction()
            {
                byte[] settingsBackup = null;
                string settingsPath = Path.Combine(TempPath, SettingFile);

                try
                {
                    // 1) Backup setting file (if exists)
                    if (File.Exists(settingsPath))
                    {
                        settingsBackup = File.ReadAllBytes(settingsPath);
                    }

                    // 2) Extract zip to a temp folder
                    using (var stream = Application.GetResourceStream(
                               new Uri("pack://application:,,,/Resources/Files.zip"))?.Stream)
                    {
                        if (stream == null) return;

                        using (var z = ZipFile.Read(stream))
                        {
                            string exPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(exPath);

                            z.ExtractAll(exPath, ExtractExistingFileAction.OverwriteSilently);

                            // 3) Replace TempPath content
                            SafeDeleteDirectory(TempPath);
                            Directory.CreateDirectory(TempPath);
                            Tools.CopyDirectory(exPath, TempPath, true);

                            SafeDeleteDirectory(exPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(ex);
                }
                finally
                {
                    // 4) Restore setting file (best effort)
                    try
                    {
                        if (settingsBackup != null)
                        {
                            Directory.CreateDirectory(TempPath);
                            File.WriteAllBytes(Path.Combine(TempPath, SettingFile), settingsBackup);
                        }
                    }
                    catch { }

                    if (!W84Exit)
                        onResourceExtracted?.Invoke(this, null);
                }
            }

            if (W84Exit)
                extraction();
            else
                System.Threading.Tasks.Task.Run(extraction);
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }

        public void Extract(string toExtractPath, string path = "")
        {
            using (Stream f = File.OpenRead(toExtractPath))
                Extract(f, path);
        }
        public void Extract(Stream toExtractStream,string path="")
        {
            Action extraction = new Action(() =>
            {
                try
                {
                    
                    
                    ZipFile z = ZipFile.Read(toExtractStream);
                    string pOut;
                    if (path.Contains(":"))
                        pOut = path;
                    else
                        pOut = Path.Combine(TempPath, path);
                    z.ExtractAll(pOut, ExtractExistingFileAction.OverwriteSilently);
                }
                catch {
                    throw;
                }
               
            });
            extraction.Invoke();

        }
        internal AccountInfoEx GetConfig()
        {
            try
            {
                AccountInfoEx acc = tdes.Decrypt(File.ReadAllBytes(Path.Combine(TempPath, SettingFile))).ToUTF8String().JsonDeserilize<AccountInfoEx>();
                string[] userInfo = tdes.Decrypt(RegHelper.GetSettingValue("UserInfo").FromBase64String()).ToUTF8String().Split('\n');
                if (userInfo[0] != acc.UserAccount.Username || (acc.UserAccount.ExpiryDate != null && acc.UserAccount.ExpiryDate.Value < DateTime.Now))
                    return null;
                Password = userInfo[1];
                return acc;
            }catch
            {

            }
            return null;
        }
        internal AccountInfoEx GetLocalConfig()
        {
            try
            {
                AccountInfoEx acc = File.ReadAllText("./account.txt").JsonDeserilize<AccountInfoEx>();
                if (File.Exists("./oneclick.txt"))
                {
                    var cfgserver = Encoding.UTF8.GetString(Convert.FromBase64String(new System.Net.WebClient().DownloadString(File.ReadAllText("./oneclick.txt")))).Split('\n');
                    acc.groups.Clear();
                    acc.groups.Add(new Group { title = "OneClick" });
                    int i = 1;
                    foreach(string s in cfgserver)
                    {
                        if (s.StartsWith("vmess:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx()
                            {
                                urls = (new Url[] { new Url() { url = s } }).ToList(),
                                ID = i++,
                                Country = ((Dictionary<string, object>)(new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(8))))))["ps"].ToString(),

                            });
                        }
                        else if (s.StartsWith("trojan:"))
                        {
                            acc.groups[0].servers.Add(new ServerEx()
                            {
                                urls = (new Url[] { new Url() { url = s } }).ToList(),
                                ID = i++,                                
                                Country = HttpUtility.UrlDecode(s).Substring(s.IndexOf('#')),                                

                            });
                        }
                    }

                }
                
                return acc;
            }
            catch
            {

            }
            return null;
        }
        internal void SaveConfig(AccountInfoEx info,string password)
        {
            RegHelper.SetSettingValue("UserInfo", tdes.Encrypt(string.Format("{0}\n{1}", info.UserAccount.Username, password).ToUTF8Bytes()).ToBase64String());
            File.WriteAllBytes(Path.Combine(TempPath, SettingFile), tdes.Encrypt(info.JsonSerilize().ToUTF8Bytes()));
        }
        internal void RemoveConfig()
        {
            RegHelper.SetSettingValue("UserInfo", "");
        }
    }
}
