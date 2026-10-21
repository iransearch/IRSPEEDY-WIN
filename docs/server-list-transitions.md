# Server list light sweep (1.4.19)

`ServerCountryPicker.xaml` keeps the existing row artwork in `ServerRowContent`.
Its `ServerCardButton` template hosts `LightSweepRowTransition`; the overlay is
not hit testable, has a soft white/cyan gradient, and is clipped to the card's
14-DIP rounded corners. Idle spacing, fonts, colors, selection artwork and the
meaning of the last-successful-connection star are retained.

## Data and timing

`ServerListViewModel.Servers` is an `ObservableCollection<ServerItem>` containing
the latest filtered, sorted snapshots. The existing probe/pool models remain
authoritative for health and connection selection. `Slots` is a separate stable
collection whose `DisplayedServer` can temporarily be the previous snapshot.
Snapshots copy all visual values, including selection/checking/star flags; later
probe mutations cannot silently change the old side of a running sweep.

The reusable control exposes `Slot`, `RowTemplate`, `Duration` (520 ms),
`BandWidth` (100 DIP), `BandColor` and `Easing`. Exactly one row transition can run
at a time. The next row starts after the previous row's full completion and
overlay cleanup, with no index-based stagger timer and no overlap.
A WPF `Storyboard` runs native transform animations with CubicEase/EaseInOut.
Their eased normalized progress is `p`. In physical left-to-right coordinates its light center is
`-BandWidth/2 + (rowWidth + BandWidth) * p`. Two content presenters have complementary
render clips meeting at this center: new content appears on the left of the
light and old content remains on its right. This reveals ping, name, flag and
radio at their own positions instead of swapping the entire row ahead of the
light. A discrete keyframe on the same storyboard commits `DisplayedServer`/button
identity exactly when the light center crosses the row midpoint; completion
commits it again safely. The easing is inverted once to find this key time, which
also supports asymmetric monotonic easing. There is no independent timer that
can drift relative to the animation.
Rows retaining the same identity use a 150 ms cross-fade for changed ping/star/
selection data; identical snapshots do not animate.

Probe property notifications coalesce into one dispatcher snapshot. During a
pass, sorted source data continues updating; only the latest pending state is
kept. The current row finishes, then the next waiting row reads its target from
the latest source. At the end of a pass, earlier slots affected by newer results
are revisited; unchanged slots skip animation. No queue of obsolete orderings
is replayed, and old completion callbacks cannot overwrite a newer state.
Clicking resolves the displayed identity against the CURRENT live
group and health, so removed/failed servers cannot become a stale connection.
Sorting, server pools, DNS, Core and TUN recovery behavior are unchanged.

Initial population and unrealized/offscreen additions update immediately.
Existing slots reveal additions/removals; trailing removed slots are trimmed only
after the pass. Offscreen/unrealized rows complete immediately, so scrolling does
not add an artificial delay before the first visible row starts.
Hidden/unloaded/minimized views flush pending data and finish immediately; restore
starts with the latest data. Resizing completes the affected row rather than
stretching a partly revealed snapshot.

## Accessibility and performance

The effect is enabled by default with no app setting or registry flag. Windows
client-area animation disabled means immediate updates, respecting the OS motion
preference independently of the existing probing/smart-card ornaments.

Only native transform/opacity timelines run per frame; there is no application
progress callback or per-frame geometry mutation. Fixed complementary clip
rectangles translate with the band; no row positions or sizes animate. Content presenters use BitmapCache
only during the transition and release the second content afterward. Clipped
offscreen rows have no sweep clock. PNG flags decode with OnLoad and are frozen/
cached before use; vector flags freeze where supported. Flag scaling is HighQuality.
Frozen gradient brushes, rounded clips, skew transforms and band blur effects are
reused during each sweep. Clip transforms remain mutable animation targets.
Probe flag sheen and star glow pause inside the active row so they do not
continually invalidate its cached bitmaps; they resume after final cleanup.

## Validation

```
python3 tests/ServerListTransitionChecks/run.py /path/to/dotnet
python3 tests/ServerListTransitionChecks/bindings.py
python3 tests/ServerRefreshChecks/net48_compile.py /path/to/dotnet
python3 tests/LastSuccessfulServerChecks/run.py /path/to/dotnet
python3 tests/ProbeShineChecks/run.py /path/to/dotnet
python3 tests/ServerStarGlowChecks/run.py /path/to/dotnet
```

The first check exercises the production VM with a manually advanced view clock:
snapshot isolation, midpoint identity commit, selection retention, unchanged
rows, failure/star updates, 200 rapid publications, last-update coalescing,
stale callbacks, additions/removals and zero/single rows. It asserts a peak of
one active clock, checks that midpoint reveal cannot release the next row, and
drains 4095 synchronous offscreen acknowledgments without recursive stack growth.
The net48 check compiles the actual picker, view model, transition control,
existing motion and refresh controller against WPF reference assemblies. It
does not execute Windows XAML or establish a VPN.

Windows acceptance still required (rendering/60 FPS cannot be measured on Linux):

- Build the real WPF app, re-ping several numbered countries, and verify that
  physical left-to-right reveals run one row at a time, with the next row starting
  only after the previous band has fully disappeared.
- Select a country before it moves; check the radio and click destination both
  follow its identity. Repeat while a partial ping and a final result arrive.
- Repeat refresh/sort during a wave; after settling, every slot must match the
  newest order/ping. Star pinning must still mean the last successful connection.
- Search while sweeping; add/remove countries through a refreshed API list;
  include one country, an empty result and a failed/timeout ping (`—`, muted dot).
  Earlier successful latency remains available in the failure tooltip/cache.
- Minimize, hide the list, resize, logout, and restore during a pass. No old
  animation/data should replay. Scroll to the middle before re-sorting.
- Disable the Windows animation preference: updates must be instant.
  Check both themes, RTL text, and 100/150/200% DPI against the idle design.
- On a low-end Windows machine, profile a ten-row sequence with WPF Perforator;
  verify frame pacing and transient bitmap memory before claiming a 60 FPS result.

## Starred-row loading fix (1.4.16)

The 2026-10-21 log from 1.4.15 records a UI crash at 15:18:42 while the first
22-row list is being constructed: `XamlParseException: 'Effect' property does not
point to a DependencyObject`. Its stack enters `StyleHelper` data-trigger actions
and `Storyboard.VerifyPathIsAnimatable` during `LoadOptimizedTemplateContent`.
The saved star's style can start its nested `UIElement.Effect` animation before
the template has assigned the Path's local DropShadowEffect. Compile-only tests
and a list without an already-starred country did not exercise this path.

Star glow now starts from Loaded/visibility lifecycle handlers after the effect
exists. It animates the actual effect's dependency properties directly, using
independent mutable copies of frozen template effects. Early visibility events
and missing effects are harmless; hide/unload/minimize removes both clocks.
The star geometry, gold gradient, static glow and original four-second pulse
values/timing are retained. The light sweep and app settings are unchanged.

The portable star regression fails on the 1.4.15 XAML and passes on the fix.
For real Windows validation, load an unobfuscated build's compiled BAML:

```
powershell.exe -NoProfile -STA -File tests/ServerStarGlowChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
```

This check constructs two already-starred row presenters inside a visible window,
then exercises selection-marker changes, hide, minimize/restore and unload. It
uses only UI resources and does not initialize VPN/probes. It must run on Windows;
the Linux net48/reference-assembly check cannot execute WPF rendering or BAML.

## Read-only snapshot binding fix (1.4.17)

The next 2026-10-21 log confirms 1.4.16, then reports a different XAML error at
15:34:51: `TwoWay or OneWayToSource binding cannot work on the read-only property
'CountryName'` of `ServerItem`. WPF Run.Text uses TwoWay by default. The former
mutable GroupItem country property accepted that default; the immutable sweep
snapshot intentionally has no setter.

All snapshot display bindings now explicitly declare OneWay: country Run and
tooltip, flag/initials, latency/color, checking/selection/star triggers, displayed
identity, automation and slot binding. Replacing a snapshot continues to refresh
its view. The scrollbar's intentional TwoWay binding is retained. The model stays
immutable, and the star lifecycle fix and light sweep are preserved.

`bindings.py` fails on 1.4.16 and passes on this fix. The Windows companion also
checks actual compiled Run binding mode, getter-only country loading and snapshot
replacement without writing to the old model. Its existing already-starred row,
hide/minimize/unload checks remain in place.

## One row at a time (1.4.18)

The earlier 75 ms stagger allowed several 520 ms sweeps to overlap. The scheduler
now waits for `ServerDisplaySlot.Finish` before starting the next changed row.
That completion notification removes the current view's clock/overlay first;
the midpoint `Reveal` only swaps identity and never advances the queue. Short
ping/star/selection fades use the same serial queue.

Pending rows take the latest available target at their turn. A subsequent pass
repairs only earlier rows that became outdated while the light moved downward.
Cancellation invalidates the active callback and discards the pending sequence.
Synchronous offscreen/unrealized completions drain in a guarded loop, avoiding
recursive callback growth. The old stagger property/index and timers are removed.
This scheduling applies to full-row transitions; the existing smart-card and
probing-flag ornaments keep their separate behavior.

## Presentation independent of probe callbacks (1.4.19)

Backend publications only replace the latest sorted snapshots and post one
presentation callback. The picker schedules presentation at DispatcherPriority.Render;
starting the next row is likewise posted after the previous view releases its
storyboard. No probe callback, network completion, sleep or timer is needed to
advance a wave. Canceled presentation tickets cannot start old rows, and repeated
publications coalesce while a presentation callback is pending.

Partial background/manual ping notifications now use DispatcherPriority.Background,
allowing rendering and input to run first. Cache writes remain before these UI
notifications, and final results retain their existing priority and cancellation
guards so late partials cannot replace a completed result. This does not move WPF
objects to a worker thread; WPF still needs a responsive UI dispatcher.

The portable transition suite also tests deferred presentation, 200 publications
before the first presentation turn, completion with no further backend activity,
queued-ticket cancellation and native coordinate/key-time alignment. On Windows,
exercise the real native clocks and compiled template with an unobfuscated build:

```
powershell.exe -NoProfile -STA -File tests/ServerListTransitionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
```

This checks direct storyboard targets, complementary native clip transforms,
asymmetric easing/midpoint identity, full completion cleanup, short opacity fades
and hiding an active row. It does not start any network tests or VPN service.
It cannot run on Linux; frame pacing still requires the Windows acceptance above.
