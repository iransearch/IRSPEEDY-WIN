# IRSPEEDY-WIN regression test inventory — phase 1

Source branch: `agent/core-startup-diagnostics-fix`. This inventory groups the **39 tracked `tests/*` suites** by *required test environment*, not by module names or test-file count.

**Important:** Classification is an operational starting point. Many directories contain mixed offline and platform-specific checks; where they do, the folder is assigned by its more demanding path, and any offline part can be promoted separately after verification. A test that is not executed is **NOT RUN**, never PASS. The first gate deliberately executes only the six Linux/.NET8 fixture-based harnesses below. A passing phase-1 job does not certify a Windows executable, WPF rendering, VPN connection, production Core, or backend token revocation.

## Fast — offline or fixture-based (Linux-capable; only selected suites run)

| Directory | Phase-1 CI | Scope / notes |
| --- | --- | --- |
| `LoginRecoveryChecks` | **RUN** | Production login failover/DNS and POST replay safeguards with fake DoH and loopback HTTPS; also invokes password re-login and login presentation sub-harnesses |
| `ConnectionTestChecks` | **RUN** | Connection-test UI/network lifecycle with inert doubles, not a live tunnel |
| `AiStartupChecks` | **RUN** | Auto-selector startup timing with fake Core transport |
| `LastSuccessfulServerChecks` | **RUN** | Last-successful pin and picker ordering |
| `ServerListTransitionChecks` | **RUN** | Offline planner/ordering; its Windows `run-wpf.ps1` is not executed |
| `DeviceLimitChecks` | **RUN** | Removal/logout handlers with UI/network doubles; no live account |
| `CodegraphChecks` | Separate existing workflow | Graph maintenance; not a substitute for application regression |
| `ConnectedLatencyChecks` | NOT RUN | Candidate offline suite; verify prerequisites |
| `ConnectionTlsChecks` | NOT RUN | Loopback TLS fixtures; privileged-port and CA behavior requires an isolated runner review |
| `ErrorReportingChecks` | NOT RUN | Offline error and RPC harnesses; validate prerequisites |
| `LoggingChecks` | NOT RUN | Log-retention fixture; ensure output isolation |
| `LoginPresentationChecks` | RUN indirectly | Invoked by LoginRecovery harness, not a standalone CI job |
| `PasswordReloginChecks` | RUN indirectly | Invoked by LoginRecovery harness, not a standalone CI job |
| `ProbeShineChecks` | NOT RUN | Offline visual/logic checks; native WPF path deferred |
| `ServerRefreshChecks` | NOT RUN | Offline scheduler but temporary project references Newtonsoft.Json restore; pin/contain dependency first |
| `ServerStarGlowChecks` | NOT RUN | Python portion candidate; native WPF portion deferred |
| `SharingUiChecks` | NOT RUN | UI contract checks; prerequisite verification required |
| `ThemeChecks` | NOT RUN | Python theme contracts candidate; native WPF part deferred |
| `SharingMotionChecks` | NOT RUN | Motion/animation fixtures; native WPF behavior separately deferred |
| `UrlTestRetryChecks` | NOT RUN | C# retry checks, candidate for next CI tier |
| `XhttpHeaderChecks` | NOT RUN | Production XHTTP parser fixture; validate NuGet and dependencies |
| `TimeZoneChecks` | NOT RUN | .NET test project, environment review |
| `TrafficChecks` | NOT RUN | Offline collector/RPC loopback fixture; candidate after dependency review |
| `PasswordChangeChecks` | NOT RUN | Password-change test project; validate external assumptions |
| `ConnectionStartChecks` | NOT RUN | Startup/URL cancellation C# project; verify SDK and refs |
| `NetworkRecoveryChecks` | NOT RUN | Selected offline slices possible; some tests require net48 references or Windows |
| `HysteriaXrayChecks` | NOT RUN | Generator/AutoSelector fixtures; native Core probes separated before running |

## Windows — real Windows/WPF/OS integration required

| Directory | Phase-1 CI | Reason |
| --- | --- | --- |
| `CoreIdentityChecks` | NOT RUN | Windows WPF/UI compile and startup identity checks |
| `DirectSharingAvailabilityChecks` | NOT RUN | OS sharing/network state behavior |
| `ExitChecks` | NOT RUN | Native shutdown/desktop state path |
| `HotspotChecks` | NOT RUN | Windows hotspot implementation |
| `HotspotPayloadChecks` | NOT RUN | Windows payload/PowerShell validation |
| `PublicIpChecks` | NOT RUN | System proxy/Windows behavior |
| `SplitTunnelChecks` | NOT RUN | Windows routing/OS boundaries |

## Integration — native Core, network/OS, or multi-component verification

| Directory | Phase-1 CI | Reason |
| --- | --- | --- |
| `ConnectionDiagnosticChecks` | NOT RUN | Diagnostics and real operating environment not release-certified by offline fixtures |
| `HotspotIntegrationChecks` | NOT RUN | Explicit hotspot integration |
| `ParallelDnsChecks` | NOT RUN | Includes native DNS/Core probe variants |
| `ProxifierQuicChecks` | NOT RUN | Native WFP/QUIC behavior |
| `ResidentialChecks` | NOT RUN | Multi-component net48/WPF and HTTPS fixture; isolated runner required |

## Execution policy

- PR base and Push target: **only** `agent/core-startup-diagnostics-fix`; **never** `main`.
- GitHub Action: `.github/workflows/regression-guard.yml`, read-only `contents: read`, no credentials/secrets passed to tests, Ubuntu 24.04 + .NET 8.
- Six selected suites run sequentially. The harness must exit nonzero on *any* failure, missing prerequisite or timeout. Reports list each actual suite result, duration and explicitly deferred live categories. Nested suites are noted as such and do not count as standalone jobs.
- No Windows desktop, production Core, customer credentials, TLS trust-store modifications, live API, real VPN connection, or release/packaging tests are performed in phase 1.
- The classification is **not** a percentage coverage metric; actual source-under-test coverage and live safety require later measurement and Windows/Core gates.
- **Branch protection** still needs separately configured required status checks. A passing check alone does not guarantee that a PR cannot be merged.
- No VPN runtime or security code is modified by this CI addition.

## Next steps (not included in phase 1)

1. Configure branch rules requiring `Fast offline regression (Linux .NET 8)` and existing Codegraph checks after the PR is reviewed.
2. Move additional isolated offline suites into CI after proving no internet dependency, no trust-store modification and repeatability.
3. Add a Windows runner for actual `net48` WPF build, BAML, visual and native lifecycle checks; don't label these completed based on Linux.
4. Add a controlled packaged-Core/integration smoke test environment without real user subscriptions.
