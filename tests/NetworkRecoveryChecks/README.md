Run `dotnet run --project tests/NetworkRecoveryChecks`.

The executable uses the production recovery state machine and compiles its
TunnelPlusService/TLS integration against application field doubles. It checks
offline suspension, worker deduplication, rejection of stale successes, retry
until external confirmation, and immediate invalidation on disconnect. It does
not exercise Windows networking, WPF rendering or a real VPN.

`python tests/NetworkRecoveryChecks/net48_compile.py /path/to/dotnet /path/to/net48/references`
also compiles the production monitor setup and notification handler against
the actual `IVPNService` interface and .NET Framework 4.8/WPF references.
This catches UI integration errors such as accessing a connection property
that only exists on `TunnelPlusService` through the service interface.

Fast-return checks also cover four consecutive direct failures, recovery on the
next fast tick without OS adapter events, suppression of extra notifications,
protection against an ICMP target failing while external TLS still works, and
production RPC ordering/cancellation using a local TCP server. The main-pool
return command is acknowledged once before recovery proof is queried. Recovery
proof matches a verified TLS socket's exact loopback source port and fixed host
to an active `proxy` chain, independently of stale URL-test rankings. Tests reject
direct/AI-only, closed, unrelated, non-loopback and missing-source telemetry.

On Windows, `powershell.exe -NoProfile -STA -File tests/NetworkRecoveryChecks/run-layout-wpf.ps1`
measures the production page's card/button positions while the banner and badge
change. This checks real WPF layout; the Linux checks cannot render WPF.

Core regression checks live in Throne-G's `internal/autoselector` and
`internal/xray`: local SOCKS success cannot erase external failure; only failing
members are reset once after network return; they rejoin after external proof;
paused networking does not probe; a blackholed Hysteria handshake is cancelled
and the next request receives a fresh client.
