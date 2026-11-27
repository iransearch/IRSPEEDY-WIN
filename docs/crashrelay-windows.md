# Windows crash reporting in CrashRelay

1. Before building, place the project's CrashRelay DSN in a UTF-8 file named `.crashrelay-dsn` next to `IRSpeedyVPN.sln`. Use the full `https://HOST|PROJECT_KEY|INGEST_KEY` form. The file is ignored by Git. A build can also pass `-p:CrashRelayDsnFile=PATH` to choose a different file.
2. Build and package Windows using the usual process. The build embeds the DSN. Use a project ingest key here and keep administrative credentials on the server. Builds made without the file run without CrashRelay.
3. Close any running IRSPEEDY instance. Run `IRSpeedyVPN.exe --crashrelay-test` and leave it open for a few seconds. Check the configured project in CrashRelay for `IRSPEEDY Windows telemetry test`. Projects can distinguish these events by their Windows platform.

CrashRelay receives managed app crashes, handled exceptions from the existing diagnostics path, UI hangs, and unexpected Core exits during active connections. Fatal events are saved locally and can be sent on the next launch. Reports include stack frames and selected error codes. The integration omits exception messages, console logs, HTTP observations, account IDs and dump files.
