# Offline Windows login recovery checks

See [the implementation and validation notes](../../docs/windows-login-recovery.md).

Requirements: Python 3, .NET 8 runtime/SDK files, native curl at `/usr/bin/curl`.
The tests create temporary CA/certificates and loopback listeners, then delete
their files. They never change system DNS, proxies or the machine trust store.

```text
python tests/LoginRecoveryChecks/run.py <dotnet> <csc.dll> <net8-reference-directory> [net48-reference-directory]
```

`Program.cs`/`Loopback.cs` cover native local HTTPS, fake DoH, retry rules,
cancellation and cache eviction. `Fixtures.cs` supplies framework/runtime
boundaries and a .NET 8 JSON adapter. The runner also compiles the actual
controller failover methods and the complete controller with `Api/` REST/model
fixtures. With .NET 4.8 references, complete production controller, RestHelper,
curl, DoH parser, managed HTTP and recovery files are compiled together.
