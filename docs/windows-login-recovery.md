# Windows login recovery (1.4.40)

Baseline: `feddf9c2b939eb0602e670d59b3906aa23448608` (1.4.39).
The baseline matched the reported replay, managed-fallback, launch-timeout and
argument-construction issues. `_curlUnavailable` is static. Both Login2 callers
already caught Exception; the foreground wrapper still needed shutdown guards
around its initial dispatcher await and presentation cleanup. The existing
generic UI message is `ارتباط با سرور برقرار نیست`, which is retained.

The four API endpoints retain their sequential order, last-good preference,
30-second shared network deadline and 8-second endpoint limit. Normal loading
keeps its four-second minimum and existing stages/appearance. Application exit
cancels network/presentation waits and skips work on the closing window. VPN
DNS racing, Shield, routing and Core are unchanged.

## Request and replay policy

The first attempt uses bundled curl and system DNS, with a connection timeout
of at most 3 seconds inside the endpoint deadline. curl `-w` records HTTP status,
time_connect, time_appconnect, time_pretransfer, size_upload and num_redirects
on failures as well as successes. Parsing takes each marker's last occurrence,
so identical text in the response body cannot override the final trailer.

For HTTPS, possible sending is determined only by:

```text
!MetricsKnown || UploadedBytes > 0 || HttpCode > 0 || Redirects != 0
    || TlsSeconds > 0 || PretransferSeconds > 0
```

Missing/invalid metrics fail closed, including positive submillisecond values.
A completed TCP connection alone does not prove a POST reached an HTTPS server.
Known-zero TLS/pretransfer/upload/HTTP/redirect indicators permit next-host
failover on handshake stalls/resets, including exit 28/52/55/56. Same-host DoH
recovery remains narrower: exit 6/7/35/60, or exit 28 only when time_connect is
zero; all must be proven unsent. Plain HTTP retains the previous conservative
exit 52/55/56 and post-TCP exit 28 guards and never uses same-host DoH recovery.

Every HTTP response bypasses DoH. HTTP 401/406 are final; explicit HTTP 500 and
invalid/empty application responses keep their existing endpoint policy. After
a transport failure with possible sending, no API host may replay the POST.

At most one recovery is allowed per hostname per Login2 call. All public A
records are passed together through `--resolve host:443:IP1,IP2` (or the explicit
URI port). URL, Host, SNI and certificate checks stay intact. Login redirects
are explicitly disabled through BuildArgs; other callers keep their default.
Neither transport disables TLS validation. A static credential-attempt gate
also serializes foreground/background login curl and managed attempts, charging
queue time to the same deadline. DNS GETs do not take that gate.

A started curl killed by its outer process deadline has unknown sending state:
its TimeoutException stops endpoint replay, without disabling curl globally.
Pre-launch TimeoutException propagates without changing availability. Only
exhausting absent, invalid-PE or unlaunchable candidates sets `_curlUnavailable`.

## Managed fallback

.NET is used only if no curl process could launch. For HTTPS, a direct
NameResolutionFailure or ConnectFailure before any body write permits failover.
TrustFailure/SecureChannelFailure is unsent only when GetRequestStream failed
before a write. After Write begins, with a response, on Timeout, SendFailure,
ReceiveFailure or ConnectionClosed, or without reliable phase evidence, it stays
conservative. In particular, a TLS exception with unknown phase is not sufficient
proof: buffering/renegotiation cannot be excluded. HTTP stays conservative.
Managed login redirects are disabled; non-login managed requests are unchanged.

## Parallel DoH and speculative prefetch

The login resolver races only these credential-free GETs:

| Provider | IP-literal HTTPS endpoint |
| --- | --- |
| Google | `https://8.8.8.8/resolve?type=A&name=...` |
| Cloudflare | `https://1.1.1.1/dns-query?type=A&name=...` |

Both send `Accept: application/dns-json` and fully verify their certificates.
The first completed Status=0 response with at least one public IPv4 A record
wins. Empty/error/timeout/private-only responses do not win. All A records from
the winner are filtered/deduplicated. Private, loopback, link-local, unspecified,
multicast and the existing additional nonpublic ranges are rejected.

One lookup clock is capped at 5 seconds for the entire race, never 5 seconds per
provider (the final requested cap replaces the earlier 2-second proposal).
It is further capped by the host/login deadline minus a 1-second POST
reserve. On completion, cancellation or expiry, the resolver cancels and joins
all provider workers before returning. Native curl is killed and reaped; managed
I/O is aborted and its provider worker is joined. There is no background loser
left to outlive the lookup. The old VPN race executes in the Go Core with
DNS-wire transports/configuration, rather than this JSON resolver; there is no
direct reusable helper for an additional login provider, so none was added.

For the initial curl, a monitor starts its one-second timer only after native
process launch. Fixed verbose connection/TLS/request milestones move it out of
the connecting phase; raw stderr/headers are neither stored nor logged. If curl
is still connecting at one second, a speculative race can start under the same
budget/reserve. No POST is raced or started by this monitor.

A first HTTP response, success, cancellation or non-CanRecover failure discards
the speculative answer and cancels/joins pending prefetch. A healthy login below
one second makes zero DNS requests. Only a real CanRecover failure consumes the
prefetch result and commits a new answer to the five-minute cache. If that race
already failed, it is not repeated. If prefetch never started, ordinary bounded
recovery does the lookup. HTTP response headers cancel in-flight prefetch even
while the initial curl is still reading its body. The monitor is joined before
LoginRecovery.Send exits.

A failing cached answer set is evicted. If its retry remains CanRecover, the
same recovery may perform one fresh lookup and a final request within the same
deadline. Fresh-answer failure never recurses. Only answers are shared across
logins; recovery eligibility/cancellation are per login. No cache-first preference
hint was added: system DNS remains first, with the requested prefetch supplying
latency overlap without changing the next login's initial transport policy.

`[LoginRecovery]` records contain hostname, fixed stage/reason, provider winner
and elapsed milliseconds, exit/HTTP code and connection/upload metrics. They
contain no username, password, body, headers or raw stderr. Final errors retain
sanitized initial and recovery metadata, including unknown-stage recovery
timeouts. Both Login2 callers catch cancellation/deadline errors; foreground
UI always uses the existing generic message while the window is open.

## Offline validation

Requirements: Python 3, a C compiler (`cc`), .NET 8 runtime/Roslyn/reference
files, and native curl at `/usr/bin/curl`. Supply .NET 4.8 references for both
production transport and complete controller/REST/transport compilation:

```text
python tests/LoginRecoveryChecks/run.py <dotnet> <csc.dll> <net8-refs> <net48-refs>
```

The runner executes actual production process execution, argument generation,
metrics parser, recovery/cache/provider race, managed HTTP and controller code.
TLS loopback servers use a temporary CA and valid/wrong-host certificates; no
validation override or machine trust-store change is used. A getaddrinfo test
shim returns EAI_NONAME for .invalid without sending public DNS traffic; real
curl still produces exit 6 and its -w trailer. Closed loopback ports exercise
exit 7. A real TLS handshake stall checks the revised failover rule.

Coverage includes provider timeout/error/empty/private-only results, winner
selection and elapsed logs; real curl and managed loser cancellation with joined
workers/no new curl PID; zero-DoH fast login; delayed success discard/cancel;
late exit 6/7 prefetch consumption; cache commit/eviction; concurrent actual
credential curl serialization; TLS-positive replay rejection; HTTPS vs HTTP
policy; managed DNS vs timeout; launch-deadline availability; marker spoofing;
and existing wrong-DNS, wrong-certificate, 401/406/500, post-send drop, budget,
cancellation, independent-login and log-privacy checks.

The full run additionally executes existing password re-login/loading-stage
checks plus the actual async presentation wrapper with a fake dispatcher for
exit during action/render/minimum wait and generic timeout text/four-second
minimum. These are method-level checks, not a Windows WPF executable build.

Validation result for the final five-second cap: full run.py exit 0, **175 PASS**
records, including both .NET 4.8 compilation checks. Environment: .NET SDK
8.0.422/Roslyn, .NET 8.0.28 runtime/references, .NET Framework reference assemblies
4.8 package 1.0.3, and Linux curl 8.5.0. A response arriving after two seconds is
explicitly tested to succeed under the new shared five-second cap.

## Manual Windows checklist / verification limits

- The supplied diagnostic log records bundled `curl64.exe` fileVersion **8.21.0**.
  The packaged binary is unavailable here, so verify the final build with
  `curl64.exe --version`. Confirm its -w trailer on exit 6/7/35 and multi-address
  comma-separated --resolve support (curl >= 7.59), plus verbose connected/TLS
  milestones on that Windows build.
- With a test hosts entry targeting an unreachable/wrong IP, verify recovery
  sends exactly one POST. A certificate for another domain must still be rejected
  after fallback. Cancel/exit during DoH must leave no curl child process.
- Linux native curl tests and .NET 4.8 reference compilation do not execute the
  protected single-file Windows package, Windows certificate backend or WPF
  dispatcher. These Windows smoke checks remain outstanding.
