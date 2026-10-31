# Android-style VPN DNS

Windows uses the app-owned native `vpn-dns-race` transport introduced in 1.4.36's updated
Throne-G Core. Reference Android commit:
12f470f443f5f5217a4713f87f24469feacd44f9.

| Provider | Server | Transport | TLS name |
| --- | --- | --- | --- |
| Google | 8.8.8.8 | DoH /dns-query | dns.google |
| Cloudflare | 1.1.1.1 | DoH /dns-query | cloudflare-dns.com |
| Yandex Basic | 77.88.8.8 | DoH /dns-query | common.dot.dns.yandex.net |
| Additional resolver | 78.186.60.77 | UDP/53 | — |
| Additional resolver | 213.14.111.50 | UDP/53 | — |
| Additional resolver | 94.20.20.20 | UDP/53 | — |
| Additional resolver | 217.218.127.127 | UDP/53 | — |

Windows 1.4.64 uses seven primary providers for TUN, Proxy and Residential.
It requires the updated Throne-G Core accepting 2..7 primaries, with diagnostics
covering all seven plus the TCP fallback. Login retains its separate seven-server
classic DNS race. The native probe/test `https-race` is unchanged.

Do not package this app with Core aecfe86f4cbec5c7cab2b7d7f76bee324444a875:
that Core rejects more than four primaries before binding the user proxy listener.
Version 1.4.62 exposed this incompatibility; 1.4.63 temporarily restored four.
The configuration suite checks the native limit separately from the expected IP
list, and the native runner checks generated seven-provider profiles directly.

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
DNS, server-test DNS, AI/Pool selection and Split Tunnel exclusions keep their
existing policy. TUN shared Windows DNS routes to the same composite.

In Proxy mode, HTTP/SOCKS destination domains now pass through `resolve` before
the final main/AI VPN route. DNS routing is retained (no forced `server` bypass),
so predefined/Shield, localhost, bootstrap and scoped direct rules still apply.
The original domain remains available to AI/domain routing and payload/SNI is
unchanged; outbound setup uses the resolved IP. Existing direct routes run first.
Known blocked domains are rejected before resolution; preceding Shield blockers
are checked again afterwards to catch returned IPs and rule-set IP entries.
Only proxy inbound tags match these rules, never TUN or internal DNS/probes.
The transformation is idempotent, including already-native race profiles.
Browser-owned DoH and applications that resolve before contacting the proxy
are outside this destination-resolution policy.

**Rebuild the updated Throne-G Core as well as Windows 1.4.64.** The previous
Core's four-provider limit is incompatible with this seven-provider profile.
Use the existing branch
`update/fc668b60-core-only-1.3.0-beta.1`. The local DNS engine is packaged in that
repository; no dependency-pin or external builder override is required.
Required seven-provider Core commit:
[21c9a2ccaacf02764c71b5c0238b8909ce1a8412](https://github.com/iransearch/Throne-G/commit/21c9a2ccaacf02764c71b5c0238b8909ce1a8412).
Package the rebuilt modern and legacy Core payloads in `IRSpeedyVPN/Resources/Files.zip`
before running the existing Windows single-file application build. Updating only
the C# application does not replace the bundled Core. No dependency or external
builder override is needed.

Structured Core diagnostics now reach the normal report without Debug logging:
`dns-race-created`, `dns-race-result`, `dns-race-summary`. Result `index` 0/1/2/3
maps to Google/Cloudflare/Yandex/78.186.60.77; 4/5/6 map to
213.14.111.50/94.20.20.20/217.218.127.127; 7 is TCP fallback, -1 no winner.
With a legacy four-provider profile the new Core still uses fallback index 4.
Windows 1.4.64 accepts `wins0` through `wins7` through the existing numeric
diagnostic allowlist so the new result and summary lines are retained.
`source` is `raw-network`, `ip-network`, or `ip-cache`; `elapsedMs` is the winning
raw stage or complete IP lookup latency. Complete cumulative provider/cache/error
counters survive sampling (first 32 results, every 128th, final summary).
No queried domains, returned addresses, endpoint tags or raw errors are logged.
Windows 1.4.36 also preserves these events in the final `LogPolicy` filter;
1.4.35 inadvertently retained only DNS errors and dropped healthy results and
summaries. Upgrading 1.4.35 to 1.4.36 needs only the Windows app rebuild when Core
aecfe86f4cbec5c7cab2b7d7f76bee324444a875 is already installed. DNS/Shield routing
and resolver selection are unchanged by this logging fix.
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
and worker cancellation. Fixtures also run real HTTP CONNECT/SOCKS5 inbounds with
loopback DNS/providers and captured outbounds: main and AI get IP destinations,
direct/bootstrap routing stays scoped, and Shield domain/IP/inline rule-set/DNS
blocks prevent outbound dials. OS interface monitoring is excluded from that
loopback harness. Live Windows/DoH validation requires the packaged app.
