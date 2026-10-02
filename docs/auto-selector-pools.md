# Smart and country Auto Selector pools (1.4.6.1)

Smart/country connections that supply `_smartFastUrls` now use independent
sing-box `auto-selector` groups. Single-server connections and list URL tests
retain their existing builders.

Each accepted main or AI Xray member has an authenticated SOCKS5 inbound on its
own loopback port. An exact inbound-tag rule sends it to that one outbound.
The Xray sidecar has no inner balancer or burst observatory. Its transport
builders, Hysteria2 adapter and Core `ForceIP`/`dns-direct` integration are reused.

The sing-box `proxy` group selects only `smart-proxy-*` bridges. When AI is
enabled, `ai-proxy` selects only `ai-proxy-*` bridges and AI domains route there.
An empty/rejected AI pool rejects those domains; it never falls into main or
direct. AI off installs neither the group nor the domain policy. Shield blocking,
sniff/DNS handling and Core bypasses precede AI routing; geo/default routing
follows it. Existing DNS, VOD, chain, sharing and Game Mode configuration remains
in the base shell, including its original `route.final`.

Both groups use:

| Field | Value |
|---|---|
| `expected` / `active_size` | min(3, members) / min(8, members) |
| `interval` / `bench_interval` | 60s / 300s |
| `watch_interval` | 15s |
| `sampling` | 10 recent results |
| `timeout` / `concurrency` | 5s / 4 |
| `dial_retries` / `fail_tolerance` | 2 / 0.2 |
| `max_rtt` | main 3s, AI 5s |
| `balance` / `balance_mode` | true / connection |
| `interrupt_exist_connections` | false |

Native Auto Selector probes still use the existing gstatic generate_204 URL.
They are distinct from the connection-page TCP/TLS tests. `connectivity_url` is
unset so a blocked fixed direct URL cannot declare the physical internet down;
the Core's OS/error heuristics and member recovery probes remain active. With
small pools, their quorum behavior and real network recovery must be verified
on Windows. Failed dials may retry another member within the same group.

Bridge ports are owned by the connection plan. Partial allocation failures,
cancellation, failed start, disconnect and rebuild return them to the existing
port allocator; teardown is idempotent. Each attempted start records
`auto-selector-config-apply` with group sizes/policy and no credentials.

Required Core: the Throne fork's `auto-selector`, with the sing-box dependency
`f154bec036c9` present in Core commit `4facf147`; prefer branch head `c0fa2071`.
Use that Core in the separately supplied `Resources/Files.zip`. No Core source
or archive is modified here. An incompatible Core should fail Start rather than
silently reverting to the old pool.

Validation: `dotnet run --project tests/HysteriaXrayChecks` executes production
builders, including 22 Auto Selector checks for bridge mapping, independent
membership, AI policy, proxy/TUN shells, DNS/Shield/VOD/chain retention and port
cleanup. The harness isolates WPF/settings/parser dependencies; it does not run
the packaged Core. Production builders also compile against .NET Framework 4.8.

On Windows, verify AI on/off/empty pools, shared/proxy/TUN/Game modes, cancellation
and reconnect, then disconnect Wi-Fi briefly and restore it. Confirm new flows
recover through a healthy member without manually disconnecting and that AI
traffic never uses a main member. Open flows are not forcibly interrupted merely
because selection changes. Repeat the sustained two-hour scenario from the log.
