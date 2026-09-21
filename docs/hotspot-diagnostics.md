# Direct hotspot diagnostic logs

The regular application log now contains bounded `[HotspotDiagnostic]` records.
Match their `attempt` to `[HotspotPerformance]` and the adjacent `[Hotspot]`
error. No additional user command or diagnostic mode is required.

Recorded evidence:

- App/helper/runtime versions, Windows version and process/OS architecture.
- Helper protocol and whether a previous session requires recovery.
- The session's adapter snapshot BEFORE preflight validation, including failures
  that happen before a Publisher is created.
- The Wi-Fi Direct backend's second preflight snapshot, taken from the SAME read
  used by its conflict check.
- Publisher status/error and whether this helper created a Publisher.
- ICS enable attempts, HRESULTs, preparation results and the final eight
  sharing observations, labelled public/private/other.
- Primary/cleanup failure types and HRESULTs, lease age and health failure reason.

Adapter rows include a process-scoped opaque Key, IsTun, IsSelectedTun,
IsWifiDirect, Up, Role, Status, Type and Present. Role 0 means public sharing;
role 1 means private sharing; none means not shared. Present indicates that the
COM connection also appeared in NetworkInterface enumeration. Missing entries
remain Unknown; they are not silently reported as disconnected hardware.

For `wifi-direct-already-active`, look for a `wfd-preflight-adapter` row where
IsWifiDirect and Up are true. Compare with session-preflight to spot changes
between reads. PublisherCreated=false means this helper rejected startup before
creating its own Publisher. These records show the exact reason for refusal;
they do NOT prove which application owns the adapter or that it is broadcasting.
Do not infer another application's ownership merely from Up.

Raw adapter GUIDs, user-assigned adapter names, SSIDs, passwords, MAC/IP addresses,
raw exception messages and full JSON replies are not copied into diagnostic logs.
Keys are stable within one helper process and change in the next helper process.
The logging uses existing snapshots and does not add adapter enumeration, change
network state, relax conflict checks or reset adapters.

Reproduce one failed activation and send the regular application log covering
the attempt. Build and test on Windows before distributing the executable.
Portable regression tests:

```powershell
dotnet run --project tests/HotspotChecks/HotspotChecks.csproj -c Release
```
