using System.Collections.Generic;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Interfaces;

namespace IRSpeedyVPN.Interfaces
{
    internal interface IVPNService
    {
        int ID { get; }
        string Country { get; }
        string CountryCode { get; }
        string Name { get; }
        bool IsUrlTestSupported { get; }
        List<Url> GetServerUrls();
    }
}

namespace IRSpeedyVPN.Models.NewService { public enum VPNType { CHAIN, NORMAL } }
namespace IRSpeedyVPN.Residential
{
    internal static class ResidentialPresentation
    {
        internal static string ConnectionUri(string link) => link.Split('#')[0];
    }
    internal abstract class ResidentialService : Row { }
}

internal class Row : IVPNService
{
    public int ID { get; set; }
    public string Country => "Country " + ID;
    public string CountryCode => "T" + ID;
    public string Name => "xfast";
    public bool IsUrlTestSupported { get; set; } = true;
    public List<Url> Members { get; } = new List<Url>();
    public List<Url> GetServerUrls() => Members;
}
