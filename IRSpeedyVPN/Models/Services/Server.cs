using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Models.Services
{
    public class Server:IServer
    {
        public int ID { get; set; }
        public string Address { get; set; }
        public string Port { get; set; }
        public string Country { get; set; }
        public string Service { get; set; }
        public string Protocol { get; set; }
        public string Optional1 { get; set; }
        public string Optional2 { get; set; }
        public List<Url> urls { get => (new Url[] { new Url() { url = Address } }).ToList(); }
        public override string ToString()
        {
            return string.Format("{0}: {1}", Service, Address);
        }
    }

}