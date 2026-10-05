# VPN public IP checks

Run `dotnet run --project tests/PublicIpChecks/PublicIpChecks.csproj`.

The checks compile the production lookup, IP page handler and routing helper.
HTTP fixtures exercise timeout and provider fallback, temporary startup failure,
bounded retries, cancellation, invalid/oversized responses, duplicate page
events, connection/port changes and late responses after disconnect. They verify
the explicit loopback proxy and priority of IP lookup routes while preserving
pool and AI configuration. UI fixtures exercise the real async handler without
running WPF; they do not verify a live Windows VPN.

Run `python tests/TimeZoneChecks/net48_compile.py` to compile the changed
production modules and UI handler against .NET Framework 4.8/WPF references.

On Windows, verify IP display in TUN, System Proxy and global Proxifier, including
selected-app split tunneling and AI enabled/disabled. Block the first IP provider
while keeping the VPN usable and verify fallback, then disconnect while lookup
is pending and reconnect to another country. No old IP should appear. With all
providers unavailable, lookup stops; it never disconnects VPN or uses the ISP
address through a direct request. Reopening the page during the same connection
keeps the previously observed IP; Round-robin can give different flows different
exit addresses.
