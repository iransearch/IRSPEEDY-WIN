# Connected application traffic

The left-edge tab opens a stats overlay only on the connected screen. Its colors,
font, cards and controls use the existing IRSpeedy theme. The original header and
connection layout stay fixed. Opening/closing animates a frozen panel texture with
a left-hinged PerspectiveCamera/RotateTransform3D plus translation. The real table
is shown at the open endpoint. The connected illustration pauses/resumes its
existing storyboard clocks; it is never restarted by the tab. Background blur is
a constant effect on a frozen snapshot, cross-faded without animating its radius.
Snapshots use local coordinates and the current monitor DPI. Interrupted motion
reuses the same texture and current animation values.

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

The `نمونه` button retains the seven original fixture rows in release builds. The
preview is explicitly labeled and cannot reset or contaminate the live ledger.
`زنده` returns to actual usage. Table refresh changes existing rows rather than
replacing ItemsSource, preserving selection and scroll position.

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
6. Switch between sample/live modes, sort, select and scroll. Close or disconnect
   during an animation/reset confirmation; no overlay should remain on the next page.
