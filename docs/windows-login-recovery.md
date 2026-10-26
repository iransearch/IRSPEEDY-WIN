# Windows login recovery (1.4.46 verification diagnostics)

Baseline: `94d7bc0af460627eedfa82025ad12b9b30d86c9c` (1.4.45).
The supplied 1.4.45 log confirmed a Google winner, valid public A answers and
native --resolve connection/TLS success. Both credential attempts received HTTP
200 but ended with curl exit 28; no system/next-host replay was attempted.
This release adds transfer-only diagnostics to locate the response stall;
request order, deadlines, replay rules and TLS validation are unchanged.

This temporary verification build enables `CurlHelper.LoginDohFirstVerification`:
HTTPS hostname logins first use the existing Google/Cloudflare DoH race/cache,
then curl --resolve. System DNS is a sequential fallback only after no DNS answer,
curl launch unavailability, or a failure proven unsent by the existing HTTPS
metric policy (including the narrow exit-6 proof from 1.4.44). This is an
explicit test order, not a permanent change to the intended default. Set the
single verification switch to false to restore the retained system-first path.
No UI setting or user-visible appearance changed.

The existing generic UI message is `ارتباط با سرور برقرار نیست`.

The four API endpoints retain their sequential order, last-good preference,
30-second shared network deadline and 8-second endpoint limit. Normal loading
keeps its four-second minimum and existing stages/appearance. Application exit
cancels network/presentation waits and skips work on the closing window. VPN
DNS racing, Shield, routing and Core are unchanged.

## DoH-first verification order

The initial lookup uses the existing shared five-second race cap, shortened by
remaining endpoint/flow time minus a three-second POST reserve. A valid cached
set can satisfy lookup under the same five-minute cache policy. A fresh public
answer sends a single curl request with --resolve, preserving URL, Host, SNI and
certificate verification. Only DNS GETs race. Every credential request, including
system fallback and managed fallback when curl cannot launch, uses the existing
shared credential gate. Speculative prefetch is not started in this mode.

Every completed HTTP response returns through the existing application status
policy; it never triggers system-DNS fallback. Nonzero curl exit with an HTTP
code is a partial/failed transfer and is replay-blocking in verification mode,
including on the system fallback. Unknown stage, upload, completed TLS or other
sending evidence prevents both system fallback and next-API transport failover.
If an unsent attempt used a cached set, that set is evicted/refetched at most
once within the original lookup/endpoint budget, just as in the reference path.
A fresh unsent failure falls back to system DNS once; it cannot restart DoH.
HTTP and IP-literal URLs retain the previous policy without new DNS lookups.

Log stages: dns-order mode=doh-first-verification; doh-first-lookup/answer;
doh-first-attempt/result with resolve=True, cache state, address count and curl
metrics; system-fallback with fixed reason and initial failure; and
verification-result source=system resolve=False. The provider race retains its
winner name and elapsed time records. No body, credentials, raw headers or curl
stderr are logged. Final system-fallback transport errors preserve the initial
DoH attempt's sanitized metadata. If no time remains for system fallback, the
existing deadline stops the flow; no timeout is extended. The disclosure of API
hostnames to both DoH providers now occurs on every uncached HTTPS-hostname
login, even when system DNS would be healthy.

## Shared replay policy and retained system-first reference path

In the retained system-first path, the first attempt uses bundled curl and system DNS, with a connection timeout
of at most 3 seconds inside the endpoint deadline. curl `-w` records HTTP status,
time_connect, time_appconnect, time_pretransfer, size_upload, num_redirects and remote_ip
on failures as well as successes. Parsing takes each marker's last occurrence,
so identical text in the response body cannot override the final trailer.

For HTTPS, possible sending is determined only by:

```text
!MetricsKnown || UploadedBytes > 0 || HttpCode > 0 || Redirects != 0
    || TlsSeconds > 0 || PretransferSeconds > 0
```

`MayHaveBeenSent` retains this raw rule. For HTTPS only, `DefinitelyNotSent`
can override it when **all** of these hold: curl is available, MetricsKnown,
exit 6, zero connect/TLS/upload/HTTP/redirects and no remote_ip. Positive
pretransfer alone cannot defeat this complete DNS-failure proof. The same
HTTPS-aware decision is used for DoH eligibility and final Failure()/exception
classification, including a failed recovery curl. Plain HTTP never applies the
override. Exits 7/35/60/28/52/55/56 do not gain an exception for positive
pretransfer. Unknown/partial metrics or any contradictory connection/sending
indicator still close the override. A possibly-sent second attempt is never
cleared by proof belonging to the initial attempt.

Missing/invalid metrics fail closed, including positive submillisecond values
outside that narrow DNS-stage exception.
A completed TCP connection alone does not prove a POST reached an HTTPS server.
Known-zero TLS/pretransfer/upload/HTTP/redirect indicators permit next-host
failover on handshake stalls/resets, including exit 28/52/55/56. Same-host DoH
CanRecover remains narrower: exit 6/7/35/60, or exit 28 only when time_connect is
zero; all must be proven unsent. A separate conditional lookup also permits
HTTPS exit 28 after TCP when the same all-zero sending indicators hold and a
valid remote_ip is known. It authorizes a credential retry only if DoH supplies
a different public address. Missing/invalid peer or transfer metrics cannot
enable this path. Plain HTTP retains the previous conservative
exit 52/55/56 and post-TCP exit 28 guards and never uses same-host DoH recovery.

Every HTTP response bypasses DoH. HTTP 401/406 are final; explicit HTTP 500 and
invalid/empty application responses keep their existing endpoint policy. After
a transport failure with possible sending, no API host may replay the POST.

At most one recovery is allowed per hostname per Login2 call. Public A records
excluding every known failed peer are passed together through
`--resolve host:443:IP1,IP2` (or the explicit
URI port). URL, Host, SNI and certificate checks stay intact. Login redirects
are explicitly disabled through BuildArgs; other callers keep their default.
Neither transport disables TLS validation. A static credential-attempt gate
also serializes foreground/background login curl and managed attempts, charging
queue time to the same deadline. DNS GETs do not take that gate.

A started curl killed by its outer process deadline has unknown sending state:
its TimeoutException stops endpoint replay, without disabling curl globally.
Pre-launch TimeoutException propagates without changing availability. Only
exhausting absent, invalid-PE or unlaunchable candidates sets `_curlUnavailable`.

Termination after Kill waits at most 2 seconds. Output/error EOF is acknowledged
separately, with a drain bounded by the remaining request budget and a 2-second
cap: an exited parent with inherited pipe handles cannot block indefinitely.
There is no parameterless WaitForExit in the execution/cleanup path. If the OS
does not confirm termination within the grace, the process handle is retained
and later credential attempts fail closed until exit is confirmed. No reaper
thread is created, PID reuse cannot release the guard, and unavailable-curl
managed fallback cannot bypass it. Killing and draining do not authorize replay
or consume a new network-request budget; termination grace is cleanup time.

Pending-stop handle ownership (1.4.42): ExecuteCurl first detaches stdout/stderr
callbacks and closes its readers, then hands the process to the pending registry.
A single shared ThreadPool timer, armed only while entries remain, checks for
exit every second. Exited, handed-off processes are removed and Disposed without
any subsequent login. The timer is itself Disposed when the registry empties;
a later pending stop re-arms it. A live process is retained, and an executing
owner's process is never disposed by that timer before handoff. Credential checks
also prune, but are no longer the sole cleanup path.

Notify_Exit calls ShutdownPendingCurlStops after canceling login. ProcessExit
and DomainUnload hooks provide idempotent backstops. Shutdown stops/disposes the
timer and releases handed-off handles; owners still executing Dispose in their
own finally. Late timeout registrations cannot repopulate the registry, and a
shutdown latch continues to block credentials even after the registry is empty.
No wait-for-exit, per-process polling task or dedicated cleanup thread is added.

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
It is further capped by the host/login deadline minus a **3-second POST
reserve**, in ordinary recovery, cache refresh and prefetch alike. After a
3-second first attempt in an 8-second endpoint, a non-prefetched lookup gets
at most about 2 seconds, leaving about 3 seconds for TLS, POST and response.
Scheduling, cleanup and another login's credential-gate queue can consume some
of that remainder; it is a lookup-allocation reserve, not a deadline extension.
On completion, cancellation or expiry, the resolver cancels and joins
all provider workers before returning. Native curl is killed and reaped; managed
I/O is aborted and its provider worker is joined. Unconfirmed OS termination is
retained behind the fail-closed credential guard rather than waited on forever.
The old VPN race executes in the Go Core with
DNS-wire transports/configuration, rather than this JSON resolver; there is no
direct reusable helper for an additional login provider, so none was added.

For the initial curl, a monitor starts its one-second timer only after native
process launch. Fixed verbose connection/TLS/request milestones move it out of
the connecting phase; raw stderr/headers are neither stored nor logged. If curl
is still connecting at one second, a speculative race can start under the same
budget/reserve. No POST is raced or started by this monitor.

Privacy: on a system-DNS lookup/connection still pending after one second, the
API hostname is sent directly to **both Google and Cloudflare**, even if system
DNS is merely slow and the first request eventually succeeds. Canceling the
prefetch/discarding its cache result cannot retract an already-sent DNS query.
These GETs carry no username/password or login body. This disclosure is the
explicit tradeoff of the existing prefetch policy; no new toggle/UI was added.

A first HTTP response, success, cancellation or ineligible transport failure discards
the speculative answer and cancels/joins pending prefetch. A healthy login below
one second makes zero DNS requests. Only a proven-unsent eligible failure with a
usable peer consumes the prefetch result and commits a new answer to the
five-minute cache. If that race
already failed, it is not repeated. If prefetch never started, ordinary bounded
recovery does the lookup. HTTP response headers cancel in-flight prefetch even
while the initial curl is still reading its body. The monitor is joined before
LoginRecovery.Send exits.

A failing cached answer set is evicted. If its retry remains proven-unsent and
eligible (including conditional alternate-peer timeout), the same recovery may
perform one fresh lookup and a final request within the same
deadline. Fresh-answer failure never recurses. Only answers are shared across
logins; recovery eligibility/cancellation are per login. No cache-first preference
hint was added: system DNS remains first, with the requested prefetch supplying
latency overlap without changing the next login's initial transport policy.
If a cached set contains only the already failed peer, it is evicted/refetched
once without issuing a redundant credential request. A fresh set containing
only failed peers ends recovery. Cache invalidation retains the original answer
reference even when the retry uses a filtered address subset.

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

The 1.4.41 additions exercise a real slow HTTPS response after a 3-second first
failure/near-budget DoH; the prefetch reserve; empty/spoofed/invalid remote_ip;
same/different peers on exit 28/35/60; cached same-peer eviction; native TLS-stall
recovery with one verified POST; killed-process cancellation; inherited stdout/
stderr pipe handles; and a retained live process blocking credentials until
confirmed exit. The shared five-second race cap and all earlier checks remain.

The 1.4.41 full run.py exited 0 with **198 PASS** records, including both
production .NET Framework 4.8 compilation checks. Environment: .NET SDK
8.0.422/Roslyn, .NET 8.0.28 runtime/references, reference assemblies net48 package
1.0.3 and Linux curl 8.5.0. Native curl can report zero time_connect despite an
accepted loopback TCP socket; the native test accepts that existing CanRecover
case too, while separate deterministic tests cover positive-connect alternate-
peer gating. No TCP-only timing is treated as evidence of a sent HTTPS POST.

The 1.4.42 regression checks inspect real SafeProcessHandle.IsClosed to verify
independent cleanup without another POST; owner handoff; retaining live processes;
timer disposal/re-arming; the actual ProcessExit hook in a child executable; and
explicit shutdown with active owners and late registration. Shutdown probes run
in separate executables so the real fail-closed latch is never reset for tests.
The full 1.4.42 run.py exits 0 with **205 PASS** records, including both .NET
Framework 4.8 compilation checks; no normal loading/replay/DNS behavior changed.

## Manual Windows checklist / verification limits

- The supplied diagnostic log records bundled `curl64.exe` fileVersion **8.21.0**.
  The packaged binary is unavailable here, so verify the final build with
  `curl64.exe --version`. Confirm its -w trailer on exit 6/7/35 and multi-address
  comma-separated --resolve support (curl >= 7.59), plus verbose connected/TLS
  milestones and remote_ip on that Windows build.
- With a test hosts entry targeting an unreachable/wrong IP, verify recovery
  sends exactly one POST. A certificate for another domain must still be rejected
  after fallback. Cancel/exit during DoH must leave no curl child process.
- Verify same-IP DoH answers skip the retry, and a TCP-accepted/TLS-stalled
  poisoned address can recover only via a different public peer with zero sent
  indicators. Exercise inherited pipe handles and AV-denied Kill on Windows:
  cleanup must return within the bounded grace and credentials must stay blocked
  until process exit is confirmed. Linux tests cover real ordinary Kill/pipe
  inheritance and inject a retained live process to test that guard; they do not
  reproduce antivirus refusal.
- Linux native curl tests and .NET 4.8 reference compilation do not execute the
  protected single-file Windows package, Windows certificate backend or WPF
  dispatcher. These Windows smoke checks remain outstanding.

## 1.4.43 decision diagnostics

Login result summaries now include pretransfer, redirect count, curl availability,
MayHaveBeenSent, CanRecover, alternate-peer eligibility and whether a remote IP
was observed. Recovery decisions include HTTPS/literal-host guards, per-login
recovery availability and remaining lookup/endpoint budgets. Lookup events record
prefetch use, cache use and answer count. HTTP-return events identify nonzero curl
exits with an HTTP code; controller events distinguish invalid/null responses,
HTTP policy and replay-blocking transport failures. No credentials, response
bodies, raw curl output or headers are logged. This release only adds diagnostics;
retry and response handling policies are unchanged.

## 1.4.44 DNS-stage regression checks

The exact captured Windows trailer is parsed, then used before a real native
curl HTTPS retry against a certificate-verified loopback server (one POST).
Negative cases independently set unknown metrics, TCP, TLS, upload, HTTP,
redirects, peer, unavailable curl, partial trailer and other exit codes.
Production Failure() plus the extracted production controller are exercised
with no DoH answer and a DNS-failed recovery curl, permitting exactly one
next-host failover. A posted recovery failure remains replay-blocking. Logs
include raw mayHaveBeenSent, definitelyNotSent and HTTPS-aware
effectiveMayHaveBeenSent without credentials. No DNS-provider budgets,
UI, VPN, Shield, routing or Core behavior was changed.

Manual Windows check: repeat offline login with the bundled curl and verify
exit 6 / positive pretransfer now logs definitelyNotSent=True,
effectiveMayHaveBeenSent=False, starts Google/Cloudflare, and permits the
next host after bounded DNS failure. Reconnect and verify normal successful
login; no Windows packaged build was executed by the Linux harness.

## 1.4.45 verification checks and manual follow-up

DohFirstChecks covers a fake provider race with a private-only invalid answer,
a valid winner and a real certificate-verified HTTPS POST; exact one-POST
system fallback after a bad DoH peer; cached failure eviction/one refresh;
401/406/500 without DNS-order replay; native post-send connection drop;
wrong-domain certificate rejection before and after system fallback; lookup
empty/error without DNS loops; unknown/posted/partial-HTTP replay blocking;
managed fallback only for unavailable curl; shared credential serialization;
lookup/POST budget allocation; and cancellation/expiry before I/O or during DNS.
The complete earlier system-first/recovery/prefetch checks remain enabled to
verify that switching the experimental flag off restores that behavior.

On Windows, with internet connected and ordinary system DNS healthy, log in and
verify a Google/Cloudflare winner (or a fresh cache hit), doh-first-attempt with
resolve=True, and doh-first-result exit=0/http=200, without system-fallback.
Also test with internet fully disconnected: each host should record bounded
no-answer/failure, one system attempt and safe next-host failover. These logs
verify the real bundled curl/certificate backend; the Linux tests do not run the
protected single-file WPF package.

## 1.4.46 transfer-only diagnostics

The final native -w trailer adds time_starttransfer, time_total, size_download
and http_version. LoginCurlResult stores these as optional diagnostics, separate
from the existing MetricsKnown/replay evidence. Missing/invalid values are
logged as unknown (never as an invented zero); they cannot change replay or
DoH eligibility. Numeric parsing rejects negative, NaN and infinite values.
Each marker still uses its final occurrence. HTTP versions use a fixed
allowlist, and remoteIp is emitted only as a canonical IP address (with
remoteFamily=IPv4/IPv6/unknown). No arbitrary stderr/header/body text is emitted.

Result summaries log transferMetrics, firstByte and total (seconds), download
(body bytes reported by curl), httpVersion, remoteIp/family and bodyChars
(captured .NET string length). These fields appear on system and --resolve
attempts and are retained in sanitized transport errors. bodyChars is a count,
not response content; bytes and characters need not be equal for non-ASCII or
encoded payloads. A first-byte timestamp is curl's first response byte and can
include an informational response; it is not the time the complete body arrived.
No expected Content-Length is inferred and a download count is not proof that
an application response is valid.

TransferDiagnosticChecks validates final-marker precedence, missing/invalid
optional values, scalar-only log privacy, canonical IPv4/IPv6, invariant
formatting, successful verified native HTTPS and real HTTP 200 timeouts with
zero or one body byte. The timeout tests execute the production DoH-first path
and confirm one POST, no system fallback and replay-blocking final errors.

Windows follow-up: keep internet connected, log in with this verification build
and compare firstByte/total/download/bodyChars/httpVersion/remoteIp across
attempts. A final HTTP 200 with zero download isolates a header-only timeout;
positive partial download isolates incomplete transfer. No raw response,
credentials, user headers or certificate-validation bypass should be enabled.
The protected single-file Windows package still requires a Windows build and
smoke test; Linux native tests and .NET 4.8 compilation do not execute that UI.
