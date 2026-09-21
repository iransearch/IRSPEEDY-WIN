# IRSPEEDY direct sharing integration

The WPF sharing window has two independent tabs. Existing HTTP/SOCKS sharing is unchanged. Direct sharing uses the tested Wi-Fi Direct Legacy AP + ICS helper. It requires the current service's connected effective TUN mode, exactly one up `irspeedy-tun`, and the registered SGuard RPC process identity. Game Mode follows the VPN's existing selective routing; it does not imply every destination uses a VPN exit.

## Build and deployment

Run `Build-IRSpeedy-Hotspot.cmd` at the repository root on Windows with .NET 8 SDK and .NET Framework 4.8 developer pack. It runs the helper, coordinator and payload checks, enables the existing Costura single-file packaging, builds WPF, and checks that both architecture payloads reached the EXE. Ordinary Windows builds also embed the helpers, so the existing builder on `agent/core-startup-diagnostics-fix` needs no extra copy step. `-p:IncludeDirectHotspot=false` remains a development-only opt-out; the UI reports that the feature is absent from that build.

`tools/HotspotPayload.targets` publishes complete self-contained `win-x64` and `win-x86` helpers below the intermediate `obj` directory, archives each publish output (including native runtime, WinRT dependencies and notices), and embeds both ZIPs with fixed resource names before WPF compilation. The helper remains a separate .NET 8 process at runtime; it is carried as opaque resource data inside the .NET Framework EXE. Existing Costura/SmartAssembly packaging can process the host as before. No `Hotspot` directory must be distributed beside the final executable, and the Dotfuscator target no longer copies one. Old build directories may still contain obsolete loose helpers; the application never loads those files.

When direct sharing starts, the host selects the payload by OS bitness and extracts it under `%ProgramData%/IRSpeedyHotspotRuntime/<payload-sha256>`. There is no extraction on the login/startup path or UI availability check. The cache root is restricted to Administrators and SYSTEM, with owner, ACL and reparse-point checks. A cross-process file lock serializes cache updates. Every helper launch checks cached file lengths and SHA-256 hashes against the embedded payload; missing, altered or extra files cause a complete verified replacement. Payload versions use different directories, so an application update does not overwrite the previous helper runtime. Extraction failure is reported in the UI and never falls back to an adjacent helper. The recovery journal remains in its existing location and lifecycle semantics are unchanged.

After the existing final obfuscation/packaging step, check the actual deliverable on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-HotspotPayload.ps1 -AssemblyPath path/to/final/IRSpeedyVPN.exe
```

This verifies both embedded ZIPs, their required runtime files/notices and helper PE architectures without running the app or changing sharing. Also copy only the final EXE to a fresh directory (no `Hotspot` folder), then complete the runtime acceptance checks below. Preserve the existing host configuration/runtime handling of the chosen single-file builder. The helper requires Windows 10 build 19041 or later; older systems retain proxy sharing. Both app and helper require Administrator. Communication still uses redirected inherited stdin/stdout.

## Lifecycle

`PauseSharingBeforeCoreRestart` invalidates TUN eligibility and synchronously drains an in-progress start/stop through the coordinator before core mutations. It runs at Connect, RunV2ray, DisconnectInternal (including silent), TryReconnect, TryStartCoreWithConfig, TryStopCore, and SafeStopCore. A failed cleanup blocks a new config/start; explicit VPN disconnection can still stop the core and leaves a visible cleanup error with a retained helper for retry.

Silent/automatic reconnect preserves the user's sharing intent. Only after a successful core start does the coordinator capture the new TUN GUID and process start time, start a fresh helper session, and verify the pair again. Explicit disconnect or the sharing Stop button cancels the intent. Credentials are hidden during pause/failure. The watchdog remains active independently of the popup/UI dispatcher. Core/TUN loss observed by the watchdog before the lifecycle hook preserves intent but waits for a new successful core-start signal. Other watchdog failures and cleanup failures cancel intent; there is no health-driven restart loop. Closing the popup keeps the session; app exit stops it. Hard app failure closes inherited pipes and the helper performs its finally cleanup; a durable journal supports recovery if that cannot finish.

Protocol v2 adds `requestId`, integrated credentials, and `coreStartedUtcTicks`; the standalone test still uses `0000000000`. Integrated passwords are uniformly generated as ten ASCII digits, stored with current-user DPAPI, and may be replaced with exactly ten ASCII digits while off. SSID and password persist across reconnects; neither is written to diagnostic logs. Public IPC response logs must not be dumped because a successful start returns the password.

## Validation boundary

Portable coordinator checks cover silent reconnect with a new GUID, explicit disconnect cancellation, owner isolation, cleanup failure/retry, watchdog stop, post-start TUN loss, password validation, and a concurrent start/pause barrier. Existing helper safety checks remain applicable. Linux compilation and simulated tests do not verify Windows hardware, WPF rendering, installer deployment, or zero packet leakage.

Windows acceptance: build and validate the final EXE, copy it alone to a fresh folder, then test direct TUN activation, disabled activation in Proxy/disconnected mode, unchanged HTTP/SOCKS sharing, phone traffic and VPN exit IP, stable credentials after silent reconnect/server changes, explicit Stop/disconnect, closing/reopening popup, application exit, feature-omitted/extraction-failed/old-Windows messages, and forced loss of tunnel/helper with cleanup recovery. Measure real traffic during failure separately before describing this as a verified kill switch.

Payload regression checks: `dotnet run --project tests/HotspotPayloadChecks/HotspotPayloadChecks.csproj -c Release` covers first extraction, nested dependencies, cache reuse, missing native files, same-size corruption, extra files, version/architecture isolation, unsafe and duplicate ZIP paths, incomplete payloads, and staging cleanup. These portable checks do not validate Windows ACLs, WPF resource target ordering or final obfuscation.
