"""Compile and exercise the production startup method with a fake Core.

Run with Python 3 and the .NET 8 SDK: python tests/ParallelDnsChecks/startup_flow.py
"""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / 'IRSpeedyVPN'
source = (APP / 'Services/TunnelPlusService.cs').read_text(encoding='utf-8-sig')
startup = source[source.index('        private bool TryStartCoreWithConfig('):
                 source.index('        private string[] GetShieldFiles()')]
diagnostics = (APP / 'Services/TunnelPlusService.Diagnostics.cs').read_text(encoding='utf-8-sig')
start_failure = diagnostics[diagnostics.index('        private string DiagnosticStartFailure('):
                            diagnostics.index('        private void Diagnostic(string stage')]

fixture = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services.Libcore;
using Newtonsoft.Json.Linq;

namespace System.Windows.Forms { static class Application { public static string ExecutablePath => "fixture.exe"; } }
namespace IRSpeedyVPN.Resource {
    static class ResourceManager { public static string PackagedCoreSha256 => new string('a', 64); }
}
namespace IRSpeedyVPN.Services.Xray {
    class AutoSelectorPlan { public int MainMembers, AiMembers; public bool AiEnabled; }
    static class SmartIpRouting {
        public const string AiRuleTag = "ai", AiBalancerTag = "ai", AiProxyPrefix = "ai-";
        public static bool IsEnabled() => false;
    }
}
namespace IRSpeedyVPN.Services.SplitTunneling {
    static class SplitTunnelStore { public static (bool Enabled, List<string> Apps) Load() => (false, new List<string>()); }
    static class SplitTunnelPolicyBuilder { public static string Apply(string config, object settings, string app) => config; }
}
namespace IRSpeedyVPN.Services.SingBox {
    static class TunBrowserCompatibility { public static string Apply(string config) => config; }
    static class VpnTimeZoneRouting { public static string Apply(string config) => config; }
}
namespace IRSpeedyVPN.Services.Traffic {
    static class TrafficConfiguration { public static string Enable(string config) => config; }
    class TrafficUsageService {
        public static readonly TrafficUsageService Instance = new TrafficUsageService();
        public void StartSession(Func<int, object> query) { }
    }
}
namespace IRSpeedyVPN.Services.Libcore {
    class LibcoreServiceClient {
        public static Func<LoadConfigReq, ErrorResp> OnStart;
        public static CoreBuildInfo BuildInfo;
        public LibcoreServiceClient(string host, int port, int timeout = 0) { }
        public CoreBuildInfo QueryBuildInfo(int timeout, CancellationToken token) => BuildInfo;
        public ErrorResp StartWithDeadline(LoadConfigReq req, int timeout, CancellationToken token) => OnStart(req);
        public object QueryConnections(int timeout) => null;
    }
}
namespace IRSpeedyVPN.Services {
    static class CoreDiagnosticMetadata {
        public static int ProcessId;
        public static int ListenerPid(int port) => ProcessId;
    }
    static class ConnectionDiagnostics {
        public static string Fingerprint(string config) => "fixture";
        public static string SafeCoreStartText(string error) => "[redacted]";
        public static void RequestSnapshot() { }
    }
    static class ErrorReporting { public static void Annotate(Exception ex, params string[] fields) { } }
    static class LogHelper {
        public static readonly List<string> Identity = new List<string>();
        public static void WriteLog(object value) {
            if (value is string text && text.StartsWith("[CoreRuntimeIdentity]")) Identity.Add(text);
        }
        public static void WriteExLog(string value) { }
    }
    static class GeoRoutingFallback {
        public static (string SingBoxConfig, string XrayConfig, string[] MissingFiles) Apply(string config, string xray, string directory)
            => (config, xray, Array.Empty<string>());
    }
    class TunnelPlusService {
        private const int CorePort = 1, CoreStartDeadlineMs = 30000;
        private static readonly string corePath = typeof(TunnelPlusService).Assembly.Location;
        private const string legacyError = "dns.servers[0]: unknown transport type: vpn-dns-race";
        private const string dnsError = "create service: initialize DNS server[0]: vpn-dns-race requires 2 to 7 primary servers";
        private const string nonDnsError = "start inbound/mixed[proxy]: listen tcp 127.0.0.1:10808: bind: address already in use";
        private const string standardConfig = @"{""dns"":{""servers"":[{""type"":""https"",""tag"":""dns-remote"",""detour"":""proxy"",""server"":""1.1.1.1"",""path"":""/dns-query""}]},""outbounds"":[{""type"":""direct"",""tag"":""proxy""}]}";
        private readonly List<string> calls = new List<string>();
        private readonly List<LoadConfigReq> starts = new List<LoadConfigReq>();
        private readonly List<string> dnsConfig = new List<string>();
        private readonly List<string> failures = new List<string>();
        private CoreBuildInfo buildInfo;
        private int listenerPid = -1;
        private CancellationToken cancellation;
        private bool userCancelRequested, coreOwned, lastVpnMode;
        private Process coreProcess;
        private string diagnosticXrayConfigId;
        private Xray.AutoSelectorPlan autoSelectorPlan;
        private Action onStop;
        private bool PauseSharingBeforeCoreRestart() => true;
        private string ResolveCorePath() => corePath;
        private void EnsureCoreRunning(int port, ref Process process, ref bool owned) { }
        private CancellationToken GetStartCancellation() => cancellation;
        private void TryStopCore() { calls.Add("cleanup"); onStop?.Invoke(); }
        private void RecoverCoreAfterFailedStart() => calls.Add("recover");
        private bool SafeStopCore(LibcoreServiceClient client, out Exception failure) { failure = null; calls.Add("pre-stop"); return true; }
        private void Diagnostic(string stage, string detail = null) {
            if (stage == "parallel-dns-config") dnsConfig.Add(detail);
            if (stage == "core-start-config-error" || stage == "core-start-incompatible-error") failures.Add(detail);
        }
        private void AnnotateCoreStartup(Exception ex, CoreRuntimeIdentity identity, string config, string xray, bool needXray, string stage) { }
        private T ExecuteCoreCall<T>(Func<LibcoreServiceClient, T> call, string operation, Action<Exception> annotate, Action<Exception> recovered)
            => call(new LibcoreServiceClient("127.0.0.1", CorePort));
'''

checks = r'''
        private static void Check(bool condition, string description) {
            if (!condition) throw new Exception(description);
            Console.WriteLine("PASS " + description);
        }
        private bool Start(IEnumerable<string> errors, out string error, string config = standardConfig) {
            var responses = new Queue<string>(errors);
            LibcoreServiceClient.BuildInfo = buildInfo;
            CoreDiagnosticMetadata.ProcessId = listenerPid;
            LibcoreServiceClient.OnStart = request => {
                starts.Add(request);
                calls.Add("start");
                string failure = responses.Dequeue();
                if (failure == legacyError) failure = "decode config at " + request.CoreConfig + ": " + failure;
                return new ErrorResp { Error = failure };
            };
            return TryStartCoreWithConfig(config, out error, true, "{}", "prefer_ipv4");
        }
        public static void Main() {
            var service = new TunnelPlusService();
            Check(service.Start(new[] { dnsError, null }, out var error) && error == null
                && service.calls.SequenceEqual(new[] { "pre-stop", "start", "cleanup", "pre-stop", "start" }),
                "other DNS rejection calls TryStopCore once between the two Start calls");
            Check(LogHelper.Identity.Count == 2 && LogHelper.Identity.All(entry =>
                entry.Contains("Packaged Core SHA256: " + ResourceManager.PackagedCoreSha256)
                && entry.Contains("Core package match: unknown") && entry.Contains("Core RPC identity: unavailable")),
                "each attempt logs the packaged hash and the unavailable fake process identity");
            Check(service.dnsConfig.SequenceEqual(new[] { "enabled=True added=True", "enabled=False added=False" })
                && (string)JObject.Parse(service.starts[0].CoreConfig)["dns"]["servers"][0]["type"] == "vpn-dns-race"
                && service.starts[1].CoreConfig == standardConfig
                && service.starts.All(r => r.NeedXray && r.XrayConfig == "{}" && r.XrayOutboundDnsStrategy == "prefer_ipv4"),
                "fallback disables parallel DNS and restores the original sing-box and Xray inputs");

            service = new TunnelPlusService();
            Check(!service.Start(new[] { legacyError }, out error) && service.starts.Count == 1
                && service.calls.SequenceEqual(new[] { "pre-stop", "start", "cleanup" })
                && CoreRuntimeIdentity.UserConnectionError(error) == CoreRuntimeIdentity.CoreCompatibilityMessage
                && service.failures.Single().Contains("reason=unknown-dns-transport requiredFeature=vpn-dns-race cause=\"dns.servers[0]: unknown transport type: vpn-dns-race\""),
                "a missing DNS transport logs the cause, shows the upgrade message and never retries");

            service = new TunnelPlusService { listenerPid = Process.GetCurrentProcess().Id };
            Check(!service.Start(Array.Empty<string>(), out error) && service.starts.Count == 0
                && CoreRuntimeIdentity.UserConnectionError(error) == CoreRuntimeIdentity.CoreCompatibilityMessage
                && service.failures.Single().Contains("reason=package-mismatch"),
                "a package mismatch shows the upgrade message before Start");

            service = new TunnelPlusService { autoSelectorPlan = new Xray.AutoSelectorPlan(), buildInfo = new CoreBuildInfo() };
            Check(!service.Start(Array.Empty<string>(), out error) && service.starts.Count == 0
                && CoreRuntimeIdentity.UserConnectionError(error) == CoreRuntimeIdentity.CoreCompatibilityMessage
                && service.failures.Single().Contains("reason=pool-identity-not-reported"),
                "a legacy RPC reply stops a Pool request before Start");

            service = new TunnelPlusService { buildInfo = new CoreBuildInfo() };
            Check(service.Start(new string[] { null }, out error) && service.starts.Count == 1,
                "a legacy RPC reply still allows a non-Pool Start");

            service = new TunnelPlusService();
            Check(!service.Start(new[] { nonDnsError }, out error) && service.starts.Count == 1,
                "non-DNS rejection has no second Start");

            service = new TunnelPlusService();
            var userCanceled = service;
            service.onStop = () => userCanceled.userCancelRequested = true;
            Check(!service.Start(new[] { dnsError }, out error) && service.starts.Count == 1,
                "user cancellation during cleanup suppresses the DNS fallback");

            using (var source = new CancellationTokenSource()) {
                service = new TunnelPlusService { cancellation = source.Token, onStop = source.Cancel };
                Check(!service.Start(new[] { dnsError }, out error) && service.starts.Count == 1,
                    "start token cancellation during cleanup suppresses the DNS fallback");
            }

            service = new TunnelPlusService();
            const string xrayError = "unknown outbound type: auto-selector-round-robin";
            Check(!service.Start(new[] { dnsError, xrayError }, out error) && service.starts.Count == 2
                && error == new CoreRuntimeIdentity().IncompatibleFailure()
                && service.failures.Last().Contains("reason=unknown-outbound-type requiredFeature=auto-selector-round-robin"),
                "failed fallback returns the second failure through the production formatter");

            service = new TunnelPlusService();
            Check(!service.Start(new[] { dnsError, dnsError }, out error) && service.starts.Count == 2
                && service.calls.Count(call => call == "cleanup") == 2,
                "a second DNS rejection is cleaned up and never starts a third attempt");

            service = new TunnelPlusService();
            var existingRace = SingBox.ParallelDnsRouting.Apply(standardConfig);
            Check(!service.Start(new[] { dnsError }, out error, existingRace) && service.starts.Count == 1,
                "a race not added by this start does not authorize fallback");
        }
    }
}
'''

with tempfile.TemporaryDirectory(prefix='parallel-dns-startup-') as temporary:
    project = Path(temporary) / 'Checks.csproj'
    project.with_name('Program.cs').write_text(fixture + startup + start_failure + checks, encoding='utf-8')
    sources = ['Common/CoreStartFailureDiagnostic.cs', 'Common/CoreRuntimeIdentity.cs',
               'Common/NetworkFailureDiagnostic.cs', 'Services/Libcore/LibcoreMessages.cs', 'Services/SingBox/ParallelDnsRouting.cs']
    items = ''.join(f'<Compile Include={quoteattr(str(APP / name))} />' for name in sources)
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup>' + items +
                       '<PackageReference Include="Newtonsoft.Json" Version="12.0.2" />'
                       '</ItemGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(project), '-v:q'], check=True)
