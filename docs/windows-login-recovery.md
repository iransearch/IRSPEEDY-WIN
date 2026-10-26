# Windows login recovery (1.4.39)

Baseline: `978aa7bb4a4f126711208cce5c81ee4635624b3c` (1.4.38).
Login2 uses a fresh LoginNetworkContext for each call, including background
account/list refresh. The four API endpoints retain their sequential order,
last-good preference, 30-second shared network deadline and 8-second endpoint
limit. Loading presentation, VPN DNS racing, Shield, routing and Core are unchanged.

## Request and replay policy

The first request uses bundled curl and system DNS. Its connection timeout is
at most 3 seconds; after a connection succeeds, it can use the remaining endpoint
budget to receive a response. A pre-send failure can enter one recovery for the
same hostname before advancing to the next endpoint. Google DoH uses
`https://8.8.8.8/resolve?type=A&name=...`, at most 2 seconds, and public IPv4
answers are cached for 5 minutes. At least one second is reserved for the
credential request when assigning the lookup budget. Every operation consumes
the same endpoint and login deadlines; cancellation is checked before lookups
and POST attempts and aborts native curl/.NET I/O.

curl `-w` records HTTP status, time_connect, time_appconnect,
time_pretransfer, size_upload and num_redirects even on nonzero exits. No
rounding to milliseconds is used for replay eligibility. Missing/invalid
metrics fail closed. Eligible errors are 6, 7, 35, 60 and 28 only before TCP
connect. All require zero upload, zero completed TLS/pretransfer and no HTTP
response or redirect. Errors 35/60 may have completed TCP, but must not have
completed TLS or sent the request. Errors 52/55/56, timeouts after TCP connect,
completed TLS, uploads, and unknown stages prohibit replay on every API host.
HTTP 500 and invalid/empty application responses retain their previous
explicit endpoint failover policy; none triggers DoH. HTTP 401/406 are final
even without a JSON body/message (1.4.38 could convert that case to null).

All accepted A records are passed in one `--resolve host:443:IP1,IP2` argument
(the original URI port is used when explicit). URL, Host, SNI and certificate
checks stay intact. Login does not follow redirects, because metrics from a
later redirect connection cannot prove the first POST was unsent. There is
no insecure TLS option or certificate-validation override. The .NET transport
is used only when curl cannot launch, never after a curl network/TLS failure.

A failing cached answer set is evicted. If its failure is still safe to replay,
the same recovery may make one fresh network lookup and one final request.
Fresh-answer failure never recurses. If a POST may have been sent, cache eviction
does not authorize another lookup/request. Only answer sets are shared across
logins; recovery attempt state and cancellation are per login. Failed queries
do not create a cross-login cooldown. Existing non-login transports are unchanged.

`[LoginRecovery]` events contain random login ID, hostname, fixed stage/reason,
exit code, HTTP status and connection/upload metrics. They contain no body,
headers, username, password or raw stderr. Final errors retain sanitized initial
and recovery failures. These records pass the existing LogPolicy filter.

## Offline validation

Run `tests/LoginRecoveryChecks/run.py` with the .NET host, Roslyn `csc.dll`,
.NET 8 reference directory and optional .NET 4.8 reference directory:

```text
python tests/LoginRecoveryChecks/run.py <dotnet> <csc.dll> <net8-refs> [net48-refs]
```

The Linux integration harness executes production curl argument generation,
process execution/cancellation, telemetry parser, recovery state machine, cache,
public-IP filtering and HTTP fallback code. HTTPS loopback servers use a local
test CA and valid/wrong-host certificates; native curl verifies both. A local
fake DoH server supplies public-address fixtures. Test transport adapters map
those addresses to loopback and route the simulated wrong system-DNS answer to
the wrong-host certificate server. This mapping exists only in tests. No public
DNS/API endpoint, credentials or machine trust-store modification is used.

Coverage includes healthy DNS, wrong nonempty DNS, all A records, 401/406/500,
drop and timeout after POST, bounded recovery and endpoint handoff, independent
login state, cancellation before/during curl/DoH, expired budgets, bad cached
answers, private addresses, wrong-host certificates after recovery, unsafe or
missing metrics, credential-free logging and the actual log filter. The full
production controller additionally runs with a fake REST boundary to verify
empty-body 401/406 and per-call state; its real REST/transport wiring is compiled
against .NET 4.8. Existing password re-login and loading-stage checks also run.
This does not execute the protected Windows single-file application or the
bundled Windows curl/PE selection on Linux; a packaged Windows smoke test remains.
