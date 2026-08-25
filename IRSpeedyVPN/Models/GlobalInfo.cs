using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Models
{
    internal class GlobalInfo : UserAccount
    {
        public string TempPath { get; set; }
        public string Password { get; set; }
        public int ManagementPort { get => 7075; }
        public SettingInfo settings { get; set; }
        public DateTime ConnectionTime { get; set; }
        public IVPNService CurrentService;
        public string ServerResponse { get; set; }
        public List<Url> Vods { get; set; }
        public List<Url> Ais { get; set; }

        public void Import(AccountInfoEx acc, string password,String tempPath)
        {
            ServerTime = acc.UserAccount.ServerTime;
            UserID = acc.UserAccount.UserID;
            GroupName = acc.UserAccount.GroupName;
            Status = acc.UserAccount.Status;
            ExpiryDate = acc.UserAccount.ExpiryDate;
            FirstLogin = acc.UserAccount.FirstLogin;
            Username = acc.UserAccount.Username;
            Password = password;
            TempPath = tempPath;
            settings = acc.Settings;
            Vods = acc.vods;
            Ais = acc.ais;
        }
        /*
        public String GetSetting(string Name)
        {
            if(settings!=null)
            {
                 var setting = settings.Where(x => x.name == Name).SingleOrDefault();
                if (setting.name==Name&&!string.IsNullOrEmpty(setting.content))
                {
                    return setting.content;
                }
            }            
            return null;
        }
        public Version GetUpdateVerion()
        {
            string resselerStr = "";
#if _RESELLER
            resselerStr= "Reseller";
#endif
            string sversion = GetSetting(resselerStr+"UpdateVersion");
            string url= GetSetting(resselerStr + "UpdateUrl");
            Version version;
            if (!string.IsNullOrEmpty(sversion) && !string.IsNullOrEmpty(url) && Version.TryParse(sversion, out version))
                return version;
            return null;

        }
        public String GetUpdateUrl()
        {
            string resselerStr = "";
#if _RESELLER
            resselerStr = "Reseller";
#endif
            return GetSetting(resselerStr + "UpdateUrl");
        }
        public String GetUpdateChangeLog()
        {
            string resselerStr = "";
#if _RESELLER
            resselerStr = "Reseller";
#endif
            return GetSetting(resselerStr + "UpdateChangelog");
        }*/
    }
}
