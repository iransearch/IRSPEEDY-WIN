namespace IRSpeedyVPN.Models.Services
{
    public class Setting
    {
        public string ID { get; set; }
        public string name { get; set; }
        public string content { get; set; }
        public string sec { get; set; }
        public override string ToString()
        {
            return string.Format($"{name} = {content}");
        }
    }

}