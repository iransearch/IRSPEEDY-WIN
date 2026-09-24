# Proxifier browser QUIC regression checks

Run the deterministic checks on Linux with .NET 8 and GCC:

```sh
gcc -shared -fPIC tests/ProxifierQuicChecks/wfp-test.c -o /tmp/irspeedy-wfp-test.so
dotnet run --project tests/ProxifierQuicChecks -- /tmp/irspeedy-wfp-test.so
```

The production files compile as C# 7.3. A native test double validates WFP struct
layout, the mandatory executable + UDP + remote-port conditions, IPv4/IPv6
transactions and rollback. Lifecycle tests use a harmless `sleep` process to
exercise cancellation, startup failure, unexpected Proxifier exit and repeated
disconnect. The native test double and fixtures are not application dependencies.
These checks do **not** execute Windows WFP or verify browser network behaviour.

## Windows acceptance test (administrator, normal production manifest)

1. Restore Firefox `network.http.http3.enable=true` or Chromium's QUIC flag to
   Default; fully restart the browser. Use the same server that reproduced the
   problem. Connect with global Proxifier, open YouTube and play/seek a video.
2. Check the single `[Proxifier QUIC]` startup log reports at least one browser.
   `netsh wfp show state` can inspect temporary filters named
   `IRSpeedy Proxifier browser UDP/443`: each must have an app identity, protocol
   UDP (17), remote port 443, and an ALE_AUTH_CONNECT IPv4 or IPv6 layer.
3. Disconnect and check those filters disappear. Repeat connect/disconnect rapidly
   and switch to TUN, System Proxy, and Telegram-only; none should retain filters.
4. Close Proxifier unexpectedly, exit IRSpeedy normally, and terminate IRSpeedy
   with Task Manager in separate sessions. The dynamic WFP session must disappear
   in each case, including after Windows RPC rundown on forced process termination.
5. Connect a Hysteria2 node on UDP/443 and confirm it remains functional. No core,
   updater, WebView2, wildcard application, or system-wide UDP rule is created.
6. Test installed Firefox/Chrome/Edge and a portable browser. Running portable
   browsers are discovered before launch; a portable executable started afterward
   is picked up within the 5-second discovery interval (reload the page if needed).
7. Verify on x64 and the supported x86/Windows 7 build. If Windows Base Filtering
   Engine is unavailable, ordinary proxy startup remains available and a diagnostic
   identifies the filter failure; QUIC fallback is not guaranteed in that case.

Scope: known browser executables only (Chrome, Edge, Firefox, Brave, Opera,
Vivaldi, Chromium, Waterfox, LibreWolf, Floorp, Zen), exact executable paths,
UDP destination 443, outbound IPv4 and IPv6. No browser preferences are modified.
No new package, helper executable or driver is needed; Costura single-file
packaging is unchanged.

Native definitions are based on Microsoft's Windows SDK `fwpmu.h`, `fwpmtypes.h`
and `fwptypes.h`:

- https://learn.microsoft.com/en-us/windows/win32/fwp/object-management
- https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter0
- https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter_condition0
- https://learn.microsoft.com/en-us/windows/win32/api/fwptypes/ns-fwptypes-fwp_value0
