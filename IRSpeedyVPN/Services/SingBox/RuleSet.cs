namespace IRSpeedyVPN.Services.SingBox
{
    public class RuleSet
    {
        public string format { get; set; }
        public string path { get; set; }
        public string tag { get; set; }
        public string type { get; set; }
    }

    public class DefaultDomainResolver
    {
        public string server { get; set; }
        public string strategy { get; set; }
    }
}
