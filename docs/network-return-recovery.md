# Prompt main-pool recovery after a WAN outage

An ISP connection can return without Windows reporting an adapter change. The
previous recovery observer queried TLS/status but never asked the main selector
to sweep immediately, leaving it behind the 900-second normal interval. Failed
samples from the outage could also keep a working member outside the qualified set.

While connected, or while the internet is known offline, the direct IP watcher
checks every five seconds. Four consecutive failed fast pings are needed to
declare an outage. Before suspending a connected VPN it checks external TLS;
failure of the fixed ICMP endpoint alone cannot stop a working path. The existing
ten-minute/four-ping notifications remain separate from recovery signals.

The application sends ordered `network-down` / `network-return` actions for the
explicit `proxy` tag. Each dedicated RPC has a 750 ms deadline, uses the active
connection generation and is cancelled on disconnect. Return is acknowledged
once before the external TLS and fresh-qualified-main-pool proof are checked.
The observer does not start a sweep on every confirmation retry.

The matching Throne-G core cancels old probes and interval jitter, suspends checks
while offline, and schedules a coalesced full sweep immediately on return. The
first successful external probe publishes its member before slow peers finish.
Only failure samples within the detected outage (with the existing 45-second
detection margin) are discarded, once per verified member in the recovery window.
Earlier server failures and normal qualification limits remain authoritative.

The Core process, TUN, DNS configuration and established healthy flows are retained.
AI remains a separate selector with its existing policy. Package the matching new
ThroneCore binary as `V-Guard/SGuard64.exe` inside the existing single-file payload;
updating only the application cannot provide the new Core actions.

Diagnostics retain the online transition, `pool-network-availability` RPC result,
and `network-return-path` proof so the next report can measure detection and
confirmation separately.

Validation: `tests/NetworkRecoveryChecks`, its net48 compilation check and the
manual connection-test checks; Throne-G's race-enabled selector/Xray/box tests
and a modern Windows x64 Core cross-build. Actual Windows ISP/Wi-Fi interruption
and full single-file packaging need Windows runtime verification.
