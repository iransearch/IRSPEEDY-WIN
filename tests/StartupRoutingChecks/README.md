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
7. Build the application in Release with the default `UseCosturaSingleFile=true`.
   Copy only the EXE into a fresh directory outside bin/dist; put no Grpc.Core,
   Grpc.Core.Api or grpc_csharp_ext DLLs next to it. Run and connect. Expect
   `stage=native-ready architecture=x64` (or x86), `control-ready`, then the
   independent `automatic` events.
8. Repeat with the SmartAssembly-protected EXE alone. Preserve the embedded
   `IRSpeedy.NativeGrpc.x86` / `.x64` resources and the Costura loader when
   configuring protection. Repeat using a cold native cache and a valid cache.
   Single-file here refers to distribution; Windows loads the embedded native
   library from an automatically managed per-user, content-hashed cache.
9. The C# checks validate both embedded PE architectures, cache reuse, recovery
   from a corrupt cache and concurrent extraction before starting the test server.

The 2-second budget covers network selection. Core construction, cancellation
draining and a hung-core RPC deadline can add overhead. Early results are polled
every 100ms; older cores without incremental results return the final batch.

Live handoff uses a persistent Grpc.Core 2.46.6 channel to Xray's loopback
RoutingService (HTTP proxy use explicitly disabled). The package supports net48
without depending on the optional HTTP/2 features of a curl executable or the
Windows HTTP stack. No temporary request/header files or curl processes are used.
The native libraries are embedded by `build/NativeGrpc.targets`, with sidecar
copying disabled. Managed references are embedded by Costura in app builds.
The transport checks start a real local gRPC server and verify healthy/empty
responses, rejected rule removal, a deadline and cancellation. They also cover
retaining errors instead of accepting HTTP 200 as gRPC success.

Runtime integration still needs to be checked on Windows with the actual core.
No change is claimed to repair an unreachable remote AI server; its failed
probes are classified as timeout, cancelled, DNS, TLS or other network failure.
Wire definitions: https://github.com/XTLS/Xray-core/blob/main/app/router/command/command.proto
API: https://xtls.github.io/en/config/api.html
