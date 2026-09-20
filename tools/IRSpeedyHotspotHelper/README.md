# Experimental Wi-Fi Direct VPN sharing

Standalone IRSPEEDY helper, not integrated into the production WPF app/installer.
Windows 10 build 19041+ / Windows 11; elevated .NET 8 desktop process.
No verified persistent kill switch. Windows hardware acceptance is required.

## Current startup mode: wifi-direct

The helper now uses WiFiDirectAdvertisementPublisher with autonomous group owner
and LegacySettings enabled. Phones/TVs can join using the generated SSID/password,
without a Wi-Fi Direct app. It does not call StartTetheringAsync or select an
Internet connection profile. Windows Mobile Hotspot must remain off.

AP creation and Internet sharing are separate:
1. Require active irspeedy-tun, no existing ICS and no active Wi-Fi Direct adapter.
2. Persist a version 3 recovery journal before creating the publisher.
3. Create a fresh legacy AP, register status and incoming connection callbacks,
   and wait up to 10 seconds for Started (abort/timeout are errors).
4. Identify the newly activated Wi-Fi Direct adapter, waiting up to eight seconds
   only if it has not appeared. Ambiguity or foreign sharing aborts.
5. Enable ICS on the selected TUN/public and Wi-Fi Direct/private pair and verify
   the pair. No eight-second wait for automatic ICS: this AP API does not create it.
6. Admit password-authenticated clients and disclose credentials only after the
   pair is verified. Keep WiFiDirectDevice handles until disconnect or stop.
7. Poll health/parent heartbeat as before; stop the publisher and clean owned ICS.

SGuard remains the TUN owner; no extra VPN core, drivers or third-party binaries.
The shared ICS backend still calls EnableSharing(0). This may still fail with
80040201: Wi-Fi Direct AP success alone does not prove the ICS issue is fixed.
We have not established the competitor's internal API implementation.

## Build and test

Download the complete agent/windows-hotspot-poc branch. Run Build-Hotspot.cmd beside
IRSpeedyVPN.sln with .NET 8 SDK x64 installed. It runs tests and publishes a fresh
self-contained win-x64 folder under artifacts/hotspot-poc/build-*/publish.
The sibling build.log contains build output. The test requests UAC elevation.

Before testing, stop the competitor VPN/sharing and Windows Mobile Hotspot.
Connect IRSPEEDY in full TUN mode; select the CURRENT SGuard64 PID in the launcher.
The launcher must show Startup v3: Wi-Fi Direct Legacy AP + ICS.
There is no password prompt. Only a verified start displays the generated SSID and
temporary password. Connect a phone with proxy None and mobile data disabled.
Keep the terminal open; the test runs for 120 seconds, Q stops early.

Confirm DHCP, VPN exit IPv4, IPv6, DNS and video/UDP connectivity independently.
Test disconnect, core exit and stopping the driver. Hardware, driver and Windows
policy differences can cause wfd.start or wfd.accept-client failures.
Do not enable Mobile Hotspot during the test: it takes precedence over Wi-Fi Direct.

Manual build from repository root:

```powershell
dotnet run --project tests/HotspotChecks/HotspotChecks.csproj
dotnet publish tools/IRSpeedyHotspotHelper/IRSpeedyHotspotHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/hotspot-poc
```

Keep the entire published folder together and retain THIRD-PARTY-NOTICES.txt.
No firewall resets, global ICS disable, service restart or policy bypass is used.

## Diagnostics

ICS subscriber-failure retry: EnableSharing retries only COM HRESULT 80040201,
up to five calls per role with 250/350/500/500 ms waits. Each call uses a fresh
EnumEveryConnection read and revalidates TUN, private adapter and sharing ownership.
Foreign sharing, missing/down adapters and other HRESULTs abort immediately.
After a failing call, inspect actual state: an already-applied role is not written
twice. The complete pair must still pass normal verification before credentials
are released. Exhaustion preserves the COM error and triggers rollback.
The diagnostic ics.enableAttempts array records each attempt/result/HRESULT.
On failure the test driver also reads SharedAccess, Netman and EventSystem status;
it does not restart services. A transient cause is not yet established on the test PC.

Startup mode is wifi-direct. Errors expose separate wfd.* or ics.* stages.
Wi-Fi backend reports publisherStatus, publisherError and sanitized connectionError.
ICS observations record wfd-adapter-ready, before-bind, bind-verify and failure state.
A Started publisher is not Internet access. ICS pair verification is not an exit-IP test.
Client count tracks retained WiFiDirectDevice associations, not traffic activity.
Callback failures appear in status without client identifiers or exception messages.

Do not share the successful start response or screenshots containing Wi-Fi credentials.
Diagnostic/error snapshots contain no password, SSID, MAC address or packet contents.
Error snapshots are captured before cleanup so publisher success is not obscured by Stop.

## Recovery and limitations

The atomic administrator/SYSTEM-only journal at
%ProgramData%\IRSpeedyHotspotPoC\session.json uses version 3 / Kind=wifi-direct.
Stop refuses a journal/backend mismatch. Version 1/2 journals use the original
Mobile Hotspot backend only for recovery, then the process exits; relaunch for v3.

```powershell
'{"command":"recover"}' | .\artifacts\hotspot-poc\IRSpeedyHotspotHelper.exe
```

Use the actual published path in an elevated shell. A new process cannot stop
another process's Wi-Fi Direct publisher. If the recorded private adapter is still
up after a hard crash, recovery refuses takeover and retains the journal. If the
crash happened before recording the adapter, any active Wi-Fi Direct adapter blocks
recovery. Stop its owning app/network first, then recover again. Do not blindly
delete the journal or disable unrelated sharing.

Pending client requests completed after stop are disposed and not admitted.
Requests are bounded to 16 pending/retained client handles. Publisher status alone
is insufficient for health; the active TUN/private pair is also checked.
The 500 ms watchdog and 10-second lease cannot guarantee zero leakage during
API hangs, crashes, sleep or races. Use non-sensitive test traffic. Production needs
persistent packet-level protection and real Windows acceptance testing.

## Verification and sources

54 pure-C# simulated-backend checks pass, including bounded HRESULT-specific retries,
partial native success, foreign sharing during retries, TUN loss, immediate Wi-Fi Direct binding,
delayed/missing adapters, no client admission on ICS failure and backend-specific
journal recovery. All helper sources compile with .NET 8 / Windows SDK 10.0.19041.56
using Roslyn directly on Linux. This does not validate Windows publishing, UAC,
WinRT activation, event callbacks or actual Wi-Fi/ICS behavior.

The Microsoft desktop console example demonstrates these APIs from an MTA desktop
process. This implementation is independent; no Microsoft sample source was copied.
The existing ICS interface attribution remains in THIRD-PARTY-NOTICES.txt.

- https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/WiFiDirectLegacyAP
- https://learn.microsoft.com/en-us/uwp/api/windows.devices.wifidirect.wifidirectadvertisementpublisher
- https://learn.microsoft.com/en-us/uwp/api/windows.devices.wifidirect.wifidirectlegacysettings
