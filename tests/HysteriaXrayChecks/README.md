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

The Smart regression also generates a seven-member Hysteria2 pool and checks
that its routing has no `geoip:`, `geosite:` or `ext:` database selectors. Local
IPv4/IPv6 and hostname bypass is inline; Iran routing remains in the outer
sing-box `ir_IP` / `category-ir_SITE` SRS rules. It also checks the leastLoad
strategy, absence of fallback, 30-minute interval and two probe samples.

This fixes the `failed to open geoip.dat` failure of the single-file runtime.
No DAT download or external file next to the application is needed. The existing
`V-Guard/geo/*.srs` runtime payload is still required by sing-box.

Inline local-list sources (snapshot 2026-09-23):

- https://github.com/v2fly/geoip/blob/master/plugin/special/private.go (blob `598c971199812e817869623f2f4a4ea78014e978`)
- https://github.com/v2fly/domain-list-community/blob/master/data/private (blob `b8570db79fb0077aacfc10dbac646e57e7c47446`)
