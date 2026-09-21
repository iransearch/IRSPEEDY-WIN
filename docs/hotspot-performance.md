# Hotspot activation timings

Filter the application log for `[HotspotPerformance]`. Each activation has a
random `attempt` identifier (not a device or user identifier).

| Clock | Stage | Measures |
| --- | --- | --- |
| app | payload-prepare | Extract/validate or reuse the embedded helper payload |
| app | process-launch | Start the helper process |
| app | helper-ready | Wait for the ready handshake after launch |
| app | recovery | Recover a previously journaled session, when necessary |
| helper | preflight-adapters / backend-prepare | Enumerate and validate adapters and startup prerequisites |
| helper | journal-create / journal-start | Persist recovery state before network mutation |
| helper | publisher-start | Configure and start the Wi-Fi Direct publisher |
| helper | adapter-ready | Wait for the adapter (or automatic sharing on the legacy backend) |
| helper | ics-bind / ics-verify | Configure and confirm the owned sharing pair |
| helper | clients-enable | Allow client connections after ICS is verified |
| helper | rollback | Cleanup after a failed startup, when necessary |
| helper | session-start | Entire helper session startup, including rollback on failure |
| app | activation-total | Entire channel startup through validated active response |

`elapsedMs` is the stage duration; `totalMs` is elapsed time from the relevant
clock's start. App and helper clocks have different origins. Total rows contain
their child stages: do not sum all rows. Outcome describes whether the stage
completed without an exception, not an end-to-end Internet connectivity test.
A timeout with no helper reply may have only app-side timing rows.

Timings appear in optional `timings` fields on helper start replies and errors.
The application logs only allowlisted stages, integer durations and success
flags. Do not log whole replies: existing successful protocol replies contain
SSID/password for the UI. Timing records themselves contain no SSID, password,
MAC, adapter GUID, username or raw exception message.

Publisher start and stop now subscribe before invoking the Windows operation,
then check status once and await a terminal status event. Temporary subscriptions
are removed on success, error and timeout. The 10-second publisher deadline is
unchanged. Adapter/ICS polling, native retries, ownership checks and durable
recovery journal remain in place.

## Validation

Run the portable fake-backend and event-wait regression checks with .NET 8:

```powershell
dotnet run --project tests/HotspotChecks/HotspotChecks.csproj -c Release
```

These checks do not enable Wi-Fi or alter Windows networking. Build the Windows
helper with the existing builder and test on a real adapter before release.
Compare multiple cold activations and repeated activations separately. Record
publisher-ready, ICS-ready and actual client Internet availability separately;
the activation-total record does not prove that a client has Internet access.
Do not claim an overall speedup until those measurements have been collected.
