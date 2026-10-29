# Sentry Core RPC diagnostics — Windows 1.4.61

Core failures now carry structured context under `diagnostic.schema=core-rpc-v1`.
An example event message is `Core start RPC start at read-header: retry-failed`. The event message
and fingerprint use fixed command/stage/outcome aliases, independent of obfuscated
method names. Existing HTTP reports retain `http-context-v2`.

| Fields | Meaning |
| --- | --- |
| `core.operation`, `core.operation_id`, `core.connection_id` | Requested start/test flow and random correlation IDs; IDs are excluded from grouping. |
| `core.rpc`, `core.rpc_stage` | Actual RPC alias and blocked phase: connect, write-header/body, read-header/body, decode, server-error or complete. Unknown method strings become `other`. |
| `core.rpc_budget_ms`, `core.rpc_elapsed_ms`, `core.close_reason` | Native RPC deadline/elapsed time and whether a deadline, cancellation or transport failure closed it. Budget zero means no explicit RPC deadline, not a zero-millisecond timeout. |
| `core.lifecycle_stage` | Ensure listener, stop-before-start, start or test. A Stop timeout cannot masquerade as a blocked Start command. |
| `core.attempts`, `core.attempt1_ms`, `core.attempt2_ms`, `core.retry_prepare_ms`, `core.total_ms` | Completed/in-flight attempt count and timings for the wrapper invocation, including retry preparation. This is not the total UI click-to-connect duration. |
| `core.outcome` | `failed`, `retry-failed` or `retry-succeeded`. A retry preparation failure still records one actual RPC invocation. |
| `core.first_*` | First failure classification/HRESULT, socket/native code when present, and original RPC stage/closing cause. The final exception chain still describes the final failure. |
| `core.runtime_version`, `core.runtime_commit`, `core.runtime_arch`, `core.runtime_sha256` | Validated identity of the running Core captured before startup; inaccessible identity remains unknown/omitted. `core.runtime_pid` identifies that snapshot; `core.runtime_matches_process` detects a replacement during retry. No executable path is exported. |
| `core.process_state`, `core.pid`, `core.exit_code`, `core.owned` | Observed process state before failed-start cleanup. Unavailable state is explicit. These values are diagnostic and do not grant process ownership. |
| `core.config_id`, `core.xray_config_id`, `core.need_xray`, `core.main_members`, `core.ai_members` | Existing salted HMAC configuration fingerprints, routing-shell presence and pool counts. No configuration text or links. |
| `core.cancel_requested`, `core.connection_generation`, `network.start_seq` | Connection cancellation/generation and network sequence at operation start, alongside the existing selected/observed mode and network end sequence. |
| `core.last_output_category`, `core.last_output_reason`, `core.last_output_age_ms` | Optional fixed classification of a matching main-process signal in the same connection generation; never the raw output line. |
| `core.failure_reason` | Fixed classification of a rejected Start response, without its raw error/configuration content. |
| `exception.handled` | `true` for a normal logged exception, `false` for the existing fatal-report path. It is not inferred from the inner socket error. |

The initial timeout is retained locally while the existing retry runs. A recovered
RPC is reported once as a handled warning; a terminal failure reaches the existing
caller once as an error with both attempt histories. Startup defers the warning
until the Start payload is accepted: a returned error payload becomes a failure,
not a recovered-success warning. User cancellation during retry is kept separate.
The startup/connect deadlines, retry count/delay, Core/routing/Shield and UI error
messages are unchanged.

Only closed-vocabulary, numeric, GUID, semantic-version and hash fields are allowed.
Both annotation/import and Sentry's final before-send pass validate them. Raw
Exception.Data, exception messages, executable paths, URLs, IPs, credentials,
headers, request/response bodies, configuration and raw logs are not attached.
Unknown/fabricated metadata is discarded. The existing per-process reporting
bounds remain; deduplication distinguishes command, phase and retry outcome so a
recovered warning cannot suppress a later terminal error. Correlation IDs and
configuration hashes do not fragment Sentry issue grouping.

Offline tests use real loopback RPC sockets and Sentry's exact 6.11.0 assembly,
without initializing its SDK or sending telemetry. Production reporting, RPC and
retry/startup methods compile against real .NET Framework 4.8 references. Verify
on Windows that a stalled Start produces the expected stage/budget, cancellation
still exits promptly, a successful retry is a warning, and a rejected payload is
an error. Confirm actual receipt and filtering in Sentry using the existing client
verification flow. Already-received 1.4.33 events cannot be enriched retroactively;
the client must be updated to 1.4.61 or later.
