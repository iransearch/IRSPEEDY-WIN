# Connected application traffic

The middle-left edge tab opens the stats overlay on the connected screen. Its
colors, font, cards and controls use the existing IRSpeedy theme. Motion follows
the approved `irspeedy-fold-stats-blur.html` reference:

| Effect | Reference and native target |
| --- | --- |
| Panel bounds | Left/right 18, top 50, bottom 84 in the full connection scene |
| Perspective | 1300 logical pixels; origin at the full scene center |
| Closed transform | Translate X by `-(panelWidth + 28)`, rotate Y by -68 degrees around the left edge |
| Transform timing | 620 ms in both directions; KeySpline(.22,1,.36,1) |
| Panel opacity | 0 to 1 over 480 ms with CSS ease (.25,.1,.25,1) |
| Crease | 34px band at 48%; opacity .55 to 0 over 600 ms, scale X 1.7 to .2 over 620 ms |
| Background | Translate X 10, scale .975, opacity .6, blur radius 6; transform/blur 620 ms, opacity 520 ms |
| Shadow | Offset Y 12, blur 34, color #1e3a8a35 |

A full-scene viewport performs rotation then translation **before** perspective;
a panel-sized viewport or a 2D viewport translation would clip/distort this effect.
The texture includes 48px padding for the shadow. Emissive materials preserve the
captured UI colors. The native table replaces the texture at the open endpoint.

The connected illustration pauses/resumes its existing animation clocks. Only a
frozen image of the connected content moves/scales/blurs; the original layout and
header remain fixed. At the closed endpoint the image returns to identity before
the original is restored, avoiding a jump. Background controls are disabled while
the overlay is open, including keyboard activation. Rapid reversals reuse current
animated values and textures instead of recapturing or resetting to an endpoint.

## Source and counting

`TrafficConfiguration` enables Throne's internal traffic tracker via
`experimental.clash_api.default_mode` and enables `route.find_process`. It adds no
external controller listener and preserves routing rules, outbounds and existing
experimental options. Stats use a dedicated, deadline-bounded ProtoRPC connection,
independent of connect/test RPCs and their locks. This does not restart the core.

`LibcoreService.QueryConnections` is available in both reviewed Throne-G commits
`4facf1479dbbd62654fb727e47a6c5c5d9737a57` and
`c0fa2071bda243762b6387e56355f8a4242bcad5`. The protobuf response supplies active
connections and a non-draining recently-closed ring. The collector samples each
second while a real core session runs, even if the panel/window is hidden. It
credits byte differences by connection ID, accounts for short-lived closed
connections and groups totals by executable name (as Throne does). Unknown process
names have their own row. This is traffic observed by the core, including its
tracked direct routes; it is not a system-wide network-adapter meter.

## Persistence and reset

Cumulative per-program totals are stored in
`%LOCALAPPDATA%\IRSpeedyVPN\traffic-stats-v1.json`, outside the extracted runtime and
Temp directory. There is no automatic expiration. A worker saves every ten seconds
and performs a bounded final sample/save before normal core teardown and exit.
Abrupt process termination may lose the last unsaved interval.

Writes use a flushed temporary file and atomic replacement, with a recovery backup.
An unreadable primary and backup are preserved instead of silently overwritten.
The file records only executable name/path and byte totals, not destination IPs,
domains or browsing history.

The reset arrow opens an inline confirmation. Reset serializes with polling, reads
a fresh baseline, atomically saves zero totals, and continues counting only new
bytes. A failed baseline query or save does not clear current totals. Reset removes
the old recovery backup so pre-reset usage cannot return through recovery. It does
not stop/start the core or disconnect the tunnel.

The connected UI always shows real traffic. The sample toggle was removed at the
user's request; the original fixture source is retained only for design reference
and is never bound by the connected screen. Table refresh changes existing rows
rather than replacing ItemsSource, preserving selection and scroll position.

## Validation

Run `dotnet run --project tests/TrafficChecks/TrafficChecks.csproj` on .NET 8.
The checks exercise real production ledger, store, collector, config and RPC code,
including 64-bit protobuf values, closed-ring deduplication, reset during an
in-flight query, disk reload/recovery, failed reset and an unresponsive RPC peer.
Existing ConnectionStartChecks and ExitChecks also link the new traffic decoder.

The isolated UI C# 7.3 compilation against .NET Framework 4.8 references and both
WPF markup compilation passes are checked during development. A complete packaged
Windows runtime/performance check is still required; Linux cannot execute WPF.

Windows acceptance checks (same single-file builder, no new package dependencies):

1. Connect; open/close the middle-left tab repeatedly, including reversal mid-motion.
   The title, rocket position and connection card must remain fixed, and the rocket
   animation must resume from its paused phase. Escape/outside click close the panel.
2. Check at 100%, 125%, 150% and 200% DPI, including moving between monitors. The
   moving texture must align with the live panel without a final jump or clipped text.
3. Generate traffic with the panel closed and the app minimized. Open it and confirm
   real per-app download/upload values, then close the source app: totals must remain.
4. Disconnect/reconnect and restart IRSpeedy. Totals must accumulate, including a
   fresh session whose core counters start at zero. Removing Temp must not reset them.
5. Reset while a download is active. Old bytes must not return on the next poll;
   the tunnel remains connected. Cancel/Escape must preserve totals.
6. Sort, select and scroll. Close or disconnect during an animation/reset
   confirmation; no overlay should remain on the next page. The sample toggle
   must not be present, and Tab must not reach the hidden connection controls.
