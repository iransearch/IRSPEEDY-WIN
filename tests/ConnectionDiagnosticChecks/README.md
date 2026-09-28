# Connection loss diagnostics (network-core-v2)

This change is observational. It does not add health probes, reconnects, proxy repairs,
core resets or direct fallback. Existing connection and public-IP behavior is retained.

- `proxy-change-begin/end`: operation ID, caller stack (method names only), thread,
  active connection, HKCU settings and WinINet LAN flags before/after every application
  proxy mutation. Native set failures and swallowed disable errors are recorded.
- `runtime-snapshot`: sampled every two seconds, emitted on change or every 15 seconds;
  active service state, actual registry/WinINet configuration, tracked core exit status,
  IPv4/IPv6 listener owner PIDs/bind types and core TCP state counts. No socket probes.
- `network-event/network-snapshot`: event order, interface status and route changes.
- `core-output/core-detail`: active service identity is separate from the process reader;
  safe diagnostic vocabulary retains error causes, unknown tokens/URLs/config values
  are removed. Separate per-category budgets keep route chatter from hiding errors.
  Suppressed counts and the last 40 sanitized lines on process exit are recorded.
- `process-kill-request/core-stop-rpc-begin/core-exit/core-exit-decision`: intentional
  stops, exit codes, and the actual existing lifecycle decision after a process exit.
- `ui-connection-callback/ui-connection-dispatch/ui-navigation`: queued UI delay,
  obsolete callback checks, requested connection result and displayed screen.
- `public-ip-*`: the existing explicit proxy request, HTTP result, cancellation/error,
  and a session-salted fingerprint of the displayed address. No extra IP requests.

Records have event UTC, monotonic time, session/build IDs and sequence numbers captured
before asynchronous writing. The queue is bounded and reports overflow. Logging failure
must not affect the connection. A forcibly terminated application may lose queued records.

`dotnet run --project tests/ConnectionDiagnosticChecks` checks active versus idle reader
state, rate-budget isolation, safe text, slow/failing sinks and compiles the proxy wrappers.
Windows native queries and WPF behavior still require a Windows build/run.

For reproduction: run the normal single-file builder, connect, disconnect/reconnect Wi-Fi,
and retain `log.txt` through the failure and at least 15 seconds afterwards. Note the time
and whether the UI still shows connected. Do not delete the log before collecting it.
A proxy change with no matching application mutation can identify an external/unobserved
change, but these passive logs alone cannot name the external process responsible.
