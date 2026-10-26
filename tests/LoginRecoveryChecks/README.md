# Offline Windows login recovery checks

See [the implementation and validation notes](../../docs/windows-login-recovery.md).

Requirements: Python 3, C compiler (`cc`), .NET 8 runtime/SDK files, native curl at `/usr/bin/curl`.
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

`RegressionChecks.cs` covers HTTPS/HTTP replay evidence, managed phases, static
curl availability, trailer/argument robustness and native exits 6/7; `dns_fail.c`
keeps the real-curl .invalid check offline. `PrefetchChecks.cs` covers provider
racing, invalid winners, cancel/join cleanup, speculative cache lifecycle and
credential serialization. `ui_lifecycle.py` executes the actual presentation
wrapper against a fake dispatcher for shutdown and generic timeout errors.
The runner also executes existing password re-login and loading-stage checks.

`ImprovementChecks.cs` covers the three-second POST reserve with slow loopback
HTTPS, alternate-peer remote_ip gating, cached peer filtering, bounded native
process/pipe cleanup and fail-closed credential blocking on unconfirmed exit.

`PendingStopChecks.cs` checks real handle closure without a subsequent login,
owner handoff, live-process retention and timer cleanup/re-arming. Child-process
probes exercise the actual ProcessExit hook and explicit shutdown, including late
registrations, without resetting the production shutdown latch.

`DnsFailureChecks.cs` reproduces the Windows exit-6 positive-pretransfer trailer,
checks each contradictory indicator/unknown metrics and HTTPS-only scoping,
executes a verified native HTTPS POST after recovery, and checks actual
Failure()/controller failover with failed DoH and a DNS-failed recovery attempt.

`DohFirstChecks.cs` exercises the temporary production DoH-first flag, native
HTTPS via --resolve and sequential system fallback, cache refresh, strict TLS,
post-send/partial HTTP replay rejection, cancellation and shared POST gating.
The system-first checks explicitly retain the constructor's reference policy.
