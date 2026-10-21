# Server list paired comet (1.4.23)

The existing .NET Framework 4.8 WPF picker consumes the application's existing
country order immediately after each completed country check. Its aggregation,
ranking, probe execution and connection selection rules are unchanged. Partial
results update the existing indicators in place; they do not start a sorting sweep.
The original checking sheen is restored inside the flag of a checking country.

## Presentation

`ServerListViewModel.Servers` holds immutable `ServerItem` snapshots in the latest
real order. The ItemsControl binds only to stable `ServerDisplaySlot` objects.
Each slot has five separately revealed snapshots: signal, existing connection
badge, country name, flag and selection. No visual collection Move or row
translation occurs. `DisplayedServer` (including the click identity) commits
with the country-name group; clicks still resolve against the live group/health.
The actual selected service is never changed by the animation.

`ServerListSweepAnimator` owns one monotonic Stopwatch, one active pass and one
CompositionTarget.Rendering subscription. Each pass directly exchanges TWO
assigned countries and draws one noninteractive band on each visible affected
row. Both bands share the same start and elapsed time; no other pair starts
until the 800 ms pass ends. Only assigned identity changes get a pass. Same-position ping, selection and star updates bind normally without
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
.95, .99 and 1. The white peak is approximately 55% opaque, with a softer leading
falloff and a low-opacity cyan tail. The static skew
and rounded row/viewport clips are prepared before each pass. The existing
card's actual corner radius is read from its template. No blur or shadow is
added. Idle row artwork, text, spacing, RTL, palette and existing icons remain
unchanged.

Before each pass, existing content groups are measured through TransformToVisual
into the physical LTR overlay. Inherited RTL transforms are included. The timeline
calculates crossing times using the inverse cosine easing. For each group, the
shared crossing is the later of the two measured row crossings. Both groups keep
their old data until both bands have reached them, then both assignments commit
before either binding is notified. Names, flags and selection therefore exchange
atomically even when content centers or row widths differ. Dropped frames process
every crossed group once. Changed content fades from opacity .2 to 1 over 400 ms on the existing
content element itself; unchanged content gets no flash. No brightness rectangle
is drawn over the element's layout bounds: stretched country labels otherwise
produce a conspicuous horizontal gray bar behind the text. Flash start times use the
logical crossing time, so late frames do not extend the effect.

Two servers exchanging any two positions require ONE 800 ms pass with two lights.
A three-country cycle resolves through two such direct exchanges (about 1.6 s,
plus normal frame/dispatcher scheduling). Unchanged intermediate rows are skipped.
There is no stagger or all-finished delay. A final group's 400 ms opacity fade
can outlive its band; it does not draw another light or a white rectangle.

## Coalescing and lifecycle

Both active rows and targets stay frozen. New notifications replace the latest
pending order without restarting the clock. At every boundary, preparation finds
the first mismatch against that newest order and its target's current owner.
Those two slots exchange their countries in place. Matching positions cost no
animation time. Neither source is cleared, hidden or made vacant during sorting.
Each content group remains a permutation of all current identities throughout
an exchange, including inside PropertyChanged observers. Both partially committed
rows are protected from ordinary publications. Structural additions/removals cancel
a partial pair and reconcile the newest membership instantly, avoiding blank cards
or obsolete removed identities.

A new explicit check cycle cancels presentation, applies the newest authoritative
data and invalidates old callbacks before resetting checking states. The manual
refresh and explicit disconnect/restart paths both use this boundary. Canceled
old background checks retain their existing data cleanup but cannot request a
new sorting effect. Search/navigation/loading uses instant presentation.

Pairs with both rows unrealized/offscreen drain in an iterative preparation loop.
Realized offscreen partners are prepared with their viewport-clipped band too,
so scrolling them into view retains the same elapsed time. An unrealized partner
still commits atomically on the realized row's timeline. Recycling is checked before revealing or flashing a realized row.
Empty/single lists and additions/removals reconcile immediately. Scroll changes
refresh cached clips/bounds without restarting time. Resize, hide, minimize, unload, close and
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
- Restored the original clipped probing-flag sheen and safe ProbeShift transform
  ownership in `ServerCountryPicker.ProbeShine.cs`. Its 3.2 s cycle runs only while
  that displayed country is checking. Hide, minimize, unload and the OS animation
  preference stop its clock; restore resumes only checking flags. It is independent
  of the paired reorder bands and never changes server results.
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
python3 tests/ProbeShineChecks/run.py /path/to/dotnet
python3 tests/ThemeChecks/run.py
python3 tests/ServerRefreshChecks/net48_compile.py /path/to/dotnet
```

The model/timeline checks cover incremental completion, reset order including
reduced motion, atomic paired group timing, stable selection, one active pair,
no empty rows or duplicate names/flags at every binding notification, 200 coalesced
publications, rapid randomized reorderings, stale callbacks, additions/removals,
empty/single lists, 4096 unrealized rows, exact geometry/easing and dropped frames.
The flag ownership fixture checks safe cloning of shared/frozen transforms. Contracts compare idle row artwork against
1.4.20 and inspect removed effects, bindings and the backend-free frame path.
The net48 check compiles actual picker/controller/refresh C# against real WPF
reference assemblies with application/Core doubles. It does not build BAML or
produce a Windows app executable.

Windows-only compiled-template smoke test, using an unobfuscated app build:

```
powershell.exe -NoProfile -STA -File tests/ServerListTransitionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
powershell.exe -NoProfile -STA -File tests/ServerStarGlowChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
powershell.exe -NoProfile -STA -File tests/ProbeShineChecks/run-wpf.ps1
```

The comet smoke test exercises the production picker, existing country aggregation
on fixture URL data, two moving bands, paired measured group commits, no blank
rows, active pair freezing, reset, selection, checking-flag clocks and minimize/
restore/close cleanup. Fixture service
constructors and VPN/probes are not run. Native WPF tests have not been run in
the Linux implementation environment. Rendering correctness, input/scrolling,
DPI/RTL appearance and frame pacing still require Windows acceptance. Test both
themes at 100/150/200% DPI, rapid rechecks, search, added/removed rows, minimized
windows and the OS reduced-motion preference. Profile a low-end Windows device
before making a 60 FPS claim.
