Run `dotnet run --project tests/NetworkRecoveryChecks`.

The executable uses the production recovery state machine and compiles its
TunnelPlusService/TLS integration against application field doubles. It checks
offline suspension, worker deduplication, rejection of stale successes, retry
until external confirmation, and immediate invalidation on disconnect. It does
not exercise Windows networking, WPF rendering or a real VPN.

Core regression checks live in Throne-G's `internal/autoselector` and
`internal/xray`: local SOCKS success cannot erase external failure; only failing
members are reset once after network return; they rejoin after external proof;
paused networking does not probe; a blackholed Hysteria handshake is cancelled
and the next request receives a fresh client.
