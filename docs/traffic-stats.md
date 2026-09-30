# Connected application traffic

The middle-left edge tab opens a separate, owned drawer directly **beside the
left edge** of the connection window. The connection page and header retain their
layout and live animations. The drawer uses the existing IRSpeedy theme and the
real `ConnectionTrafficPanel`; it adds no taskbar entry or independent application.

Opening slides the panel outward from the connection edge over 680 ms; closing
slides it back over 520 ms. Both use KeySpline(.22,1,.36,1). A clipped render
translation moves the panel without resizing the main window, rebuilding the
connection page, capturing its image, or projecting the table into a 3D viewport.
Rapid toggles reverse from the current position. The UI refresh timer pauses only
while the panel moves; background traffic collection continues throughout.

The drawer follows the main window when dragged and closes when the app is hidden,
minimized, disconnected, or leaves the connected page. Escape dismisses a reset
confirmation first, then closes the drawer. Normal closing retains the table's
scroll and sorting state. Placement uses monitor working-area and screen-pixel
coordinates, including the app's presentation scale. If there is insufficient
space on the left when opening, the main window is moved only as far right as
needed to make space; its size and internal layout do not change.

Each download/upload/total cell keeps its value and unit on one line (`533 MiB`).
The narrow, rounded scrollbar uses the server-list appearance, fades out when
idle, and does not change column widths as it appears or disappears. The units
remain accurate binary units (KiB, MiB, GiB); values are not relabeled as decimal MB.

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

1. Connect; toggle the middle-left tab repeatedly, including reversal mid-motion.
   The panel must slide beside the window, with the title, rocket and connection
   card retaining their positions and continuing their animations.
2. Check at 100%, 125%, 150% and 200% DPI, including moving between monitors and
   opening near screen edges. The drawer must remain beside the connection window,
   fit the working area and follow dragging without covering the connection page.
3. Close and reopen after scrolling/sorting. Values and units must stay on one
   line; the thin scrollbar should appear on interaction and fade when idle.
4. Minimize/hide the app or disconnect while the drawer is moving. No orphan drawer
   may remain. Escape must cancel a reset confirmation before closing the drawer.
5. Generate traffic with the panel closed and the app minimized. Open it and confirm
   real per-app download/upload values, then close the source app: totals must remain.
6. Disconnect/reconnect and restart IRSpeedy. Totals must accumulate, including a
   fresh session whose core counters start at zero. Removing Temp must not reset them.
7. Reset while a download is active. Old bytes must not return on the next poll;
   the tunnel remains connected. Cancel/Escape must preserve totals. The sample
   toggle must not be present.
