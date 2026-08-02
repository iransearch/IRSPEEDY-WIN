using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Models.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Models.NewService
{
    [JsonConvertible]
    public class AccountInfoEx
    {
        public List<Group> groups { get; set; }
        [JsonProperty("user_data")]
        public UserAccount UserAccount  { get; set; }
        public string id { get; set; }
        public string datetime { get; set; }
        public SettingInfo Settings { get; set; }
        public List<Url> vods { get; set; }
    }




}
