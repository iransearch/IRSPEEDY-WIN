# XHTTP header import and preservation (1.4.30)

Run `python tests/XhttpHeaderChecks/run.py [dotnet]` with the .NET 8 SDK.

The production VLESS and transport parser methods are copied verbatim into the
harness. The tests run the production Xray config generator and final
AutoSelectorPoolPlan SOCKS-bridge generator. Unrelated importers, WPF, registry
and JSON helper dependencies use the existing HysteriaXrayChecks fixtures.

Cases cover the reported nested `headers.headers` Safari value (with synthetic
authentication/address), a regular User-Agent map, plus signs, percent escapes,
quotes, Unicode, nested download headers, ordinary extras and no extra field.
They assert the original JSON and header values survive exactly after one URL
decode, through single-server, probe (including the production `useCoreDns=true`
path) and final one-member Pool configurations.
Header names are not rewritten or flattened. The original report's nested header
map is preserved literally; this fix does not reinterpret it as User-Agent.

The reported-header regression failed before removing the second decode and
passed afterwards. In the Linux validation environment these checks and the
existing HysteriaXrayChecks were compiled with Roslyn C# 7.3, .NET 8 reference
assemblies and the SDK's available Newtonsoft.Json assembly. This is config-level
validation, not a native Windows/net48 application build or a live VPN connection.

Version 1.4.30 also imports the independent `headers=Name|Value|Name|Value`
query parameter used by Throne. A nonempty explicit list replaces `extra.headers`
and retains all other extra fields. Empty values and reserved characters survive;
an empty query parameter leaves extra headers intact. An incomplete pair list is
rejected instead of silently discarding it. Header names and values are not
heuristically flattened or rewritten. The query-only regression fails against
the 1.4.29 parser and passes against the corrected parser.

Probe failure records now include transport, so XHTTP can be distinguished from
other VLESS/TLS failures. Core HTTP header errors have the fixed classification
`invalid-http-header`; the scrubbed diagnostic retains the header-error phrase
while removing private names and values. ConnectionDiagnosticChecks verifies
that classification and redaction.

Other query values, TLS verification, fingerprint, ALPN, routing, Pool policies
and probe scheduling are not changed. Previously double-encoded extra JSON is not
heuristically decoded again: standard links encode the JSON once.

These tests verify configuration and diagnostics. They do not establish that the
original `extra.headers.headers` link connects or identify its runtime failure.
