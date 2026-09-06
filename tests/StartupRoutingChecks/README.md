Run on a machine with the .NET SDK and .NET Framework 4.8:

```
dotnet run --project tests/StartupRoutingChecks/StartupRoutingChecks.csproj
```

These checks link the production routing and RPC code. They cover preservation
of AI/IR/private/UDP rule priority, independent removal of startup rules, absence
of fallback tags, untested candidates, Xray protobuf responses and a TCP server
that accepts connections but never answers an RPC.

Windows integration check with the packaged core:

1. Connect with Smart and AI enabled. Confirm `StartupTest` reports successful
   candidates and `StartupRoute stage=pinned` for each successful group.
2. Request a site immediately. `ConnectionStartup stage=core-ready` records the
   core start duration. Separate background `route-response` / `route-probe-failed`
   events measure one bounded request through each production route.
3. Wait for separate `StartupRoute stage=automatic` entries for smart and AI.
   They must follow healthy `GetBalancerInfo` results, without a core restart.
4. Disconnect while probes/handoff are running, then select a different country.
   The old session must not remove any new session's uniquely named rules.
5. With all candidates unreachable, expect `no-healthy-candidate`, no startup
   pin for that group, and no `fallbackTag`. `waiting-for-health` remains until
   the live balancer reports eligible targets. The selection budget stays 2s;
   `stopReason=budget-exhausted` is different from `healthy-routes-found`.
6. API failures retain tested pins and report gRPC status/detail per balancer.
   There must be no routing curl process and no `Routing curl exit=2`.
7. Confirm both `grpc_csharp_ext.x86.dll` and `grpc_csharp_ext.x64.dll` accompany
   the executable in normal, published and protected distributions. The build
   validates them and copies them to the Dotfuscator output automatically.
   When using SmartAssembly, resolve/embed `Grpc.Core.dll` and `Grpc.Core.Api.dll`
   like other managed dependencies; keep both native DLLs alongside the final EXE.

The 2-second budget covers network selection. Core construction, cancellation
draining and a hung-core RPC deadline can add overhead. Early results are polled
every 100ms; older cores without incremental results return the final batch.

Live handoff uses a persistent Grpc.Core 2.46.6 channel to Xray's loopback
RoutingService (HTTP proxy use explicitly disabled). The package supports net48
without depending on the optional HTTP/2 features of a curl executable or the
Windows HTTP stack. No temporary request/header files or curl processes are used.
The transport checks start a real local gRPC server and verify healthy/empty
responses, rejected rule removal, a deadline and cancellation. They also cover
retaining errors instead of accepting HTTP 200 as gRPC success.

Runtime integration still needs to be checked on Windows with the actual core.
No change is claimed to repair an unreachable remote AI server; its failed
probes are classified as timeout, cancelled, DNS, TLS or other network failure.
Wire definitions: https://github.com/XTLS/Xray-core/blob/main/app/router/command/command.proto
API: https://xtls.github.io/en/config/api.html
