Run `dotnet run --project tests/NetworkRecoveryChecks`.

The executable uses the production recovery state machine and compiles its
TunnelPlusService/TLS integration against application field doubles. It checks
offline suspension, worker deduplication, rejection of stale successes, retry
until external confirmation, and immediate invalidation on disconnect. It does
not exercise Windows networking, WPF rendering or a real VPN.

Fast-return checks also cover four consecutive direct failures, recovery on the
next fast tick without OS adapter events, suppression of extra notifications,
protection against an ICMP target failing while external TLS still works, and
production RPC ordering/cancellation using a local TCP server. The main-pool
return command is acknowledged once before recovery proof is queried.

Core regression checks live in Throne-G's `internal/autoselector` and
`internal/xray`: local SOCKS success cannot erase external failure; only failing
members are reset once after network return; they rejoin after external proof;
paused networking does not probe; a blackholed Hysteria handshake is cancelled
and the next request receives a fresh client.
