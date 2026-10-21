# Server list light sweep (1.4.15)

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
`StaggerDelay` (75 ms), `BandWidth` (100 DIP), `BandColor` and `Easing`.
A single WPF `DoubleAnimation` with CubicEase/EaseInOut drives normalized progress
`p`. In physical left-to-right coordinates its light center is
`-BandWidth/2 + (rowWidth + BandWidth) * p`. Two content presenters have complementary
render clips meeting at this center: new content appears on the left of the
light and old content remains on its right. This reveals ping, name, flag and
radio at their own positions instead of swapping the entire row ahead of the
light. `DisplayedServer`/button identity commits when `p >= 0.5`, exactly when
the light center crosses the row midpoint; completion commits it again safely.
There is no independent timer that can drift relative to the animation.
Rows retaining the same identity use a 150 ms cross-fade for changed ping/star/
selection data; identical snapshots do not animate.

Probe property notifications coalesce into one dispatcher snapshot. During a
wave, sorted source data continues updating; only the latest pending state is
kept. The current wave finishes, then that latest state is revealed. No queue of
obsolete orderings is replayed, and old completion callbacks cannot overwrite a
newer state. Clicking resolves the displayed identity against the CURRENT live
group and health, so removed/failed servers cannot become a stale connection.
Sorting, server pools, DNS, Core and TUN recovery behavior are unchanged.

Initial population and unrealized/offscreen additions update immediately.
Existing slots reveal additions/removals; trailing removed slots are trimmed only
after the wave. The first visible row starts the cascade, even after scrolling.
Hidden/unloaded/minimized views flush pending data and finish immediately; restore
starts with the latest data. Resizing completes the affected row rather than
stretching a partly revealed snapshot.

## Accessibility and performance

The effect is enabled by default with no app setting or registry flag. Windows
client-area animation disabled means immediate updates, respecting the OS motion
preference independently of the existing probing/smart-card ornaments.

Only normalized animation progress, clips, opacity and RenderTransform change per
frame; no row positions or sizes animate. Content presenters use BitmapCache
only during the transition and release the second content afterward. Clipped
offscreen rows have no sweep clock. PNG flags decode with OnLoad and are frozen/
cached before use; vector flags freeze where supported. Flag scaling is HighQuality.
Frozen gradient brushes, rounded clips, skew transforms and band blur effects are
reused during each sweep. Render clips and translation remain mutable by design.

## Validation

```
python3 tests/ServerListTransitionChecks/run.py /path/to/dotnet
python3 tests/ServerRefreshChecks/net48_compile.py /path/to/dotnet
python3 tests/LastSuccessfulServerChecks/run.py /path/to/dotnet
python3 tests/ProbeShineChecks/run.py /path/to/dotnet
python3 tests/ServerStarGlowChecks/run.py /path/to/dotnet
```

The first check exercises the production VM with a manually advanced view clock:
snapshot isolation, midpoint identity commit, selection retention, unchanged
rows, failure/star updates, 200 rapid publications, last-update coalescing,
stale callbacks, offscreen staggering, additions/removals and zero/single rows.
The net48 check compiles the actual picker, view model, transition control,
existing motion and refresh controller against WPF reference assemblies. It
does not execute Windows XAML or establish a VPN.

Windows acceptance still required (rendering/60 FPS cannot be measured on Linux):

- Build the real WPF app, re-ping several numbered countries, and verify that
  physical left-to-right reveals cascade down with no instant row replacement.
- Select a country before it moves; check the radio and click destination both
  follow its identity. Repeat while a partial ping and a final result arrive.
- Repeat refresh/sort during a wave; after settling, every slot must match the
  newest order/ping. Star pinning must still mean the last successful connection.
- Search while sweeping; add/remove countries through a refreshed API list;
  include one country, an empty result and a failed/timeout ping (`—`, muted dot).
  Earlier successful latency remains available in the failure tooltip/cache.
- Minimize, hide the list, resize, logout, and restore during a cascade. No old
  animation/data should replay. Scroll to the middle before re-sorting.
- Disable the Windows animation preference: updates must be instant.
  Check both themes, RTL text, and 100/150/200% DPI against the idle design.
- On a low-end Windows machine, profile a ten-row cascade with WPF Perforator;
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
