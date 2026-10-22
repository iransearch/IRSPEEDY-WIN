# Server list comet (1.4.28)

The WPF/.NET Framework 4.8 picker consumes the existing application ranking as
each country completes its check. It does not wait for every country to finish.
Networking, country aggregation, ranking rules, connection selection, concurrency,
retries and timeouts are unchanged.

## Completion eligibility

`RefreshGroup` records completion using the existing completed-country notification.
A partial ping, cached result or IsChecking=false alone does not grant eligibility.
Starting another check revokes that country's eligibility; beginning a new cycle
or loading a list clears all eligibility. Countries not completed in the current
cycle keep their order during ordinary latency sorting and retain the checking
sheen inside the flag. Connection pinning is an independent priority.

`ServerListViewModel.Servers` holds the authoritative immutable snapshots in the
application's current sorted order. `PresentationOrder()` first extracts the
starred identity to the front, preserving relative order of all other countries.
It then projects completed ordinary countries into their slots below the pin.
The starred country remains first even when pending or actively checking. Pin
changes apply immediately and invalidate active animation targets for the old
pin, so stale callbacks cannot demote the new star. Pending rows may shift down
when a new country is pinned; ordinary latency sorting otherwise leaves them fixed. When only one country has finished there is no eligible position
exchange yet. Any relative-order change among completed countries is presented
without waiting for the rest of the list.

## Direct assignment and unlimited bands

The ItemsControl binds to stable `ServerDisplaySlot` objects. `TryBeginPass()`
compares every slot with the latest presentation target and includes ALL changed
assignments in one frozen pass. It no longer selects a pair, parks a displaced
country in the source slot, or builds the target order through intermediate swaps.
Unchanged assignments update their data normally and receive no light or flash.

For [A,B,C,D] -> [B,C,D,A], all four affected slots target [B,C,D,A] in the SAME
800 ms pass. Each position receives its target once for that snapshot. Multiple
disjoint cycles also share one pass. The number of bands equals the number of
changed realized rows, without a fixed maximum. Bands outside the viewport are
clipped. All bands use the same elapsed-time clock and start together.

Each content group (signal, existing badge, name, flag and selection) retains its
old snapshot until the shared crossing time measured from the physical controls.
The timeline takes the latest crossing across participating surfaces for each
group. Every assignment of that group commits before any binding is notified.
Each group therefore remains a permutation: no vacant source or duplicate country.
`DisplayedServer` and the click identity commit with the name group. Selection
remains attached to the stable identity and is never changed by presentation.

There are no collection Move operations, animated layout dimensions or vertical
row translations. Idle artwork, spacing, text, colors, RTL, flags, icons and the
existing gold connection star are retained. The white content rectangles remain
removed. Only changed content fades from opacity .2 to 1, with no blur or shadow.

## Timing and incoming results

The named constants remain SORT_PASS_MS=800, FLASH_MS=400, BAND_WIDTH_DIP=170,
and SKEW_DEG=-18. Easing is .5-.5*cos(pi*t), with t clamped to [0,1]. Physical
leadingEdgeX=eased*(W+.85*B), bandLeftX=leadingEdgeX-.85*B. RTL transforms are
included in measurement; the light always travels physically left to right.

One UI-owned Stopwatch and CompositionTarget.Rendering callback advance prepared
transforms and opacity. Preparation/measurement occurs outside the frame callback.
The frame callback performs no networking, ranking, flag decoding, I/O or waits.
The existing flag cache remains preloaded/frozen.

Backend publications update/coalesce the latest snapshot without restarting an
active pass or changing its frozen targets. At the next pass boundary, changed
slots are recalculated against the newest order. A position can change again if
newly completed results actually change its rank; it never visits intermediate
countries merely to construct a single fixed target snapshot.

Check-again cancels old presentation and resets existing indicators without a
sweep. The all-finished event starts no additional animation. Reduced-motion,
unload, minimize and shutdown cleanup apply eligible final states immediately,
clear overlays/flashes and unsubscribe rendering. Structural additions/removals
reconcile immediately and invalidate stale callbacks. Animation progress is not
persisted across restarts.

## Validation for 1.4.27

Executed the production planner/timeline fixture with Roslyn C# 7.3, .NET 8
reference types and inert WPF value doubles. Tests cover:

- Immediate three-country update while three other countries are unfinished.
- A fourth completion coalescing during that pass without changing active targets.
- Every unfinished content group staying in its original slot.
- Three- and four-country rotations resolving in one pass without pair staging.
- All 720 six-country permutations targeting each final slot directly in one pass.
- 4096 changed rows in one pass, atomic uniqueness, selection and lifecycle reset.
- Timing, physical crossing order, dropped frames, rapid publication and no-op data.

UI/old-animation and binding contracts passed. The Windows-only WPF script now
includes a real three-band scenario; that script and a full net48 application
build were NOT run here. Native rendering, smoothness and frame rate still need
Windows verification.

## Pin-priority regression validation for 1.4.28

The new production-slot regression failed on 1.4.27 before the fix. It now covers
pinning before any current-cycle completion, a checking star, ordinary sorting
below the pin, pin transfer during partial reveal, stale callback cancellation,
selection/atomic uniqueness, reduced-motion/lifecycle cleanup, recovery of an
already-misordered display, and removal of the pinned country. The native WPF
fixture includes the pending-star case; native Windows execution remains unrun.
