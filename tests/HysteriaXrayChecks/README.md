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
