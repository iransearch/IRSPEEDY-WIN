using System;
using IRSpeedyVPN.Common;

internal static class StartupFailureChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string denied = "start inbound/mixed[PRIVATE_TAG]: listen tcp 127.0.0.1:10808: bind: An attempt was made to access a socket in a way forbidden by its access permissions. password=PRIVATE_VALUE";
        string fields = CoreStartFailureDiagnostic.Fields(denied);
        check(fields.Contains("component=inbound") && fields.Contains("inboundType=mixed")
            && fields.Contains("operation=bind") && fields.Contains("network=tcp")
            && fields.Contains("localPort=10808") && fields.Contains("bindScope=loopback"), "local socket access denial has actionable metadata");
        check(fields.Contains("nativeError=-1") && fields.Contains("codeSource=unavailable"), "missing native code is never guessed from access-denied text");
        check(!fields.Contains("PRIVATE") && !fields.Contains("127.0.0.1"), "startup metadata excludes raw tags, credentials and bind addresses");

        fields = CoreStartFailureDiagnostic.Fields("app/proxyman/inbound: failed to listen UDP on 127.0.0.1:7085: WSAEACCES");
        check(fields.Contains("component=xray") && fields.Contains("network=udp") && fields.Contains("localPort=7085")
            && fields.Contains("nativeError=10013") && fields.Contains("codeSource=symbol"), "Xray UDP bridge and reported Winsock symbol are retained");
        fields = CoreStartFailureDiagnostic.Fields("start inbound/http[PRIVATE]: listen tcp6 [::1]:18443: bind: socketError=10048");
        check(fields.Contains("network=tcp6") && fields.Contains("localPort=18443") && fields.Contains("bindScope=loopback")
            && fields.Contains("nativeError=10048") && fields.Contains("codeSource=reported"), "IPv6 local bind and numeric native error are retained");
        fields = CoreStartFailureDiagnostic.Fields("listen udp 0.0.0.0:7080: bind: permission denied");
        check(fields.Contains("bindScope=wildcard") && fields.Contains("localPort=7080"), "wildcard bind is classified without logging its address");
        fields = CoreStartFailureDiagnostic.Fields("dial tcp 198.51.100.4:443: connect: permission denied");
        check(fields.Contains("operation=dial") && fields.Contains("network=tcp") && fields.Contains("localPort=-1")
            && !fields.Contains("198.51.100"), "remote endpoint is never reported as a local bind");
        fields = CoreStartFailureDiagnostic.Fields("initialize inbound/tun[tun-in]: configure tun interface: Access is denied.");
        check(fields.Contains("component=tun") && fields.Contains("operation=configure") && fields.Contains("localPort=-1"), "TUN access denial is distinguished from a proxy port failure");
        check(CoreStartFailureDiagnostic.Fields("open wintun.dll: Access is denied.").Contains("component=tun-driver")
            && CoreStartFailureDiagnostic.Fields("initialize network monitor: permission denied").Contains("component=network-monitor"), "driver and network monitor failures are separate");
        check(CoreStartFailureDiagnostic.IsDnsComponent("create service: initialize DNS server[dns-remote]: outbound not found")
            && !CoreStartFailureDiagnostic.IsDnsComponent(denied)
            && !CoreStartFailureDiagnostic.IsDnsComponent("start inbound/mixed[dns-server]: listen tcp 127.0.0.1:10808: bind denied"),
            "only Core DNS-component rejection authorizes standard-DNS fallback");
        // Throne-G 15a68bb emits the first parser cause. 21c9a2c with the pinned
        // sing-box f154bec emits the other parser, DNS manager and inbound causes.
        const string unknownRace = "dns.servers[0]: unknown transport type: vpn-dns-race";
        string configPrefix = "decode config at {\"dns\":{\"servers\":[{\"type\":\"vpn-dns-race\"}]},"
            + "\"outbounds\":[{\"server\":\"192.0.2.1\",\"uuid\":\"049797c0-fe08-4b8f-ad98-02f750be6475\"}],"
            + "\"test\":\"PRIVATE }\\\": dns.rules[0].timeout " + new string('x', 9000) + "\"}: ";
        const string unknownPool = "unknown outbound type: auto-selector-round-robin";
        check(CoreStartFailureDiagnostic.UnsupportedFeature(configPrefix + unknownRace) == "vpn-dns-race"
            && CoreStartFailureDiagnostic.UnsupportedFeature(configPrefix + unknownPool) == "auto-selector-round-robin",
            "startup log identifies both unsupported features after a large config dump");
        check(CoreStartFailureDiagnostic.UnsupportedFeature("decode config at {\"remark\":\"" + unknownPool + "\"}: invalid settings") == null
            && CoreStartFailureDiagnostic.UnsupportedFeature("error password=\"" + unknownRace + "\"") == null
            && CoreStartFailureDiagnostic.UnsupportedFeature(unknownPool + "-extra") == null
            && CoreStartFailureDiagnostic.UnsupportedFeature(unknownRace + "-extra") == null,
            "unsupported-feature fields ignore private config and credential text and require the exact type");
        CheckLoggedCause(check, configPrefix + unknownPool, "unknown-outbound-type", "\"" + unknownPool + "\"");
        CheckLoggedCause(check, configPrefix + unknownRace, "unknown-dns-transport", "\"" + unknownRace + "\"");
        CheckLoggedCause(check, configPrefix + "dns.rules[0].timeout: time: invalid duration \"invalid\"",
            "invalid-dns-duration", "unavailable");
        CheckLoggedCause(check, configPrefix + "start inbound/mixed[proxy]: listen tcp 127.0.0.1:37233: bind: address already in use",
            "address-in-use", "unavailable");
        CheckLoggedCause(check, configPrefix + denied, "socket-access-denied", "unavailable");
        CheckLoggedCause(check, configPrefix + "open wintun.dll: Access is denied.", "socket-access-denied",
            "\"open wintun.dll: Access is denied.\"");
        CheckLoggedCause(check, configPrefix + "context deadline exceeded", "timeout", "\"context deadline exceeded\"");
        CheckLoggedCause(check, configPrefix + "invalid settings", "unclassified", "unavailable");
        CheckLoggedCause(check, configPrefix + "duplicate outbound/endpoint tag: timeout", "unclassified", "unavailable");
        foreach (var suffix in new[] { " /private/path", " 192.0.2.1", " [::1]", " vless://private.example",
            " 049797c0-fe08-4b8f-ad98-02f750be6475", " SGVsbG9Xb3JsZA", " password=example",
            new string('x', 161), "\r\nforged", " ｋ" })
            check(CoreStartFailureDiagnostic.SafeCause(configPrefix + unknownPool + suffix) == "unavailable",
                "cause fails closed on unexpected trailing data");
        var dnsErrors = new[] {
            unknownRace,
            configPrefix + unknownRace,
            "start service: start dns/vpn-dns-race[dns-remote]: outbound detour not found: proxy",
            configPrefix + "dns.rules[0].timeout: time: invalid duration \"invalid\"",
            "create service: initialize dns router: parse dns rule[0]: domain_regex: parse expression 0: error parsing regexp: missing closing ]: `[`",
            "create service: initialize DNS server[0]: vpn-dns-race requires 2 to 7 primary servers"
        };
        for (int i = 0; i < dnsErrors.Length; i++)
        {
            string dnsError = dnsErrors[i];
            fields = CoreStartFailureDiagnostic.Fields(dnsError);
            check(CoreStartFailureDiagnostic.IsDnsComponent(dnsError) && fields.Contains("component=dns") && !fields.Contains("PRIVATE"),
                "Core DNS rejection and privacy-safe diagnostic agree for fixture " + i);
        }
        var otherErrors = new[] {
            configPrefix + "duplicate outbound/endpoint tag: proxy",
            configPrefix + "duplicate outbound/endpoint tag: dns server",
            "start inbound/mixed[vpn-dns-race]: listen tcp 127.0.0.1:37233: bind: address already in use",
            "start service: create netlink socket: operation not permitted",
            "initialize inbound[0]: dns server options rejected",
            "start inbound/private_type[dns server]: invalid settings",
            "initialize inbound/tun[tun-in]: configure tun interface: dns server options rejected",
            "start dns/vpn-dns-race[dns-remote]: permission denied",
            "start dns/vpn-dns-race[dns-remote]: socketError=10013",
            "start dns/vpn-dns-race[dns-remote]: listen udp 127.0.0.1:10808: bind: address already in use",
            "unknown transport type: vpn-dns-race-test",
            "decode config at {\"tag\":\"unknown transport type: vpn-dns-race"
        };
        for (int i = 0; i < otherErrors.Length; i++)
            check(!CoreStartFailureDiagnostic.IsDnsComponent(otherErrors[i]),
                "non-DNS or blocked Core error cannot authorize fallback for fixture " + i);
        fields = CoreStartFailureDiagnostic.Fields("start inbound/PRIVATE_TYPE[PRIVATE_TAG]: listen tcp 127.0.0.1:99999: bind: denied");
        check(fields.Contains("inboundType=unknown") && fields.Contains("localPort=-1") && !fields.Contains("PRIVATE"), "unknown types and invalid port values are bounded");
        check(CoreStartFailureDiagnostic.Fields(new string('x', 9000)).Contains("errorTruncated=True"), "large startup errors have an explicit truncation marker");
        fields = CoreStartFailureDiagnostic.Fields("error password=\"listen tcp 127.0.0.1:7085\" {\"remark\":\"wintun\"}");
        check(fields.Contains("component=unknown") && fields.Contains("network=unknown") && fields.Contains("localPort=-1"), "private payload cannot masquerade as bind or driver metadata");

        const string userMessage = "خطا در برقراری اتصال VPN";
        foreach (string error in new[] { denied, "short technical error", "startup-timeout", "rpc-transport-failure",
            new CoreRuntimeIdentity().Failure("permission denied"), "تنظیمات مسیریابی یا DNS معتبر نیست" })
            check(CoreRuntimeIdentity.UserConnectionError(error) == userMessage, "connection failure displays only the requested text");
        check(CoreRuntimeIdentity.UserConnectionError(new CoreRuntimeIdentity().IncompatibleFailure()) == CoreRuntimeIdentity.CoreCompatibilityMessage,
            "the existing Core failure banner has a localized upgrade message");
        check(CoreRuntimeIdentity.UserConnectionError(null) == "" && CoreRuntimeIdentity.UserConnectionError("") == ""
            && CoreRuntimeIdentity.UserConnectionError("سروری یافت نشد") == "" && CoreRuntimeIdentity.UserConnectionError("Connection canceled.") == "",
            "normal disconnect, cancellation and empty search status remain silent");
    }

    private static void CheckLoggedCause(Action<bool, string> check, string error, string expectedReason, string expectedCause)
    {
        check(CoreStartFailureDiagnostic.Reason(error) == expectedReason
            && CoreStartFailureDiagnostic.SafeCause(error) == expectedCause,
            "startup reason and sanitized cause agree for " + expectedReason + "/" + expectedCause);
    }
}
