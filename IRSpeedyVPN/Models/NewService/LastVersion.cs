using System;

namespace IRSpeedyVPN.Models.NewService
{
    public class LastVersion
    {
        public int id { get; set; }
        public string type { get; set; }
        public string version_number { get; set; }
        public string version_url { get; set; }
        public object update_change_log { get; set; }
        public int force_update { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
}
