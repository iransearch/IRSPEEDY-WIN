# Direct sharing availability

- Run one read-only capability check after each successful manual/password login or automatic cached login. Credential/session renewal and sharing-window opening do not repeat it. The result is kept in memory until the next login; changing a USB Wi-Fi adapter/driver requires a new login to refresh it.
- Windows older than build 19041 (Windows 10 2004), including Windows 7/8/8.1, and builds without the hotspot payload remain unavailable. No helper or modern Wi-Fi APIs are loaded on those systems.
- On supported Windows, run the system `netsh.exe wlan show wirelesscapabilities` on a worker with a five-second process deadline and bounded pipe drain. This command reports supported wireless features: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-wlan
- Require a positive Wi-Fi Direct GO (group owner/access point) capability on at least one adapter. Do not use legacy Hosted Network, Miracast, an adapter's friendly name, or a successful WFD service-handle open as proof of GO support. Never start a publisher, turn on Wi-Fi, change ICS or recover a journal merely to inspect capability.
- Recognized negative GO results disable the tab with the hardware/driver reason. An absent/malformed report, unrecognized translation or value, service/access failure, or timeout leaves an explicit **unconfirmed** result and the tab disabled. It is not cached as permanent hardware incompatibility. Positive and negative results are both re-evaluated on the next login. The parser accepts invariant GO labels with known localized result words; unrecognized localized labels remain unconfirmed.
- Initially the direct tab is disabled. The existing window UI timer reads only the cached result and updates the tab, lock and reason. Disabled buttons reject mouse/keyboard input; the selection and start handlers also guard programmatic/queued requests. The proxy tab stays available under its existing connection requirements.
- Preserve Stop/recovery access for a live/paused/starting session or unconfirmed cleanup. Capability support is not a guarantee that radio state, TUN, ICS or policy permit startup; existing startup guards continue to apply.
- No packaging or helper changes; the existing single-file builder embeds the new .NET Framework code normally.

## Verification

`dotnet run --project tests/DirectSharingAvailabilityChecks`

Compiles the production capability/session code, process runner and UI application method (with tiny WPF substitutes). Exercises OS/payload gates, positive/negative/unknown and multiple-adapter reports, localization, timeout handling, one check per login, stale-login isolation, tab disabling, fallback, and retained Stop access.

Windows acceptance: test one Windows 7/8 installation, a supported Windows installation with a GO-capable adapter, and one without GO support. Open/reopen sharing while watching process creation: one netsh capability invocation after login, none from the dialog. Verify disabled selection with mouse and keyboard, proxy availability, supported direct startup, and unknown-message behavior with WLAN AutoConfig stopped. Full rendering and real driver reports require Windows; the portable tests do not claim that coverage.

- Login queues the single capability check at dispatcher ContextIdle after showing the server list. The callback only schedules the existing worker; process creation, encoding/driver work and all bounded waits stay off the dispatcher. This reduces startup contention but does not establish Wi-Fi checking as the cause of a reported login stall.
- Sharing tab changes are presentation-only (180 ms eased fade/translation, respecting disabled system animations). Adapter/listener snapshots run asynchronously with one request at a time; stale results are discarded after toggles or service changes.
