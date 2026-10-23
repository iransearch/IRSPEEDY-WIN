# Android-style VPN DNS

Windows 1.4.34 uses the app-owned native `vpn-dns-race` transport in the updated
Throne-G Core. Reference Android commit:
12f470f443f5f5217a4713f87f24469feacd44f9.

| Provider | Server | Transport | TLS name |
| --- | --- | --- | --- |
| Google | 8.8.8.8 | DoH /dns-query | dns.google |
| Cloudflare | 1.1.1.1 | DoH /dns-query | cloudflare-dns.com |
| Yandex Basic | 77.88.8.8 | DoH /dns-query | common.dot.dns.yandex.net |
| Additional resolver | 78.186.60.77 | UDP/53 | — |

All primary requests run concurrently through `proxy`, with an 8-second stage
deadline. Valid replies enter a buffered completion queue: a later-listed
provider which finishes first keeps its place even if Google's reply is also
ready when selection resumes. Losers are canceled without delaying the caller.

Raw forwarding bypasses IP caches, including repeated raw A/AAAA requests.
Raw failures produce SERVFAIL. IP lookups maintain separate TTL cache entries per
provider and family. Before any network work they scan all selected DoH/TCP
caches, including the fallback; all requested families must be present, unexpired
and nonzero TTL. Multiple valid cache hits use configured order, as on Android.
UDP checks its cache inside its IP worker. Both address families share one
logical provider race. Positive partial-family results can complete a provider;
complete negative results can also win.

After primary failure/timeout, one `tcp://1.1.1.1:53` fallback stage gets 4 seconds
through the same VPN. There are no TCP reuse probes or implicit UDP-to-TCP retries.
When an IP provider has produced no usable record, its stage waits for its
deadline as on Android. NOERROR (including NODATA) and NXDOMAIN are valid;
malformed/truncated/mismatched replies and server errors cannot win.
Parent cancellation prevents fallback.

Remote DNS rules receive a 12-second outer budget. Explicit shorter timeouts,
cache-bypass flags and scoped/direct rules remain effective. Direct/bootstrap
DNS, server-test DNS, AI/Pool routing and Split Tunnel exclusions keep their
existing policy. TUN shared Windows DNS routes to the same composite.

**Rebuild the updated Throne-G Core as well as Windows 1.4.34.** The old Core
does not recognize the new transport. Use the existing branch
`update/fc668b60-core-only-1.3.0-beta.1`. The local DNS engine is packaged in that
repository; no dependency-pin or external builder override is required.
Core implementation commit: 71b93203ed46de66a86ce3294b67b197573db0bf.

Debug logs distinguish raw/network, IP/network and IP/cache winners. Provider
tags map to primary-0/1/2/3 (Google/Cloudflare/Yandex/additional), or fallback.
`dns.final` is a routing default, not the actual winner.

Generate and verify Windows configuration fixtures:

```sh
IRSPEEDY_DNS_FIXTURES=/absolute/path/to/fixtures dotnet run --project tests/ParallelDnsChecks
python tests/ParallelDnsChecks/run-core.py /path/to/Throne-G /absolute/path/to/fixtures
```

The runner uses `go test -race` on the real local DNS engine, transport and
CLI/Check parser. Native tests cover completion order, each provider, real local
TLS/UDP/TCP detours, raw/IP cache separation, preflight including fallback,
TTL=0, dual-family coordination, cache bypass/clear, negatives, stage deadlines
and worker cancellation. Live Windows TUN/DoH validation requires the packaged app.
