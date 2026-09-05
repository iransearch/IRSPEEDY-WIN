Run on a machine with the .NET SDK and .NET Framework 4.8:

```
dotnet run --project tests/StartupRoutingChecks/StartupRoutingChecks.csproj
```

These checks link the production routing and RPC code. They cover preservation
of AI/IR/private/UDP rule priority, independent removal of startup rules, absence
of fallback tags, untested candidates, Xray protobuf responses and a TCP server
that accepts connections but never answers an RPC.

Windows integration check with the packaged core and curl:

1. Connect with Smart and AI enabled. Confirm `StartupTest` reports successful
   candidates and `StartupRoute stage=pinned` for each successful group.
2. Request a site immediately. `ConnectionStartup stage=core-ready` records the
   core start duration; connection tests record the actual response time.
3. Wait for separate `StartupRoute stage=automatic` entries for smart and AI.
   They must follow healthy `GetBalancerInfo` results, without a core restart.
4. Disconnect while probes/handoff are running, then select a different country.
   The old session must not remove any new session's uniquely named rules.
5. With all candidates unreachable, expect `no-healthy-candidate`, no startup
   pin for that group, and no `fallbackTag`. An API/curl failure keeps existing
   tested pins and records `handoff-pending` instead of pretending handoff worked.

The 2-second budget covers network selection. Core construction, cancellation
draining and a hung-core RPC deadline can add overhead. Early results are polled
every 100ms; older cores without incremental results return the final batch.

Live handoff uses Xray's loopback RoutingService via HTTP/2 in the bundled curl.
It does not use the system proxy, reload Xray, or close existing user streams.
Wire definitions: https://github.com/XTLS/Xray-core/blob/main/app/router/command/command.proto
API: https://xtls.github.io/en/config/api.html
