using System;
using System.Collections.Concurrent;
using System.Linq;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;

internal static class Program
{
    private static void Main()
    {
        var lines = new ConcurrentQueue<string>();
        LogHelper.Sink = lines.Enqueue;
        TunnelPlusService.Exercise();
        var output = string.Join("\n", lines.ToArray());
        Require(output.Contains("selectedMode=Proxy") && output.Contains("appliedMode=TUN"), "Selected/applied mode must be distinguished.");
        Require(output.Contains("appliedMode=Proxy"), "Established proxy mode must be explicit.");
        Require(output.Contains("appliedMode=not-connected"), "Do not label an idle service as connected.");
        Require(output.Contains("schema=network-core-v1") && output.Contains("appMvid="), "Build/session identification missing.");
        Require(output.Contains("category=network-changed"), "Core signal missing.");
        Require(!output.Contains("PRIVATE_VALUE") && !output.Contains("example.com") && !output.Contains("secret://"), "Core output disclosed private data.");
        Require(ConnectionDiagnostics.Fingerprint("one") == ConnectionDiagnostics.Fingerprint("one"), "Session fingerprints must be stable.");
        Require(ConnectionDiagnostics.Fingerprint("one") != ConnectionDiagnostics.Fingerprint("two"), "Different members need distinct fingerprints.");
        LogHelper.Sink = s => throw new InvalidOperationException("simulated disk failure");
        TunnelPlusService.Exercise();
        ConnectionDiagnostics.Write("check", "disk-failure");
        Console.WriteLine("PASS: mode distinction, build identity, core-output privacy, member correlation, and logging failure isolation.");
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
}
