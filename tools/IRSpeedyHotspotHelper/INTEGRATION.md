# IRSPEEDY direct sharing integration

The WPF sharing window has two independent tabs. Existing HTTP/SOCKS sharing is unchanged. Direct sharing uses the tested Wi-Fi Direct Legacy AP + ICS helper. It requires the current service's connected effective TUN mode, exactly one up `irspeedy-tun`, and the registered SGuard RPC process identity. Game Mode follows the VPN's existing selective routing; it does not imply every destination uses a VPN exit.

## Build and deployment

Run `Build-IRSpeedy-Hotspot.cmd` at the repository root on Windows with .NET 8 SDK and .NET Framework 4.8 developer pack. It runs both hotspot test suites, builds WPF, and publishes self-contained `win-x64` and `win-x86` helpers into the application output's `Hotspot` directory. Ordinary Windows builds also publish the helpers; `-p:IncludeDirectHotspot=false` is available for development-only builds without this feature. The feature shows an installation message if its helper is missing.

Deploy the complete output directory. Keep `Hotspot/win-x64` and `Hotspot/win-x86` beside the final EXE, including SmartAssembly output; do not merge or obfuscate the helper into the .NET Framework executable. The existing Dotfuscator target copies the helper folders into its output. Helper requires Windows 10 build 19041 or later; older systems retain proxy sharing. Both app and helper already require Administrator. Communication uses redirected inherited stdin/stdout; no public named pipe or new elevation prompt is introduced.

## Lifecycle

`PauseSharingBeforeCoreRestart` invalidates TUN eligibility and synchronously drains an in-progress start/stop through the coordinator before core mutations. It runs at Connect, RunV2ray, DisconnectInternal (including silent), TryReconnect, TryStartCoreWithConfig, TryStopCore, and SafeStopCore. A failed cleanup blocks a new config/start; explicit VPN disconnection can still stop the core and leaves a visible cleanup error with a retained helper for retry.

Silent/automatic reconnect preserves the user's sharing intent. Only after a successful core start does the coordinator capture the new TUN GUID and process start time, start a fresh helper session, and verify the pair again. Explicit disconnect or the sharing Stop button cancels the intent. Credentials are hidden during pause/failure. The watchdog remains active independently of the popup/UI dispatcher. Core/TUN loss observed by the watchdog before the lifecycle hook preserves intent but waits for a new successful core-start signal. Other watchdog failures and cleanup failures cancel intent; there is no health-driven restart loop. Closing the popup keeps the session; app exit stops it. Hard app failure closes inherited pipes and the helper performs its finally cleanup; a durable journal supports recovery if that cannot finish.

Protocol v2 adds `requestId`, integrated credentials, and `coreStartedUtcTicks`; the standalone test still uses `0000000000`. Integrated passwords are uniformly generated as ten ASCII digits, stored with current-user DPAPI, and may be replaced with exactly ten ASCII digits while off. SSID and password persist across reconnects; neither is written to diagnostic logs. Public IPC response logs must not be dumped because a successful start returns the password.

## Validation boundary

Portable coordinator checks cover silent reconnect with a new GUID, explicit disconnect cancellation, owner isolation, cleanup failure/retry, watchdog stop, post-start TUN loss, password validation, and a concurrent start/pause barrier. Existing helper safety checks remain applicable. Linux compilation and simulated tests do not verify Windows hardware, WPF rendering, installer deployment, or zero packet leakage.

Windows acceptance: build the complete output; test direct TUN activation, disabled activation in Proxy/disconnected mode, unchanged HTTP/SOCKS sharing, phone traffic and VPN exit IP, stable credentials after silent reconnect/server changes, explicit Stop/disconnect, closing/reopening popup, application exit, missing-helper/old-Windows messages, and forced loss of tunnel/helper with cleanup recovery. Measure real traffic during failure separately before describing this as a verified kill switch.
