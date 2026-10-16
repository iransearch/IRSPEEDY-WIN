# Hysteria2 Xray probe checks

Run `dotnet run --project tests/HysteriaXrayChecks/HysteriaXrayChecks.csproj`.

This harness compiles the production Xray generator and models. Fixtures isolate
WPF/settings and supply parsed nodes; it does not test URI parsing or a live Core.
It checks Hysteria2 probe selection, unchanged single-server selection, existing
XHTTP/Reality selection, and generated Hysteria2 TLS/auth/obfs/SOCKS routing.

Both normal and VOD probes use `LinkNeedsXrayForUrlTest`. Their SOCKS overrides
bridge the probe box to Xray, with `NeedXray=true` and the generated `XrayConfig`.
The SingBox URL-test generator rejects Hysteria2 if that override is missing,
preventing silent fallback to its native Hysteria2 implementation. Retry requests
retain the Xray configuration through the existing retry policy.

This requires the custom Core's `protocol="hysteria2"` adapter. No Core binary,
retry timing, connection timeout, or single-server routing is changed here.
Before release, test both successful and failing Hysteria2 servers on Windows
with the packaged Core, including mixed batches and a retry.

The Smart generator retains the routing template from commit `fdc8e27`: its
leastLoad pool still uses the original `geoip:` / `geosite:` selectors when the
runtime contains the DAT files. Before starting Core, `GeoRoutingFallback`
checks the extracted `V-Guard` working directory. If any required country file
(`geoip.dat`, `geosite.dat`, `geo/ir_IP.srs`, `geo/category-ir_SITE.srs`) is
missing, it removes the country bypass from both the Xray and sing-box configs,
leaving the Smart pool, DNS handling and default proxy route in place. Missing
optional local SRS files only remove their dependent rules. The test exercises
missing, partial and complete runtime payloads with the production fallback.

This check runs before every Core start, including reconnects; missing filenames
are logged once per attempted start. It does not validate corrupt geo data or
replace a live Windows connection test. The embedded runtime remains single-file.
# AI preference lifecycle

Live Smart/country startup now replaces the legacy Xray template's balancers
with independent sing-box Auto Selectors. The legacy builder checks below remain
transport/template regressions. `AutoSelectorPoolChecks` exercises the final
live pool plan (22 checks); see [Auto Selector pools](../../docs/auto-selector-pools.md).

The harness also rebuilds a Smart configuration through repeated AI on/off
transitions in one process, changes the supplied AI members, and verifies that
old rules, balancers and members do not survive. It checks the unset preference
default, ShowIP rule priority, the captured startup preference and unchanged
15-minute/sampling-3 probe settings. Registry access is represented by the
fixture; this does not execute the Windows registry, WPF or the packaged Core.

On Windows, disconnect, save AI off, reconnect, then disconnect, save AI on and
reconnect without exiting the app. Test a new request to `https://showip.net/ip`
after each connection and correlate the route records with the configuration ID.
Settings must remain locked during startup and teardown, including the period
after `Connect()` queues its background task and returns to the UI.

# AI pool isolation

With AI enabled, a nonempty AI balancer explicitly falls back to its first
accepted `ai-proxy-*` member, matching Android. An empty leastLoad selection
must not fall through to Xray's first main-pool outbound. If no AI links are
available or all are rejected, the same AI domains route to `block` before
direct/main rules. AI off removes both the pool and blocking policy.

The regression cases cover mixed accepted/rejected inputs, null/empty inputs,
all-rejected inputs, and repeated state transitions, while retaining the main
pool and 15-minute/sampling-3 probe settings. They compile the production
generator, not a simulated balancer. Live Windows testing should additionally
fail AI probes with a working main pool and confirm that `showip.net` either
uses an AI member or fails; it must never use the main/default route.

`ai-config-apply` now reports `aiPolicy=pool|blocked-empty-pool|disabled` and
`aiFallback`. `aiRoutingEnabled=True` means the AI policy is installed; use
`aiPolicy` to distinguish a usable pool from intentional empty-pool blocking.

# Pool server DNS

Smart connections pass `XrayOutboundDnsStrategy=ForceIP` to Start, encoded as
`LoadConfigReq` field 13. In the compatible Throne Core this attaches its
in-process sing-box resolver, pinned to `dns-direct`. ForceIP returns lookup
errors instead of retrying the same domain through the OS resolver. The existing
direct transport follows the physical interface and uses the configured direct
DNS server; website DNS and AI routing rules are not replaced by this setting.

The Smart generator removes `sockopt.domainStrategy` from main and AI members,
including nested XHTTP download settings, because an explicit socket strategy
overrides the resolver supplied by Core. Other socket/transport settings remain
intact. Hysteria2 has no stream override and inherits the same instance resolver.
Single-server configurations retain their existing server DNS behavior.
Every Smart start, including reconnect and sharing reconfiguration, passes the
strategy anew. `config-apply-begin` records `xrayDnsStrategy=ForceIP`; this records
the requested policy, not confirmation that an old Core binary honored it.

The harness verifies exact protobuf field-13 encoding, main/AI Hysteria2 and gRPC
members, XHTTP upload/download settings, literal-IP preservation, and isolation
from single-server configs. Existing AI fallback/block and 15m/sampling-3
checks run alongside it. These checks do not simulate Windows DNS or QUIC.

Core compatibility was reviewed against `iransearch/Throne-G` commit
`7e7c51e0011aba02a0ffe6a9bf10f3ebcaeca7da` on
`update/fc668b60-core-only-1.3.0-beta.1`, specifically `gen/libcore.proto`,
`server.go:xrayPreparer`, and `internal/xraydns/resolver.go`. Older Core binaries
may ignore field 13; verify the bundled Core's source revision before release.
This change does not replace `Resources/Files.zip` or alter Core lifecycle.

On Windows with a compatible packaged Core, test a mixed domain-named Pool in
TUN mode after both a Wi-Fi disconnect/reconnect and a WAN outage with Wi-Fi
remaining connected. Correlate the requested DNS strategy with resolver logs
and successful new connections. Include DNS failure: it must return an error,
not recursively resolve through the proxy. Recheck AI-only fallback and empty-AI
blocking. No periodic health check or network-triggered Core restart is added.

# Dedicated DoH for initial server probes

The existing HTTP/HTTPS URL test, primary/alternate URLs, timeouts, concurrency,
result selection and per-server routing stay unchanged. Only the probe box's
DNS resolver changes from OS/local DNS to Google DoH: `8.8.8.8`, `/dns-query`,
verified TLS with SNI `dns.google`. The literal IP needs no bootstrap OS lookup.
With no proxy detour and `auto_detect_interface=true`, DoH uses the physical
network and can resolve server domains before any candidate or Pool is up.
This is deliberately separate from live website DNS racing and live Pool
bootstrap DNS, neither of which changes.

Normal and VOD Xray probes opt into socket normalization and send `ForceIP` in
TestReq field 14. XHTTP upload/download sockets and Hysteria2 use the dedicated
probe box resolver; DNS errors must not silently fall back to OS DNS. The
alternate URL pass retains the same config and strategy. Default generator
calls without this explicit opt-in retain their previous behavior.

Checks cover typed TLS/SNI preservation, independent DoH configuration,
Hysteria2 and XHTTP transports, exact field-14 bytes, real normal-test dispatch,
and alternate-pass policy retention. Core support was checked at
`iransearch/Throne-G` commit `5cc83398864bfea713efd0e43996c518afb83761`.
No new Core build is required. Older cores may ignore field 14.

`IRSPEEDY_PROBE_DNS_FIXTURE=/absolute/path/probe.json` exports the DNS profile
when running this harness for strict schema/DoH transport checks against the
pinned sing-box source. Windows acceptance still requires the packaged client,
including a DoH failure and a network switch. These checks do not prove that
Google DoH is reachable from every user's physical network.

```sh
GO=/path/to/go python tests/HysteriaXrayChecks/run-probe-doh-core.py /path/to/sing-box /absolute/path/probe.json
```

This uses the real HTTPS transport, direct dialer, verified TLS/SNI and DNS wire
messages with a local test server; it validates successful resolution and DoH
failure handling without contacting public services.
