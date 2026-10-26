# Windows login DNS and recovery (1.4.51)

The production login uses classic DNS first, matching the successful 1.4.50
behavior. This cleanup removes experiment switches and obsolete login DoH
transport code. It does not change the UI, Shield, routing, Core, VPN DNS race,
TLS verification, replay rules or the four-second minimum loading presentation.

## Production flow

For an HTTPS hostname, resolve first through LoginPlainDns and its five-minute
LoginDnsCache. Google 8.8.8.8, Yandex Basic 77.88.8.8, Cloudflare 1.1.1.1 and
AdGuard default 94.140.14.14 race A and AAAA queries on UDP port 53. A truncated
UDP reply uses TCP to the same server within the existing lookup clock.

The first valid public address set wins. Invalid, empty, failed and private-only
answers cannot win. Losing sockets are canceled/closed immediately; workers are
joined for cleanup, without waiting for their DNS answers. Socket errors caused
by cancellation are logged as canceled, not as independent provider failures.
There are no parallel credential POSTs. The fastest DNS answer is not a transfer
speed test and does not guarantee that its address family works on every ISP.

Packets validate the connected source, transaction ID, question, response flags,
CNAME owner chain and bounded compression/name parsing. Local, loopback,
unspecified, multicast and other nonpublic IPv4 addresses are rejected. IPv6 is
restricted to unscoped global unicast, excluding documentation addresses.
Classic DNS is unencrypted and unauthenticated. HTTPS certificate validation
remains mandatory even if an ISP modifies DNS answers.

curl pins the selected public addresses using --resolve while preserving the
original URL, Host, SNI and certificate validation. IPv6 addresses are bracketed
in the resolve argument. No -k/--insecure, redirected credential POST, or managed
TLS-validation bypass is enabled. No system DNS configuration is modified.

The four API endpoints retain sequential order and last-good preference. System
DNS is a sequential fallback only after an empty/failed lookup or a failure
proven unsent by the existing policy. A failing cached set is invalidated and
may be fetched once again under the same deadline; a fresh failed answer does
not restart a recovery loop. Each login has independent recovery/cancel state.

## Deadlines and execution

- Shared login network budget: 45 seconds.
- Per-endpoint budget: at most 20 seconds, capped by remaining flow time.
- Entire DNS race: at most 5 seconds, not 5 seconds per provider.
- DNS allocation keeps a three-second POST reserve inside the endpoint budget.
- curl connect timeout: at most 3 seconds inside that same budget.
- Secondary settings request retains its independent four-second limit.
- Native POSTs retain HTTP/1.1, Connection: close and --no-buffer.

Startup, credential-gate queueing, DNS and transfer consume their existing shared
deadlines. Native max-time leaves a small trailer/output flush margin. A shared
credential gate serializes foreground and background requests. Cancellation
closes DNS sockets and stops curl. Generic connection errors remain user-facing;
technical stage/metric details stay in logs. Exit skips closing-window UI work.

## Replay evidence

For HTTPS, MayHaveBeenSent is true when metrics are unknown or any of upload,
HTTP status, redirects, TLS completion or pretransfer indicates possible send.
Exit-code-only assumptions do not override complete HTTPS metrics. Plain HTTP
retains its earlier conservative exit 52/55/56 and connected-timeout guards.

The narrow DefinitelyNotSent exception handles the observed bundled-curl exit 6
with positive pretransfer despite failure in local DNS. It requires known
metrics, available curl, exit 6, zero connect/TLS/upload/HTTP/redirects and no
remote IP. It applies only to HTTPS decisions, including final host failover.
Incomplete/default-zero metrics cannot authorize retry.

Same-host recovery remains limited to proven-unsent exit 6/7/35/60, or exit 28
with zero connect time. The separate unsent connected-TCP timeout gate permits
resolve only; any retry requires a different validated public peer. A second
attempt's send evidence always controls its final failure decision.

A nonzero curl exit after an HTTP response in resolver-first mode is a failed or
partial transfer and blocks replay. HTTP 200 alone, parseable JSON alone, or a
large downloaded byte count cannot prove completion. Missing terminal chunks
and wrong lengths remain failures. Complete HTTP responses retain the existing
401/406 final policy and explicit HTTP 500/application validation policy.

Managed fallback is restricted to curl launch unavailability. Proven managed
DNS/connect/pre-write failures can advance hosts; ambiguous send/receive/TLS or
timeout phases stay replay-blocking. A request timeout does not disable curl for
the rest of the program.

## Ownership and diagnostics

Process cleanup uses bounded kill/wait/drain intervals. PendingStops retains
unconfirmed live process handles and blocks subsequent credential requests.
Its shared timer prunes and disposes released, exited owners even without a
subsequent login; shutdown prevents late re-registration. Reader/callback
ownership ends before the retained process can be disposed.

Logs use neutral resolver-first-* and dns-recovery-* stages. Provider events
identify the provider and A/AAAA type, winner and elapsed time. Credential
results record canonical remote IP/family, HTTP status/version, curl exit,
connect/TLS/pretransfer/upload evidence and optional receive timing/byte count.

Native login POST stdout is read incrementally; ordinary curl/DoH GET readers
are unchanged. Numeric progress samples every two seconds report attempt ID,
hostname, elapsed time, stdoutChars, first/last chunk time and idle time. The
write-out trailer adds a final stdout chunk; periodic samples distinguish it
from body progress. stdoutChars counts characters, not exact network bytes.

Only allowlisted response metadata is retained: content type, numeric content
length, transfer coding, connection mode and framing consistency. BodyKind is
bounded shape/JSON classification, not application acceptance. Raw body, JSON
values, cookies, credentials, arbitrary headers and stderr are not logged.

## Source and tests

- LoginRecovery.cs: deadlines, transport/replay evidence, sequential recovery.
- LoginDnsRace.cs: protocol-independent first-valid-answer race and cancel/join.
- LoginDnsCache.cs: public-address filtering and five-minute cache ownership.
- LoginPlainDns.cs: classic UDP/TCP DNS packet encoding, validation and transport.
- LoginPrefetch.cs: retained system-first policy's resolve-only prefetch lifecycle.
- CurlHelper.Login.cs: native login arguments, response metadata and progress.

ResolverFirst is the explicit production order. SystemFirst is retained as an
internal policy for compatibility and existing recovery/prefetch tests, not as
a runtime experiment toggle. Historical fake DoH providers/JSON parsing are
now test-only fixtures; the unrelated DohResolver used by other requests is
unchanged. WPF's SDK-style project includes the new source files automatically.

Run tests/LoginRecoveryChecks/run.py with .NET 8/compiler references and .NET 4.8
references. It exercises native verified loopback HTTPS, real loopback UDP/TCP
DNS, malformed responses, first-valid selection, IPv6 curl pinning, canceled
socket classification, cached-peer refresh, partial transfers, process ownership,
POST serialization, controller failover and UI lifecycle. It also compiles the
complete production controller/REST/transport path against .NET Framework 4.8.

## Windows manual checks and observed evidence

Bundled Windows curl 8.21.0 was reported by earlier user logs. The native Linux
harness uses curl 8.5.0. The bundled executable and a protected single-file WPF
Windows build were not executed in this environment.

On Windows verify -w trailers on exits 6/7/35, comma-separated multi-IP --resolve
support (curl >=7.59), strict wrong-host certificate rejection, cancellation
mid-resolve/transfer without orphan curl, and hosts-file wrong-IP recovery with
exactly one delivered POST. Check normal UI loading and cancellation unchanged.

The supplied 1.4.49 direct log stalled at 68,810 body bytes after roughly 0.855s;
VPN later completed a request to the same IPv4 peer. The 1.4.50 log selected a
Cloudflare AAAA result in 162ms and completed 159,520 bytes over IPv6 with exit 0
and application validation. This supports the working IPv6 path; it does not
prove an ISP intentionally blocks DoH. Confirm real behavior without external
VPN on the user's ISP and on networks lacking IPv6. Server/CDN logs were not
available and are not a prerequisite for these client-side tests.
