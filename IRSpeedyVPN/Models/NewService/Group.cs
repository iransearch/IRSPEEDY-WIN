using System;
using System.Collections.Generic;

namespace IRSpeedyVPN.Models.NewService
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Group
    {
        public int id { get; set; }
        public string title { get; set; }
        public string en_title { get; set; }
        public int status { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public List<ServerEx> servers { get; set; }
    }




}
