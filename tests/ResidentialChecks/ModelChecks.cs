using System;
using System.Linq;
using System.Text;
using IRSpeedyVPN.Residential;

internal static class ModelChecks
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Invalid(string body) { bool rejected = false; try { ResidentialSubscription.Parse(body); } catch (FormatException) { rejected = true; } Check(rejected, "invalid subscription rejected"); }
    static void Main()
    {
        Check(ResidentialPresentation.DisplayPing(101) == 71 && ResidentialPresentation.DisplayPing(5) == 4 && ResidentialPresentation.DisplayPing(-1) == -1, "Residential display rounding and failed result");
        Check(ResidentialPresentation.FormatBytes(1048576) == "1.0 MB" && ResidentialPresentation.FormatBytes(1073741824) == "1.0 GB", "shared invariant units");
        Check(ResidentialPresentation.Usage(214748365, 1181116006) == "0.2 / 1.1 GB", "usage uses one common unit");
        var t = ResidentialTraffic.Parse("upload=10; download=20; total=100; expire=1000");
        Check(t.Used == 30 && t.Remaining == 70 && Math.Abs(t.Ratio - .7) < .001, "traffic sums uploads/downloads and shows remaining");
        Check(ResidentialTraffic.Parse("upload=9223372036854775807; download=20; total=100").Exhausted, "overflow saturates and exhausted quota clamps");
        Check(ResidentialTraffic.Parse("upload=0; download=1; total=0").RemainingText == "∞", "Android unlimited quota contract");
        foreach (var header in new[] { "", "upload=1;total=2", "upload=-1;download=0;total=2", "upload=1;upload=2;download=0;total=3", "upload=x;download=0;total=3" })
            Check(ResidentialTraffic.Parse(header) == null, "invalid traffic remains hidden");
        Check(ResidentialPresentation.IsEco("39", "VIP") && ResidentialPresentation.IsEco(null, "اکو – سرویس") && ResidentialPresentation.IsEco(null, "eco plus")
            && !ResidentialPresentation.IsEco("139", "economy"), "Android ECO gate uses group ID and whole tokens");
        Check(new ResidentialConfig { enabled = true, subscription_url = "https://example.invalid/token" }.IsUsable, "HTTPS config accepted");
        foreach (string url in new[] { "http://example.invalid", "https://user:pass@example.invalid", "https://example.invalid/#token", "bad" })
            Check(!new ResidentialConfig { enabled = true, subscription_url = url }.IsUsable, "unsafe config rejected");
        string link = "vless://abc@example.invalid:443?type=xhttp&extra=%7B%7D#Frankfurt%2C%20DE%20%2F%20Vodafone%20GmbH";
        var plain = ResidentialSubscription.Parse(link + "\n" + link + "\n" + "trojan://pw@other.invalid:443#London, UK / ISP+Name");
        Check(plain.Length == 2 && plain[0].Nodes.Count == 1 && plain[0].Nodes[0].Link == link, "deduplicated country pool preserves original protocol/header URI");
        Check(plain[0].Code == "DE" && plain[0].Nodes[0].City == "Frankfurt" && plain[0].Nodes[0].Isp == "Vodafone GmbH" && plain[1].Code == "GB" && plain[1].Nodes[0].Isp == "ISP+Name", "Android metadata and plus-sign semantics");
        Check(ResidentialSubscription.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(link)))[0].Nodes[0].Link == link, "base64 subscription");
        string base64url = Convert.ToBase64String(Encoding.UTF8.GetBytes(link)).TrimEnd('=').Replace('+','-').Replace('/','_');
        Check(ResidentialSubscription.Parse(base64url)[0].Nodes[0].Link == link, "URL-safe unpadded base64 subscription");
        Invalid("garbage"); Invalid("vless://a@host:443#Unknown"); Invalid("http://example.invalid/#Frankfurt, DE"); Invalid(new string('a', ResidentialSubscription.MaxBytes + 1));
        Console.WriteLine("Residential model checks: " + count);
    }
}
