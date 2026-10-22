# XHTTP header import and serialization (1.4.31)

Run `python tests/XhttpHeaderChecks/run.py [dotnet]` with the .NET 8 SDK.
Add `--generators` to run the complete HysteriaXrayChecks suite with the production
formatter wired into its configuration serialization as well.

The production VLESS/transport parser and Utils.FormatJson methods are copied
verbatim into the harness. The tests run the production Xray config generator and final
AutoSelectorPoolPlan SOCKS-bridge generator. Unrelated importers, WPF, registry
and JSON serialization dependencies use the existing HysteriaXrayChecks fixtures.
The framework JavaScriptSerializer is represented by Newtonsoft in this .NET 8
harness; its JSON output then passes through the real production FormatJson.

Cases cover the reported nested `headers.headers` Safari value (with synthetic
authentication/address), a regular User-Agent map, plus signs, percent escapes,
quotes, Unicode, nested download headers, ordinary extras and no extra field.
They assert the original JSON and header values survive exactly after one URL
decode, through single-server, probe (including the production `useCoreDns=true`
path) and final one-member Pool configurations.
Header names are not rewritten or flattened. The original report's nested header
map is preserved literally; this fix does not reinterpret it as User-Agent.

The original decode regression failed before removing the second decode and
passed afterwards. Those earlier checks bypassed the production formatter and
did not detect its independent mutation of header values. In the Linux validation environment these checks and the
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

The 1.4.30 Windows log reports `http2: invalid HTTP header value for header
"Headers"` during the failed XHTTP tests. Replaying the production formatter with
the reported Safari string reproduces inserted CR/LF inside the value: escaped
quotes incorrectly toggle its quote counter and a literal comma is treated as
JSON punctuation. Braces/brackets inside strings were also treated as structure.
The corrected formatter formats parsed JSON tokens with date parsing disabled,
retaining the existing JavaScriptSerializer and NullPropertiesConverter policy.
It does not flatten/reinterpret headers or remove user-supplied control characters.

The regression fails against the old formatter and passes after the fix. Cases
also cover escaped quotes/backslashes, literal punctuation, strings resembling
dates, numeric/boolean literals, nested download headers, and both probe DNS
paths. These are configuration/diagnostic tests; a native Windows/net48 build and
live VPN connection still require validation on Windows.
