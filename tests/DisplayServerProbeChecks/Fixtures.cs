using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IRSpeedyVPN.Interfaces
{
    interface IVPNService
    {
        int ID { get; }
        string Name { get; }
        string CountryCode { get; }
        string Country { get; }
        bool IsUrlTestSupported { get; }
        List<Url> GetServerUrls();
    }
}

namespace IRSpeedyVPN.Models { public enum VPNType { NORMAL, CHAIN } }

namespace IRSpeedyVPN.Residential
{
    abstract class ResidentialService : IVPNService
    {
        public abstract int ID { get; }
        public abstract string Name { get; }
        public abstract string CountryCode { get; }
        public abstract string Country { get; }
        public abstract bool IsUrlTestSupported { get; }
        public abstract List<Url> GetServerUrls();
    }

    static class ResidentialPresentation
    {
        public static string ConnectionUri(string link) => link.Split('#')[0];
    }
}

internal sealed class TestRow : IVPNService
{
    private readonly List<Url> urls;
    public int ID { get; }
    public string Name => "Fixture";
    public string CountryCode => Country;
    public string Country { get; }
    public bool IsUrlTestSupported { get; set; } = true;

    internal TestRow(int id, int members, string country = null)
    {
        ID = id;
        Country = country ?? "Country " + id;
        urls = Enumerable.Range(1, members).Select(i => new Url
        {
            id = i,
            url = $"vless://fixture@server-{id}-{i}.example.invalid:443"
        }).ToList();
    }

    public List<Url> GetServerUrls() => urls;
}
