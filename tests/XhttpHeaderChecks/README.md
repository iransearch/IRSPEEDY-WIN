# XHTTP extra/header preservation (1.4.29)

Run `python tests/XhttpHeaderChecks/run.py [dotnet]` with the .NET 8 SDK.

The production VLESS and transport parser methods are copied verbatim into the
harness. The tests run the production Xray config generator and final
AutoSelectorPoolPlan SOCKS-bridge generator. Unrelated importers, WPF, registry
and JSON helper dependencies use the existing HysteriaXrayChecks fixtures.

Cases cover the reported nested `headers.headers` Safari value (with synthetic
authentication/address), a regular User-Agent map, plus signs, percent escapes,
quotes, Unicode, nested download headers, ordinary extras and no extra field.
They assert the original JSON and header values survive exactly after one URL
decode, through single-server, probe and final one-member Pool configurations.
Header names are not rewritten or flattened. The original report's nested header
map is preserved literally; this fix does not reinterpret it as User-Agent.

The reported-header regression failed before removing the second decode and
passed afterwards. In the Linux validation environment these checks and the
existing HysteriaXrayChecks were compiled with Roslyn C# 7.3, .NET 8 reference
assemblies and the SDK's available Newtonsoft.Json assembly. This is config-level
validation, not a native Windows/net48 application build or a live VPN connection.

The fix is limited to XHTTP `extra` in ShareHandler.ResolveStdTransport. Other
query values, TLS verification, fingerprint, ALPN, routing, Pool policies and
probe scheduling are not changed. Previously double-encoded extra JSON is not
heuristically decoded again: standard links encode the JSON once.
