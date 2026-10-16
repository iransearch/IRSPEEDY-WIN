# Parallel VPN DNS

The active connection's final JSON is transformed after Split Tunnel rules and
typed serialization. The app-owned remote profile races three verified DoH
transports through `proxy`, in this order:

| Provider | Server | TLS name | Path |
| --- | --- | --- | --- |
| Google | 8.8.8.8 | dns.google | /dns-query |
| Cloudflare | 1.1.1.1 | cloudflare-dns.com | /dns-query |
| Yandex Basic | 77.88.8.8 | common.dot.dns.yandex.net | /dns-query |

Yandex documents the Basic DoH IP and TLS name at <https://dns.yandex.com/>.
Google is listed first; a faster valid Cloudflare or Yandex response still wins.
NOERROR (including NODATA) and NXDOMAIN are valid answers. SERVFAIL, REFUSED and
transport errors do not win; if every provider fails the result is SERVFAIL,
without retrying a provider or using direct/system DNS. Existing core TTL caches
remain enabled, and losing work is canceled by the core. Parallel queries can
increase DNS traffic; this does not measure or guarantee website download speed.

The implementation uses native `evaluate` / tagged `respond` / `race` from the
Throne core's pinned sing-box fork, commit
`f154bec036c97fc998a7714bea9977bdc343780a`. No new core binary is required.
The obsolete DNS-rule `strategy` on app bootstrap rules is redundant with
`route.default_domain_resolver.strategy` and is removed for modern DNS mode.
The direct transport, resolver strategy and bootstrap domain rules stay intact.
Explicit Windows shared DNS rules race in TUN Split Tunnel mode; deliberate
direct app DNS remains direct. Probe configurations and custom DNS policies are
left alone.

Run configuration/precedence checks and emit fixtures:

```sh
IRSPEEDY_DNS_FIXTURES=/absolute/path/to/fixtures dotnet run --project tests/ParallelDnsChecks
```

To exercise the generated fixtures with the real pinned DNS parser, validator,
router and client, check out that exact core commit and run:

```sh
python tests/ParallelDnsChecks/run-core.py /path/to/sing-box /absolute/path/to/fixtures
```

`GO` can select the Go executable. The runner temporarily adds a test to the
external core checkout, runs `go test -race`, and removes the test in `finally`.
Tests cover all three winners, scoped/direct DNS, Windows shared DNS, schema
validation, errors, negative answers, AAAA/NODATA and the existing TTL cache.
Live Windows TUN/DoH verification still requires the packaged application.
