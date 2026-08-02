using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;

namespace IRSpeedyVPN.Models.NewService
{
    [JsonConvertible]
    public class ServerEx:IServer
    {
        [JsonProperty("id")]
        public int ID { get; set; }
        public string title { get; set; }        
        [JsonProperty("en_title")]
        public string Country { get; set; }
        public int group_id { get; set; }
        public int status { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public List<Url> urls { get; set; }
        public string Protocol { get; set; }
    }




}
