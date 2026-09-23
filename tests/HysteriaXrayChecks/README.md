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
