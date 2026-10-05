# Connection loss diagnostics (network-core-v2)

This change is observational. It does not add health probes, reconnects, proxy repairs,
core resets or direct fallback. Existing connection and public-IP behavior is retained.

Version 1.4.6.7 also records bounded, event-triggered Core pool/flow snapshots.
See [the connected/no-data report](../../docs/diagnostics/connected-no-data-2026-10-05.md)
for fields, privacy, rate limits and the next Windows reproduction.

- `proxy-change-end` and failures: operation ID, caller stack (method names only), thread,
  active connection, HKCU settings and WinINet LAN flags before/after every application
  proxy mutation. Native set failures and swallowed disable errors are recorded.
- `runtime-snapshot`: sampled every two seconds, written only on change;
  active service state, actual registry/WinINet configuration, tracked core exit status,
  IPv4/IPv6 listener owner PIDs/bind types and core TCP state counts. No socket probes.
- `network-event/network-snapshot`: event order, interface status and route changes.
- `core-output/core-detail`: active service identity is separate from the process reader;
  safe diagnostic vocabulary retains error causes, unknown tokens/URLs/config values
  are removed. Separate per-category budgets keep route chatter from hiding errors.
  Ordinary routes and successful per-probe details are filtered. Errors in the
  sanitized process-exit tail, interface changes, resets and pool readiness are retained.
- `process-kill-request/core-stop-rpc-begin/core-exit/core-exit-decision`: intentional
  stops, exit codes, and the actual existing lifecycle decision after a process exit.
- Failed `ui-connection-callback` results are retained; routine navigation and
  successful callback messages are filtered.
- `public-ip-*`: the existing explicit proxy request, HTTP result, cancellation/error,
  and a session-salted fingerprint of the displayed address. No extra IP requests.

Records have event UTC, monotonic time, session/build IDs and sequence numbers captured
before asynchronous writing. The queue is bounded and reports overflow. Logging failure
must not affect the connection. A forcibly terminated application may lose queued records.

`dotnet run --project tests/ConnectionDiagnosticChecks` checks active versus idle reader
state, rate-budget isolation, safe text, slow/failing sinks and compiles the proxy wrappers.
Windows native queries and WPF behavior still require a Windows build/run.

The harness additionally checks the production read-only RPC path, unsupported
queries, deadlines/cancellation, late responses, safe pool/flow formatting,
Windows/native socket error preservation and snapshot throttling.
Run `python3 tests/ConnectionDiagnosticChecks/net48_compile.py /path/to/dotnet`
for the production diagnostics/RPC compatibility build against real net48 APIs.

For reproduction: run the normal single-file builder, connect, disconnect/reconnect Wi-Fi,
and collect `log-yyyy-MM-dd.txt` from `%LOCALAPPDATA%\IRSpeedyVPN\Logs\`
through the failure and at least 15 seconds afterwards. The current date and previous
six calendar dates are retained; older daily files are deleted on the next logged write.
Note the time
and whether the UI still shows connected. Do not delete the log before collecting it.
A proxy change with no matching application mutation can identify an external/unobserved
change, but these passive logs alone cannot name the external process responsible.
# AI routing diagnostics

Smart configurations now enable Xray info messages (access logging stays off).
The application extracts generated outbound tags and request IDs before redacting
core text. ShowIP routes, failed AI probes and empty pool selections are retained;
routine AI routes are filtered. Category budgets prevent route chatter from hiding errors.
Other destinations are represented only by session fingerprints.

Read `ai-setting-saved`, `config-apply-result`, and `core-output` together.
`activeXrayConfigId` identifies the last successful Xray configuration for the
active service, even when an idle service owns Core's output reader.
An AI rule in the configuration is not evidence that a request selected an AI
member. `decision=default-route target=showip` identifies use of Xray's default
handler; a nearby empty-pool message alone cannot identify which pool failed.
