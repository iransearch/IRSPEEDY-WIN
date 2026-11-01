using IRSpeedyVPN.Common;
using System;
using System.IO;
using System.Linq;

internal static class HostkeyChecks
{
    internal static void Run()
    {
        const string prefix = "[CoreDiagnostic] schema=core-network-v2 pid=7 seq=2 monoMs=40 box=2 dropped=0 target=hostkey trace=9 request=42 ";
        string safe;
        foreach (var fields in new[] {
            "event=hostkey-begin",
            "event=hostkey-dns-begin query=11 queryType=28 strategy=0",
            "event=hostkey-dns-winner query=11 index=6 cacheHit=true source=ip-cache error=none errno=0",
            "event=hostkey-dns-end query=11 a=0 aaaa=1 rcode=0 elapsedMs=12 error=none errno=0",
            "event=hostkey-dns-address query=11 index=0 peer=2606:4700:20::1 family=6 public=true",
            "event=hostkey-route route=main tag=1241936d4dd3aad6 inboundKind=mixed network=tcp family=0 count=1",
            "event=hostkey-pool-dial route=main memberTag=8e693cdf6ffbe175 attempt=0 family=6 elapsedMs=900 error=timeout errno=10060",
            "event=hostkey-dial-address query=0 index=0 peer=10.10.34.36 family=4 public=false",
            "event=hostkey-end elapsedMs=4000 error=reset errno=10054",
            "event=hostkey-limit" })
        {
            if (!CoreDiagnosticMetadata.TryParse(prefix + fields, out safe)
                || !LogPolicy.ShouldWrite("[ConnectionDiagnostic] stage=core-detail " + safe))
                throw new Exception("Hostkey event lost by production filters: " + fields);
        }
        foreach (var invalid in new[] {
            "event=hostkey-begin password=PRIVATE", "event=hostkey-begin peer=104.26.12.47",
            "event=hostkey-dns-address peer=private.example", "event=hostkey-dns-address peer=fe80::1%3",
            "event=hostkey-end error=private.example", "event=hostkey-end error=none\npassword=PRIVATE",
            "event=hostkey-pool-dial memberTag=private.example", "event=hostkey-begin target=other" })
            if (CoreDiagnosticMetadata.TryParse(prefix + invalid, out safe)) throw new Exception("Unsafe Hostkey metadata accepted.");
        if (CoreDiagnosticMetadata.TryParse(prefix.Replace("target=hostkey ", "") + "event=hostkey-begin", out safe)
            || CoreDiagnosticMetadata.TryParse(prefix.Replace("trace=9", "trace=0") + "event=hostkey-begin", out safe))
            throw new Exception("Unscoped Hostkey metadata accepted.");
        if (CoreDiagnosticMetadata.TryParse("[CoreDiagnostic] schema=core-network-v2 event=dns-race-result pid=7 seq=2 box=2 peer=104.26.12.47", out safe))
            throw new Exception("General DNS privacy policy relaxed.");
        var nativeLog = Environment.GetEnvironmentVariable("IRSPEEDY_HOSTKEY_NATIVE_LOG");
        if (!string.IsNullOrEmpty(nativeLog))
        {
            var lines = File.ReadAllLines(nativeLog).Where(line => line.StartsWith("[CoreDiagnostic] ", StringComparison.Ordinal)
                && line.Contains(" event=hostkey-")).ToArray();
            if (lines.Length == 0) throw new Exception("Native Hostkey diagnostic fixture is empty.");
            foreach (var line in lines)
                if (!CoreDiagnosticMetadata.TryParse(line, out safe) || !LogPolicy.ShouldWrite("[ConnectionDiagnostic] stage=core-detail " + safe))
                    throw new Exception("Real native Hostkey event was dropped by Windows filters.");
            foreach (var stage in new[] { "hostkey-dns-begin", "hostkey-dns-end", "hostkey-dns-winner", "hostkey-route", "hostkey-end" })
                if (!lines.Any(line => line.Contains("event=" + stage + " "))) throw new Exception("Native fixture lacks " + stage);
            Console.WriteLine("PASS " + lines.Length + " real native Hostkey events retained by Windows");
        }
        Console.WriteLine("PASS Hostkey diagnostic parsing, correlation, privacy and final log filtering");
    }
}
