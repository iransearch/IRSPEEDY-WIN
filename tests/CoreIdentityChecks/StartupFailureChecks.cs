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
        fields = CoreStartFailureDiagnostic.Fields("start inbound/PRIVATE_TYPE[PRIVATE_TAG]: listen tcp 127.0.0.1:99999: bind: denied");
        check(fields.Contains("inboundType=unknown") && fields.Contains("localPort=-1") && !fields.Contains("PRIVATE"), "unknown types and invalid port values are bounded");
        check(CoreStartFailureDiagnostic.Fields(new string('x', 9000)).Contains("errorTruncated=True"), "large startup errors have an explicit truncation marker");
        fields = CoreStartFailureDiagnostic.Fields("error password=\"listen tcp 127.0.0.1:7085\" {\"remark\":\"wintun\"}");
        check(fields.Contains("component=unknown") && fields.Contains("network=unknown") && fields.Contains("localPort=-1"), "private payload cannot masquerade as bind or driver metadata");

        const string userMessage = "خطا در اتصال به سرویس";
        foreach (string error in new[] { denied, "short technical error", "startup-timeout", "rpc-transport-failure",
            new CoreRuntimeIdentity().Failure("permission denied"), "تنظیمات مسیریابی یا DNS معتبر نیست" })
            check(CoreRuntimeIdentity.UserConnectionError(error) == userMessage, "connection failure displays only the requested text");
        check(CoreRuntimeIdentity.UserConnectionError(null) == "" && CoreRuntimeIdentity.UserConnectionError("") == ""
            && CoreRuntimeIdentity.UserConnectionError("سروری یافت نشد") == "" && CoreRuntimeIdentity.UserConnectionError("Connection canceled.") == "",
            "normal disconnect, cancellation and empty search status remain silent");
    }
}
