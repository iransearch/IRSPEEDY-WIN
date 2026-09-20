# Experimental Windows VPN hotspot helper

Standalone IRSPEEDY-WIN PoC, not integrated into the WPF app or installer.
Windows 10 build 19041+ / Windows 11, .NET 8, elevated desktop process.
This is not a verified kill switch or a production-ready sharing feature.

## Startup v2

The reported COM HRESULT 80040201 occurred at ics.enable-public after starting
WinRT on the TUN profile; waiting eight seconds did not resolve it.
This update uses the startup ordering reviewed in nestchao's console_bridge.py:

1. Require an active irspeedy-tun selected by GUID and no existing ICS/hotspot.
2. Prepare WinRT on the current Internet connection profile; persist its GUID
   with the TUN GUID before any network mutation.
3. Start with a newly generated SSID and 128-bit random Wi-Fi password. Old
   credentials are never reused, preventing remembered clients from joining.
4. Wait up to eight seconds for the private adapter and ICS state.
5. Transfer only the complete startup pair to TUN/public and Wi-Fi Direct/private.
   Skip writes if already correct. Reject foreign sharing and incomplete pairs.
6. Verify the exact TUN/private pair before returning the credentials.

This remains a fix hypothesis requiring Windows retesting: EnableSharing(0)
is still used. No COM failure is treated as success. No second VPN core or
tun2socks is added; SGuard continues to own the existing TUN.
The ordering is independently implemented; no nestchao source was copied.
Keep THIRD-PARTY-NOTICES.txt for the existing ICS interface attribution.

## Build and run

Download the complete agent/windows-hotspot-poc branch and run Build-Hotspot.cmd
beside IRSpeedyVPN.sln. Install .NET 8 SDK x64 first. The CMD runs checks and
publishes into a fresh artifacts/hotspot-poc/build-*/publish folder; its sibling
build.log records the build. Old binaries are never reused.

Connect IRSPEEDY in full TUN mode. Select the CURRENT SGuard64 PID and confirm
ownership. There is no password prompt in v2. After verified binding the driver
shows startup mode default-profile-then-tun-v2, the SSID and temporary password.
Do not share screenshots or transcripts containing that password.
The guided test requests UAC and runs for 120 seconds; Q stops it.
Keep the entire published folder together when copying to another test machine.

Manual commands from the repository root:

```powershell
dotnet run --project tests/HotspotChecks/HotspotChecks.csproj
dotnet publish tools/IRSpeedyHotspotHelper/IRSpeedyHotspotHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/hotspot-poc
```

No global sharing reset, service restart, driver installation, firewall reset
or execution-policy change is performed.

## Windows acceptance test

1. Phone joins with proxy None and mobile data off; verify DHCP.
2. Phone public IPv4 must match VPN exit. Check IPv6 and DNS independently.
3. Test YouTube, UDP/QUIC and a second device.
4. Test VPN disconnect, core exit and closing the driver; verify cleanup.
5. Test sleep/resume and hard-crash recovery on a spare machine.

API success proves adapter roles only, not end-to-end VPN connectivity.
Use full TUN with direct bypass rules disabled. The 500 ms health watchdog and
10-second heartbeat lease are not persistent packet-level protection. Random
startup credentials reduce accidental early connections but do not guarantee
zero leakage during shutdown, crashes, OS changes or native API hangs.
Avoid sensitive test traffic. Production integration still needs packet-level
protection and actual Windows acceptance tests.

## Diagnostics and recovery

Errors include stage/type/HRESULT and bounded adapter GUID/up/role observations
before cleanup. Error records exclude passwords, SSIDs, MAC addresses and traffic.
The successful start response deliberately includes credentials for the local
driver; never collect that response in diagnostic logs.

The atomic, flushed administrator/SYSTEM-only journal is stored at
%ProgramData%\IRSpeedyHotspotPoC\session.json. Version 2 records BootstrapId.
The new helper also recovers version 1 journals; use the newest helper.
Recovery opens the recorded startup profile, never a newly chosen default.
It stops the hotspot and removes only recorded TUN/private/startup sharing.
If cleanup cannot be verified, the journal remains and new starts are refused.

From elevated PowerShell, using the actual published path:

```powershell
'{"command":"recover"}' | .\artifacts\hotspot-poc\IRSpeedyHotspotHelper.exe
```

If the recorded profile disappeared, manually stop Mobile Hotspot and inspect
the recorded adapters' Sharing tabs in ncpa.cpl. Once restored and the helper
is stopped, preserve evidence by renaming session.json to session.json.recovered.
Never blindly delete journals or disable all ICS connections.
Credentials remain configured in Windows after stopping and rotate next test.

## Protocol and verification

Newline-delimited JSON on stdin/stdout, maximum 4096 characters.
Commands: capability, start, heartbeat, status, clients, stop, recover.
Start requires tunId, corePid, experimental:true. Credentials are generated
inside the helper and returned only after ICS verification. Heartbeat every
two seconds after start; EOF triggers cleanup. Capability is read-only.

Linux verification: helper compiled against .NET 8 and Windows SDK
10.0.19041.56 using Roslyn directly; 42 pure-C# checks passed, including startup
pair transfer, unrelated sharing refusal, failure rollback and crash recovery.
Tests simulate the backend; Windows COM/WinRT, UAC, publish and Wi-Fi operation
remain unverified here. Run the normal build/test commands on Windows.

Reference:
https://github.com/nestchao/Hotspot-Bypass-VPN-Unlimited-Hotspot/blob/af54ca57a17cd86a9ae7d3d7c22dbee08d65f9cb/laptop_proxy/console_bridge.py
