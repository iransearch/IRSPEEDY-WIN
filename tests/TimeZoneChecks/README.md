Run `dotnet run --project tests/TimeZoneChecks/TimeZoneChecks.csproj`.

These checks link the production session, GeoIP parser, bundled CLDR map,
recovery journal, native structure definitions and routing rule. They exercise
activation, fixed selection, cancellation/reconnect races, subscription time,
rollback, failed restoration and recovery after simulated process termination.
Network and Windows mutation are replaced with deterministic fakes; the atomic
file journal and Win32 marshaling layouts are tested directly.

Also run `python tests/ExitChecks/lifecycle.py`,
`python tests/LastSuccessfulServerChecks/connection_result.py` and
`python tests/ThemeChecks/run.py`.

Windows acceptance checks (require a real VPN connection): verify the clock
replaces the server-change icon in both themes; click it and compare the selected
zone with the exit IP's GeoIP zone; confirm a green icon and unchanged connection
duration; toggle off and verify the original Windows zone and daylight-saving
preference; enable again, disconnect and verify restoration starts immediately.
Reconnect while a lookup is pending to check that its old result is discarded.
After a forced process kill while active, start the app again and verify recovery.
Those native mutations and WPF rendering cannot be executed on Linux.

`python tests/TimeZoneChecks/net48_compile.py` compiles the new production
modules and UI handler against real .NET Framework 4.8 and WPF reference
assemblies, with only the surrounding application/view fields stubbed out.
Pass a dotnet executable path as the optional first argument to the Python checks.
