# Experimental Windows VPN hotspot helper

Standalone PoC for IRSPEEDY-WIN; **not enabled in the WPF app, solution or installer**.
The existing proxy-based Share VPN remains unchanged. Base reviewed:
`agent/core-startup-diagnostics-fix` at `bbc3144a0e85a3e64ac799a60e1a0a51633ab997`.

## Scope and limitations

- Target: Windows 10 build 19041+ / Windows 11, .NET 8, elevated desktop process.
- Uses official WinRT Mobile Hotspot APIs and a minimal MIT-derived ICS interface.
  No GPL/AGPL source or downloaded third-party executable is included.
- Selects an already running `irspeedy-tun` by explicit GUID. The caller supplies
  the PID of the core that OWNS that TUN, not necessarily the Xray protocol process.
  Xray traffic can reach it through the app's existing sing-box TUN bridge.
- Requires a WinRT connection profile on that exact TUN. Some Wintun/driver/Windows
  combinations do NOT expose a usable profile or tethering capability. In that
  case it returns `tun-winrt-profile-unavailable` or `tethering-*` without choosing
  the physical Internet connection. Supporting such machines is a subsequent design
  task; this PoC intentionally does not start on a physical profile then rebind.
- Requires existing Mobile Hotspot and ICS to be off. Does NOT disable/take over
  other ICS, change firewall policy, install drivers, or restart network services.
  The rollback baseline is explicitly **no sharing**, not an arbitrary existing pair.
- SSID/passphrase are configured in Windows. They remain configured after stopping,
  just as with Windows Settings; this PoC does not restore previous AP credentials.
  Use a temporary password. The helper never logs/persists it in its own journal.
- Public/private ICS pair is verified, but this is NOT an end-to-end VPN reachability
  test. Split routing, Game Mode and direct rules can still route traffic outside VPN.
  Test using full tunnel with direct bypass rules disabled.
- **No kernel-level kill switch is implemented.** A 500 ms polling watchdog stops
  on core exit, TUN down, ICS change or heartbeat expiry (10 s), but cannot guarantee
  zero leakage during a race, hard kill, driver/COM hang, sleep or OS crash. Native
  calls can block; a timeout alone would not safely undo an in-flight mutation.
  Do not use sensitive traffic or label this production-ready / leak-proof.
- No automatic reconnect, Windows service, WPF UI or installer integration yet.
  Reconnection requires fresh capability discovery and an explicit new start.
  Only client count is exposed, not client MAC/IP identifiers.

## Build and automated checks

For the guided Windows path, double-click `Build-Hotspot.cmd` at the repository
root. Install the **.NET 8 SDK x64** first (not just the runtime) and download the
complete `agent/windows-hotspot-poc` branch. The CMD runs safety checks, builds and
publishes a self-contained win-x64 helper to a fresh
`artifacts/hotspot-poc/build-*/publish` directory. Its sibling `build.log` records all
build/test output. A failed step stops the flow; an older executable is never launched.

Choose Y after a successful build to open the guided test. Only the test requests
UAC elevation. It finds the active TUN GUID, shows candidate process names/PIDs,
and asks you to identify the core owning the TUN; names alone cannot prove ownership.
It then prompts for the temporary Wi-Fi password and runs for 120 seconds (Q stops).
No PowerShell execution policy is changed. This wrapper has been source-reviewed;
CMD/PowerShell execution, UAC, publishing and Wi-Fi operation require a Windows test.

Keep the entire published folder together when moving to another x64 test PC.
The build machine needs SDK/NuGet; the self-contained output does not need a
separate .NET runtime install. Do not put passwords in CMD arguments or build logs.

From the repository root, with .NET 8 SDK and NuGet access:

```powershell
dotnet run --project tests/HotspotChecks/HotspotChecks.csproj
dotnet build tools/IRSpeedyHotspotHelper/IRSpeedyHotspotHelper.csproj -c Release
dotnet publish tools/IRSpeedyHotspotHelper/IRSpeedyHotspotHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/hotspot-poc
```

Keep `THIRD-PARTY-NOTICES.txt` with the published output. Build from source; do not
ship the local compiler-check DLL as an installer artifact. Do not enable trimming
or NativeAOT without testing COM dynamic dispatch and WinRT marshalling.

Development verification (Linux workspace): all helper sources compiled against
.NET 8 and Microsoft.Windows.SDK.NET.Ref 10.0.19041.56; 30 pure-C# safety checks passed.
Full MSBuild restore was blocked by the workspace's missing NuGet named-mutex support,
so compilation used the SDK Roslyn compiler directly. This does not validate publish,
manifest/UAC behavior, COM calls, WinRT activation or real Wi-Fi hardware. Run the
normal commands above on Windows before testing or distributing the helper.

## Controlled Windows test

Use a spare test PC/adapter, connect IRSPEEDY in full TUN mode, leave existing proxy
sharing off, and open PowerShell **as Administrator**. Confirm no other ICS/hotspot
session is active. Do not change network sharing in Settings while the helper runs.

```powershell
Get-NetAdapter -IncludeHidden | Select-Object Name, InterfaceDescription, InterfaceGuid, Status
Get-Process | Where-Object ProcessName -Match 'sing|core|xray' | Select-Object Id, ProcessName
'{"command":"capability"}' | .\artifacts\hotspot-poc\IRSpeedyHotspotHelper.exe
```

Use the GUID for exactly `irspeedy-tun` and PID of its owning core. A read-only
capability request lists adapters/ICS, but does not prove TUN WinRT capability:
that is checked on start before any networking mutation.

```powershell
.\tools\IRSpeedyHotspotHelper\Test-Hotspot.ps1 `
  -HelperPath .\artifacts\hotspot-poc\IRSpeedyHotspotHelper.exe `
  -TunId 'REPLACE-WITH-TUN-GUID' -CorePid 1234 -Experimental
```

The driver prompts for a password without putting it on the command line and
maintains the heartbeat. Default test duration is 120 seconds; Q ends it early.
No execution-policy bypass is included. If scripts are prohibited, use the approved
local development/signing process rather than changing organization policy.

Acceptance checklist (not yet executed):

1. Phone joins SSID, obtains DHCP address, with its Proxy setting **None** and mobile
   data disabled to prevent a misleading fallback.
2. Phone public IPv4 equals VPN exit IPv4, not ISP address; test IPv6 independently
   (VPN IPv6 or unreachable, never ISP IPv6). Check DNS resolver and DNS leakage.
3. Browser TCP, UDP/QUIC, YouTube playback and a second device work.
4. Stop restores hotspot off and no sharing; unrelated adapter configuration remains.
5. Core exit, VPN disconnect, TUN removal, missing heartbeat and parent pipe EOF
   produce cleanup. Observe packets during transitions, not just a post-stop IP check.
6. Test helper hard kill, sleep/resume and reboot on the spare PC. Verify recovery;
   these paths are explicitly NOT protected by a persistent kill switch yet.
7. Test denied elevation, disabled Wi-Fi, unsupported drivers, access denied / missing
   WinRT capability, two Wi-Fi Direct adapters and conflicting existing ICS.
8. Reconnect core, obtain the new TUN GUID/PID, then explicitly restart the test.

## Protocol

One JSON object per line over inherited stdin/stdout; maximum request length 4096
characters. Keep stdin open. EOF and Ctrl+C request cleanup. Launch from an already
elevated parent; `runas` cannot also redirect standard streams. Production needs a
separately reviewed elevated broker/secured IPC, not a UAC bypass.

Commands: `capability`, `start`, `heartbeat`, `status`, `clients`, `stop`, `recover`.
Start fields: `tunId` (GUID), `corePid` (integer), `ssid`, `password`, `experimental:true`.
Send heartbeat every 2 s after start succeeds. `status` does not renew the lease.
Ready response is emitted first; errors contain stable code/type/HRESULT, no raw
exception text. `active:true` means API/ICS verification only, not verified VPN exit.

## Recovery

### ICS readiness diagnostics

After WinRT reports a successful hotspot start, the helper now polls ICS immediately
and every 500 ms for up to 8 seconds before any manual binding. If the exact TUN
public / Wi-Fi Direct private pair appears, it skips `Bind` entirely. If the private
adapter is still missing, it fails without binding; ambiguity or a foreign shared
connection also aborts. Otherwise it makes at most one guarded Bind attempt, followed
by another up-to-8-second verification window. It never treats a COM failure as success.

The driver prints `ICS observations` on success or error: phase, poll index, adapter
GUIDs, up/down and sharing roles (0 public, 1 private, null disabled). These bounded
snapshots include the state before rollback; they exclude SSID, password, adapter
names, MAC addresses and traffic. Include that line and the Stage/HRESULT error when
reporting the next Windows result. If rollback also fails, the original failure's
stage/type/HRESULT is retained separately from the cleanup HRESULT.

Windscribe's `src/helper/windows/changeics/icsmanager.cpp` was reviewed as a technical
reference: it avoids reconfiguration when the pair is already correct, and otherwise
disables existing sharing before enabling public then private. This helper does NOT
copy that code or disable ICS globally. The new bounded wait and diagnostics are
independent implementation; they test a readiness-race hypothesis, not a confirmed
fix for HRESULT 80040201. No additional Windscribe dependency/license is introduced.

The updated pure-C# suite contains 37 passing checks, including delayed WinRT state,
skipped redundant binding, timeout, delayed bind verification, TUN loss and preservation
of error-time state. Actual Windows retest is still required.

Before mutation, an atomic, flushed, administrator/SYSTEM-only journal is saved in
`%ProgramData%\IRSpeedyHotspotPoC\session.json`. It contains adapter GUIDs and phase,
not password or user identity. Only one helper holds the machine-wide mutex.

After a hard crash, do not start a new session over a journal. With the original TUN
profile still available, use:

```powershell
'{"command":"recover"}' | .\artifacts\hotspot-poc\IRSpeedyHotspotHelper.exe
```

If the TUN profile disappeared or shutdown cannot be confirmed, recovery fails and
retains the journal. Manually turn Mobile Hotspot off in Windows Settings and inspect
the Sharing tab of the recorded adapters in `ncpa.cpl`. Remove only this PoC's sharing.
Once verified off and the helper is no longer running, rename `session.json` to
`session.json.recovered` as an administrator to preserve evidence and permit a new
test. Never blindly delete journals or disable sharing on every connection.

## Before WPF integration

Prove TUN-profile support on target hardware; design/test persistent fail-closed
forwarding protection for IPv4/IPv6; add bounded native-operation supervision and a
service recovery model; restore arbitrary prior ICS/AP state if takeover is desired;
test installer, SmartAssembly packaging and elevation/IPC; then add an opt-in UI.

References:
- https://learn.microsoft.com/en-us/uwp/api/windows.networking.networkoperators.networkoperatortetheringmanager
- https://learn.microsoft.com/en-us/windows/win32/api/netcon/nf-netcon-inetsharingconfiguration-enablesharing
- https://github.com/poti-san/PotisanWindowsComLibs
