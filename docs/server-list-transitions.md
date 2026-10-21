# Server list comet (1.4.21)

The existing .NET Framework 4.8 WPF picker consumes the application's existing
country order immediately after each completed country check. Its aggregation,
ranking, probe execution and connection selection rules are unchanged. Partial
results update the existing indicators in place; they do not start a sweep.

## Presentation

`ServerListViewModel.Servers` holds immutable `ServerItem` snapshots in the latest
real order. The ItemsControl binds only to stable `ServerDisplaySlot` objects.
Each slot has five separately revealed snapshots: signal, existing connection
badge, country name, flag and selection. No visual collection Move or row
translation occurs. `DisplayedServer` (including the click identity) commits
with the country-name group; clicks still resolve against the live group/health.
The actual selected service is never changed by the animation.

`ServerListSweepAnimator` owns one monotonic Stopwatch, one active pass and one
CompositionTarget.Rendering subscription. `ServerListCometOverlay` draws exactly
one noninteractive band across the entire list. Only an assigned identity change
gets a pass. Same-position ping, selection and star updates bind normally without
flashing. Check-again reset preserves the visible order and resets existing
indicators without a band. The all-finished event starts no presentation work.

Constants in `ServerCometTimeline.cs` are `SORT_PASS_MS = 800`, `FLASH_MS = 400`,
`BAND_WIDTH_DIP = 170`, and `SKEW_DEG = -18`. With clamped elapsed fraction t:

```
eased = 0.5 - 0.5 * cos(pi * t)
leadingEdgeX = eased * (W + 0.85 * B)
bandLeftX = leadingEdgeX - 0.85 * B
```

The frozen cyan/white horizontal gradient has stops at 0, .30, .68, .86, .90,
.96 and 1, with the requested long tail and sharp bright edge. The static skew
and rounded row/viewport clips are prepared before each pass. The existing
card's actual corner radius is read from its template. No blur or shadow is
added. Idle row artwork, text, spacing, RTL, palette and existing icons remain
unchanged.

Before each pass, existing content groups are measured through TransformToVisual
into the physical LTR overlay. Inherited RTL transforms are included. The timeline
sorts these measured centers and calculates crossing times using the inverse
cosine easing. Each group retains its old snapshot until the leading edge
crosses its center, then commits once. Dropped frames process every crossed
group. Changed content fades from opacity .2 to 1 over 400 ms with a brief white
brightness overlay; unchanged content gets no flash. Flash start times use the
logical crossing time, so late frames do not extend the effect.

Three changed visible positions require three sequential 800 ms passes (about
2.4 seconds, plus normal dispatcher/frame scheduling). There is no stagger or
waiting period between passes. A final group's 400 ms flash can finish after its
band has left; there is still only one band.

## Coalescing and lifecycle

An active pass's target stays frozen. New notifications replace the latest
pending order without restarting the clock. At every pass boundary preparation
scans current slots from the first mismatch against that newest order. Matching
positions cost no animation time. Before any destination group reveals an
identity, its previous source is concealed. Vacancies keep the card's dimensions,
background and disabled appearance, and cannot be refilled from stale snapshots.
Partially committed rows are likewise protected from ordinary publications.

A new explicit check cycle cancels presentation, applies the newest authoritative
data and invalidates old callbacks before resetting checking states. The manual
refresh and explicit disconnect/restart paths both use this boundary. Canceled
old background checks retain their existing data cleanup but cannot request a
new sorting effect. Search/navigation/loading uses instant presentation.

Unrealized/offscreen rows drain synchronously in an iterative preparation loop.
Recycling is checked before revealing or flashing a realized row. Empty/single
lists and additions/removals use the same model. Scroll changes refresh cached
clips/bounds without restarting time. Resize, hide, minimize, unload, close and
shutdown apply final real data and remove presentation work. OS client-area
animation disabled applies completed orders immediately; resetting a check still
preserves visible order. There is no new app setting or persisted progress.

Backend notifications only publish/coalesce snapshots. Preparation, measurement
and replanning run on a separate dispatcher turn, outside Rendering. The frame
callback reads only prepared presentation state and elapsed time, commits crossed
groups and updates transforms/opacity. It never runs probes, reads tester state,
sorts, decodes flags, accesses disk, waits for workers/locks or uses synchronous
Dispatcher.Invoke. Rendering unsubscribes when no pass, flash or preparation is
pending. Existing OnLoad/frozen flag caching and HighQuality scaling are retained.

## Removed animation paths

- Deleted `LightSweepRowTransition.cs` and `LightSweepAnimationPlan.cs`: old native
  storyboards, dual presenters/complementary clips and 150 ms data crossfades.
- Replaced the old `ServerRowTransition` and VM acknowledgment/promotion queue
  with stable group snapshots and latest-order boundary replanning.
- Removed probing-flag sheen markup, ProbeShine handlers and their perpetual clocks.
- Removed the smart-card moving flag-mosaic sheen and animated dashed shine border,
  including their geometry/size handlers and perpetual storyboard.
- Removed the star's obsolete transition-surface guard. Its existing gold artwork
  and safe Loaded/visibility glow lifecycle remain; no new badge was introduced.

No lift/slide/scale, checking pass, pending pulse, ping-only reveal, stagger or
whole-list completion pulse remains in the server-list sorting path.

## Validation

Portable checks (C# 7.3, model/timeline with inert WPF value doubles):

```
python3 tests/ServerListTransitionChecks/run.py /path/to/dotnet
python3 tests/ServerListTransitionChecks/contracts.py
python3 tests/ServerListTransitionChecks/bindings.py
python3 tests/ServerRefreshChecks/run.py /path/to/dotnet
python3 tests/ServerRefreshChecks/manual_refresh.py /path/to/dotnet
python3 tests/ServerRefreshChecks/api_refresh.py /path/to/dotnet
python3 tests/LastSuccessfulServerChecks/run.py /path/to/dotnet
python3 tests/ServerStarGlowChecks/run.py /path/to/dotnet
python3 tests/ThemeChecks/run.py
python3 tests/ServerRefreshChecks/net48_compile.py /path/to/dotnet
```

The model/timeline checks cover incremental completion, reset order including
reduced motion, group reveal timing, stable selection, one active pass, source
vacating, 200 coalesced publications, rapid randomized reorderings, stale
callbacks, additions/removals, empty/single lists, 4096 unrealized passes, exact
geometry/easing and dropped frames. Contracts compare idle row artwork against
1.4.20 and inspect removed effects, bindings and the backend-free frame path.
The net48 check compiles actual picker/controller/refresh C# against real WPF
reference assemblies with application/Core doubles. It does not build BAML or
produce a Windows app executable.

Windows-only compiled-template smoke test, using an unobfuscated app build:

```
powershell.exe -NoProfile -STA -File tests/ServerListTransitionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
powershell.exe -NoProfile -STA -File tests/ServerStarGlowChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
```

The comet smoke test exercises the production picker, existing country aggregation
on fixture URL data, actual overlay coordinates, measured group commits, active
pass freezing, reset, selection and minimize/close cleanup. Fixture service
constructors and VPN/probes are not run. Native WPF tests have not been run in
the Linux implementation environment. Rendering correctness, input/scrolling,
DPI/RTL appearance and frame pacing still require Windows acceptance. Test both
themes at 100/150/200% DPI, rapid rechecks, search, added/removed rows, minimized
windows and the OS reduced-motion preference. Profile a low-end Windows device
before making a 60 FPS claim.
