# Connection diagnostics: network-core-v1

This is an observation-only change on the pre-redesign UI. No timeout,
concurrency, URL retry, pool membership, reconnect or success/failure rule changes.
Collect the single existing `log.txt` from launch through the failed test and
one connection attempt. Find `[ConnectionDiagnostic] stage=session` with
`schema=network-core-v1`, `appVersion`, `appMvid`, `appPid` and `session` to
identify the diagnostic build/run. The build still uses the existing single-file
packaging; there is no added package or redistributable.

## What the next report can distinguish

- `selectedMode`: the saved TUN/Proxy preference at the observation time.
- `gameMode`: whether Game Mode is selected. `config-mode effectiveMode` records
  the actual configuration choice after the Game Mode TUN override.
- `activeMode` and `appliedMode`: the connected service's mode versus this
  particular service instance's mode. Idle/unknown is explicit. A proxy URL test
  is not used as evidence of the app's mode.
- `service`, `connection`, `countryId`, `testId`, `requestId`, `outboundTag`,
  `memberId`: correlate group/member/request observations without server URLs.
  Member fingerprints stay stable only during this app run, even if list order changes.
- `probe-rpc-begin/end`: primary/alternate phase, core-RPC test route, configured
  timeout/concurrency, Xray flag, config/URL fingerprints, batch wall time,
  cancellation flags, and network event counters before/after the request.
  Existing `latencyMs` is member latency. `requestElapsedMs`/`batchElapsedMs`
  measure the whole request, NOT an individual member's duration.
- `core-spawn/ready/reuse/exit`, `core-stop-rpc-*`, `tests-cancel/stop/drain`,
  `process-kill-request`, `reconnect-request`, `core-rpc-timeout-retry` separate
  client-requested stops/retries from observed process exits. Exit events record
  decimal/hex code, suppression/cancel/reconnect state before early returns.
  PID -1 with `owned=False` means this instance reused an endpoint without a
  process handle; it must not be interpreted as proof the process was absent.
- Core stdout/stderr forwards only a small allowlisted category and salted
  message fingerprint, max 10 signals per 5 seconds per service. The original
  core text/config is not forwarded by this instrumentation.

## Network observation

A process-wide monitor subscribes to NetworkAddressChanged and
NetworkAvailabilityChanged. Their callbacks increment counters and request a
snapshot; they do not enumerate adapters or do file I/O. A background timer
samples every two seconds and emits on change or a requested error snapshot.
Each context gives monotonic time, net event sequence/revision and snapshot age.

Snapshots contain interface indices/type/operational state; adapter IDs,
unicast addresses, gateway and DNS lists are per-session salted HMAC IDs.
Read-only GetIpForwardTable records IPv4 default and split-default (/1) route
interface indices and route metrics, with hashed next hops. No WMI, active
reachability probes, DNS lookups or route modifications are added.

Limitations are explicit: the native IPv6 route table and effective combined
interface/route metric selection are not sampled. IPv6 address/gateway changes
can still affect the adapter fingerprints. Very short route changes between
samples may be missed. A `network changed` core error with no observed Windows
event is a useful lead, not proof of a core bug; no diagnosis is guaranteed by
logging alone. Snapshot/API failure is recorded without failing the VPN/test.
The monitor is process-wide so changes during idle/connected periods remain
correlatable; it is disposed on process exit.

## Checks and next Windows run

Run `dotnet run --project tests/ConnectionDiagnosticChecks` (.NET 8) for portable
checks of selected/applied mode, build/session IDs, salted member correlation,
core-output privacy and isolation from a failing log sink. The fixture supplies
legacy fields; it does not run a VPN or simulate a Windows route change.

The added diagnostics/helper compile against real net48 reference assemblies
with C# 7.3 using a fixture for the existing service fields. The complete modified
service parses with Roslyn. Full Windows build/packaging and runtime validation
are still required. On Windows, run Germany 2 tests in the chosen mode, connect,
then repeat after a real network switch and disconnect. Check that session,
probe phases, network snapshots, mode and core lifecycle events are all present.
Do not infer that `network changed` is fixed by this diagnostic-only commit.
