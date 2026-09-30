# Traffic checks

Run from the repository root:

```powershell
dotnet run --project tests/TrafficChecks/TrafficChecks.csproj
```

Uses the actual app collector, ledger, persistent store, configuration adapter and
ProtoRPC decoder/client. Network checks use loopback-only test peers; file checks
use a disposable temporary directory. Covers repeat polling, short-lived closed
connections, repeated closed rings, reconnects, unknown processes, resetting during
an in-flight sample, reset failure, nonblocking UI snapshots, file recovery,
post-reset recovery cleanup, 64-bit protobuf counters, malformed payloads and RPC
deadlines. No production history is modified.

For the native WPF/manual performance checks, see `docs/traffic-stats.md`.
