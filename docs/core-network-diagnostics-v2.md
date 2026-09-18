# Reading Core reset diagnostics

Use the instrumented Core from `iransearch/Throne-G`, branch
`update/fc668b60-core-only-1.3.0-beta.1`, with this Windows reader. Rebuild Core
using the established production build and package it as the application's Core
in `IRSpeedyVPN/Resources/Files.zip`. Rebuilding the WPF application alone is not
sufficient. This change does not replace bundled binaries or deploy a release.

`[CoreDiagnostic] schema=core-network-v2` lines are validated and recorded as
`core-detail`. Only recognized fields, fixed enums, numeric values and 16-digit
hashes are accepted. Raw configuration, errors, URLs and credentials are not
copied from Core stdout/stderr. Existing unstructured output keeps its category
and salted fingerprint; routine route/TUN/error output no longer requests an
additional network snapshot.

Use these joins when investigating a failed probe:

| Windows field | Core field | Meaning |
| --- | --- | --- |
| `corePid` / `readerPid` | `pid` | Core process |
| `coreConfigId` | `config` on `test-environment` | Test configuration fingerprint |
| `coreTagId` | `tag` | Outbound tag fingerprint |
| Timestamp and request context | `box`, `test`, `outbound`, `monoMs`, `seq` | Lifecycle/probe correlation |

Fingerprints use the first eight SHA-256 bytes, lowercase hex. They are not unique
request IDs: repeated tests can use the same configuration and tag. Follow the
Core box/test identities and time as well, particularly for concurrent requests.

`hy2-reset reason=interface-update` identifies a reset called from the Core's
interface-update handler. It does not by itself prove a physical network change.
`power-event` identifies the Windows suspend/resume path. Other reset callers
remain explicitly classified as `network-manager-reset` or `other-caller`.
`hy2-close` records normal outbound close separately. `box-close`, `rpc-stop`,
`rpc-stoptest`, `tests-cancel`, and probe context state help locate cleanup or
cancellation. No reset/retry/timeout behavior is changed.

The shared PID registry records processes launched by any service instance.
Reuse can discover the owner of the loopback IPv4 TCP listener with a read-only
Windows API call. It never transfers process ownership or adds exit handlers.
An unavailable PID remains -1 rather than a guessed value.

Core emission is bounded and asynchronous. Check `dropped` for queue saturation;
process shutdown can also lose the final queued records. The process-wide
`default-interface` event is independent of each box's monitor.

Focused checks cover schema rejection, control characters, cross-language
fingerprints, shared PID lookup, and existing privacy/failure isolation. The
changed diagnostic C# code also compiles against .NET Framework 4.8 references.
Windows runtime testing is still required for listener ownership, actual
suspend/resume, and reproducing the original Hysteria2 failures. These diagnostics
provide evidence for the next fix; they do not claim to fix the connection error.
