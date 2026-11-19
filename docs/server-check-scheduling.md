# Display server checks

Login, cached startup, disconnect, manual refresh and Residential use the
same display scheduler. An API row (including a numbered row for a repeated
country code) owns its own route queue. This does not alter the Smart Pool,
Auto Selector, connected latency, or runtime VPN health checks.

- Shuffle the routes separately from presentation. Among a row's candidates,
  try measured failures from its previous completed scan first. Admit at most
  eight probes, and at most `ceil(8 / active rows)` per eligible row.
- A result below 300 ms skips that row's waiting routes. Already-running
  probes finish. Choose the lowest positive ping, then fewer failures, more
  samples, and case-insensitive server ID. Skipped routes are never failures.
- The eight-second budget includes preparation and RPC startup. A distinct
  API target gets up to four seconds for the first attempt; retry a failure
  or a measurement above 500 ms once using
  `http://connectivitycheck.gstatic.com/generate_204` and the remaining budget.
  Keep the better success. With no API target, use the fallback directly.
  Capacity or an interrupted individual attempt is deferred for 250 ms.
- A scan uses a temporary Throne Core separate from the active VPN process.
  The display RPC uses two requests on one warmed connection for ordinary
  transports, and one request for XHTTP/SplitHTTP. Windows prepares its
  temporary outbound again for a fallback attempt, within the same deadline.
  Cisco continues to measure its own HTTPS endpoint, under the same scheduler,
  because the Cisco adapter does not expose an outbound tunnel for display probes.
- Cache a completed country's winner or no-result, and measured failure
  fingerprints, without an elapsed-time expiry. Account, target, and the
  full roster's configuration identities gate reuse. An API reorder does
  not invalidate a result. The first round after upgrade cannot reuse old
  snapshots whose test target was never recorded. Persist only fingerprints,
  not raw links or credentials.
- Every explicit round, including a manual refresh, leaves valid successes
  alone and retries failed, missing, incomplete or changed rows. Manual
  refresh still fetches a fresh catalog and can use the current catalog if
  that fetch fails. Connect/logout cancel and drain the scan. A ping measured
  before cancellation can be restored, but an incomplete country cannot
  satisfy next-round eligibility. A canceled or skipped probe records no failure.
- Preserve the existing picker, pinning, progress and Residential ping
  formatting. Rank completed successes by displayed ping then title, keep
  outstanding rows between successes and completed failures.

## Core and verification

The separate source patch is
[`core-patches/throne-display-probes.patch`](../core-patches/throne-display-probes.patch).
Apply to `iransearch/Throne-G` at `c3802d34`:

```sh
git am /path/to/IRSPEEDY-WIN/core-patches/throne-display-probes.patch
cd core/server
go test -race ./test_utils -run TestDisplay
```

Build and package the matching Windows Core using Throne-G's normal build
pipeline. The app repository has no updated Core binary. An older Core
without the display RPC stops the scan without marking rows failed.

Run `dotnet run --project tests/DisplayProbeChecks` for the offline scheduler,
deadline, fallback, cancellation and cache checks. Windows runtime verification
still requires the packaged Core. The historical display scheduling relationships in
the codegraph are stale for this change; use these sources for the display flow until
a reviewed graph snapshot records the new call paths.

## Probe indicator
Each numbered row starts a clipped diagonal flag sheen before its RPC starts, and clears it in a UI-thread finally block, including cancellation and errors. Partial latency updates do not end the sheen. The sheen uses the smart flag’s white gradient, skew and 3.2-second easing cycle; clocks are removed when hidden/unloaded/minimized. Removed the checking/failure subtitle; cached latency and its history tooltip remain.

### Frozen-template crash correction
The 2026-09-25 Windows crash occurred in UpdateProbeShine: WPF had frozen the DataTemplate's TranslateTransform, and even BeginAnimation(XProperty, null) cannot mutate it. ProbeShift now clones a frozen group or frozen child graph onto the specific element before either starting or removing animation. Mutable instances are reused. This also covers the unload path.

Validation: tests/ProbeShineChecks/run.py exercises production ownership logic using portable transform doubles. tests/ProbeShineChecks/run-wpf.ps1 exercises the production probe methods against actual WPF frozen transforms, animation and unload (run with Windows PowerShell -STA). The latter requires Windows and was not run in the Linux workspace.
