# Hostkey connection diagnostics (Windows 1.4.65)

This temporary investigation records only `hostkey.com` and its subdomains,
including `invapi.hostkey.com`. It observes existing requests; it does not send
an HTTP request, resolve an additional name, alter cache entries, change routing,
prefer an address family, retry a request, or change TLS validation.
There is no notification or additional UI.

Windows 1.4.67 uses Core's [Proxy domain strategy options](proxy-domain-strategy.md),
superseding the IPv4-only policy introduced in 1.4.66.
The instrumentation itself remains observational. Compare new Proxy A-only
results with TUN's retained dual-family behavior; the Core diagnostics and
required Core commit below remain unchanged.

Rebuild the updated Throne-G branch and package its modern/legacy Core payloads
in `IRSpeedyVPN/Resources/Files.zip`, then build the Windows branch normally.
An application-only rebuild with an old Core cannot produce these new events.

Required Core commit: [ 141a42f ](https://github.com/iransearch/Throne-G/commit/141a42f90f393244f47fa75261ad3d1eb4c830c9).

The normal log retains `stage=core-detail event=hostkey-*`:

| Event | Meaning |
| --- | --- |
| `hostkey-begin` / `hostkey-end` | Observed stream start/closure, elapsed time, final transport error category and native error number when available; standalone DNS traces finish at `hostkey-dns-end` |
| `hostkey-dns-begin` | Actual query type (1=A, 28=AAAA, 65=HTTPS, 0=IP lookup) and the existing strategy number |
| `hostkey-dns-winner` | Provider index, network/cache source and cache hit; indices 0..6 match the seven configured providers, 7 is TCP fallback |
| `hostkey-dns-end` / `hostkey-dns-address` | Returned A/AAAA counts, DNS response code when available, up to eight literal answers, family and public/private classification |
| `hostkey-route` / `hostkey-route-address` | Actual matched main/AI/direct/block/other route, inbound kind and candidate addresses after normal routing/sniffing |
| `hostkey-pool-dial` / `hostkey-dial-address` | Existing Pool member attempt, member tag hash, destination family/address and dial result |
| `hostkey-limit` | This trace reached its diagnostic event limit |

Correlate using Core PID + Box + `trace`, with `request` linking to the existing
Core connection ID and `query` linking DNS operations. The member hash equals
the existing `coreMemberTagId`, allowing a member-state lookup. A TUN system DNS
query and the later TCP connection are separate requests; compare their literal
Hostkey answer/destination addresses and timestamps, not a fabricated shared ID.
A repeated refresh may reuse an existing TCP/HTTP2 connection and perform no DNS
query; an absent fresh DNS event is meaningful, not proof of a broken resolver.

Raw TUN DNS uses `source=raw-network`; proxy destination IP lookup can use
`ip-network` or `ip-cache`. A `dns-race` success does not certify that the returned
IP is reachable: NOERROR/NODATA and NXDOMAIN retain their existing semantics.
`rcode=-1` means the IP lookup API did not expose a DNS response code.
`hostkey-end error=none` describes transport closure, not an HTTPS status or TLS
certificate verdict. No encrypted HTTP payload/status is inspected.

No URL paths, query strings, credentials, headers, process paths, arbitrary
domains or raw exception messages are recorded. Literal addresses are accepted
by the Windows filter only on these explicitly scoped Hostkey address events.
The general DNS privacy filter is unchanged. Each Box admits at most 128 Hostkey
traces; each trace emits at most 64 ordinary records plus its terminal record.
The existing bounded asynchronous Core/Windows log queues remain in use. There
is no static trace/IP map, timer, background worker or extra request.

## Proxy DNS verification

The production C# policy fixtures run through real loopback HTTP CONNECT and
SOCKS5 inbounds, the native seven-provider race, normal DNS routing and Shield.
Checks retain direct/bootstrap exclusions and verify the destination reaches
the selected outbound as an IP without changing the relayed bytes.

A focused check also reproduces the existing first-positive-family behavior:
when AAAA completes before A, proxy lookup can return IPv6 alone, even with
`prefer_ipv4`; a partial-family cache hit can return it again on refresh.
This change records that behavior without correcting or changing DNS policy.
It is a reproducible risk, not confirmation of the reported Hostkey failure.

Run the native suites with `IRSPEEDY_DNS_FIXTURES` pointing to fixtures emitted
by `tests/ParallelDnsChecks`, including `internal/hostkeydiag` and
`internal/autoselector`. Run `tests/ConnectionDiagnosticChecks`; optionally set
`IRSPEEDY_HOSTKEY_NATIVE_LOG` to verbose native proxy test output to check real
Core events through the Windows parser and final log policy.

Manual Windows verification: open the reported Hostkey URL over Turkey in
System Proxy, wait for the failure, refresh, then repeat in TUN and export the
log. Keep the existing network/settings unchanged during the comparison.
