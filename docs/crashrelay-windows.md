# Windows crash reporting in CrashRelay

1. Save the project ingest DSN once on the Windows build account at
   `%LOCALAPPDATA%\IRSpeedyVPN\Build\crashrelay.dsn`. This file survives fresh
   checkouts. Use the full `https://HOST|PROJECT_KEY|INGEST_KEY` form. An
   explicit `-p:CrashRelayDsnFile=PATH` takes precedence, followed by the ignored
   `.crashrelay-dsn` beside `IRSpeedyVPN.sln`, then the Windows build-account file.
2. Build the Release single-file executable with `UseCosturaSingleFile=true`
   or the existing builder. Packaging refuses to proceed if the DSN file is
   missing (`IRSC001`) or invalid/unreadable (`IRSC002`). It embeds the
   configured file in the executable. Use an ingest key for the intended
   project and keep administrative credentials on the server.
3. Exit any running IRSPEEDY instance. Run `IRSpeedyVPN.exe --crashrelay-test` and leave it open for a few seconds. The local log should show `[CrashRelay] status=ready` and `verification requested`. Confirm `IRSPEEDY Windows telemetry test` with `/issues irspeedy platform=windows` when the DSN belongs to the `irspeedy` project. Normal app launches register sessions and do not create an item in `/issues`.
4. If the test does not appear, inspect the local `[CrashRelay]` line for a missing build DSN, an initialization or SDK load failure, or an already running instance. A requested verification shows that the app called the SDK. The bot issue confirms delivery.

CrashRelay receives managed app crashes, handled exceptions from the existing diagnostics path, UI hangs, and unexpected Core exits during active connections. Fatal events are saved locally and can be sent on the next launch. Reports include stack frames and selected error codes. The integration omits exception messages, console logs, HTTP observations, account IDs and dump files.
