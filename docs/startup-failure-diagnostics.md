# Connection start failures (1.4.37)

Connection failures display only `خطا در اتصال به سرویس`. Empty disconnects and
the existing empty-server search status remain silent. The Core details link,
technical popup, copy button and tooltip have been removed from the banner.
Core identity and technical failure information remain in the local report.
The existing identity RPC's privilege field is also retained as `Core privileged`
in that report; this adds no extra network request or Core protocol change.

`core-start-config-error` now records the actual Start RPC response with fixed
metadata and sanitized context. Preflight, routing construction and RPC failures
also identify their startup phase. Fields:

- `reason`: existing error family, such as `socket-access-denied`.
- `component`, `inboundType`, `operation`, `network`: bounded categories parsed
  from the returned error, separating TUN/driver, network monitor, DNS, Xray and
  local proxy failures where the response identifies them.
- `localPort`, `bindScope`: only a valid literal local listen/bind endpoint is
  summarized. Its address is never included. Remote dial endpoints are excluded.
- `nativeError`, `codeSource`: a reported number or explicit Winsock symbol.
  Missing codes stay `-1`/`unavailable`; access-denied text does not imply 10013.
- `messageId`, `errorChars`, `errorTruncated`: correlation and input bounds.
- `text`: a bounded vocabulary-only rendering, with credentials, URLs, addresses,
  inbound tags and arbitrary configuration strings removed.

Unrecognized response details remain `unknown`; this patch does not infer a
reserved port or change routing, firewall, listener allocation or TUN settings.
The normal report filters retain these error-stage events. Existing build/PID,
mode, connection and config fingerprints correlate them with startup state.

Rebuild the Windows app. No Core source update is required for these diagnostics;
the response comes from the existing Start RPC. Current DNS-enabled Windows
builds still require the previously supplied compatible Core payload.

Validation: `tests/CoreIdentityChecks` covers socket/TUN/driver/error metadata and
the exact display text; `tests/ConnectionDiagnosticChecks` exercises the full
report pipeline and privacy; `tests/CoreIdentityChecks/ui_compile.py` verifies
the actual banner method and XAML against WPF/net48 references.
