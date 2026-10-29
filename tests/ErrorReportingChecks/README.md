# Self-contained Sentry exception diagnostics

Run on Windows with the .NET Framework 4.8 developer pack and .NET SDK:

```powershell
dotnet run --project tests/ErrorReportingChecks/ErrorReportingChecks.csproj
```

The checks link the production ErrorReporting implementation and the exact
Sentry 6.11.0 package. They do not initialize Sentry or send events. They cover
wrapper metadata, before-send filtering, request isolation, safe exception
status, severity, identity and endpoint aliasing.

Validation in the editing environment uses C# 7.3, real .NET Framework 4.8
references and Sentry's exact 6.11.0 net462 assembly. The net48-targeted test
executable is hosted on .NET 8, not Windows/net48. The expanded runner also
compiles and executes the actual startup tail/retry/annotation methods against a
real loopback protocol fixture. Full Windows build/native behavior and receipt
in Sentry still require a release smoke check.

After a Windows build, reproduce a failed server-list refresh and check Sentry:

- `diagnostic.schema=http-context-v2`, `build.mvid`, `installation.id`, `session.id`.
- `api.request_id`, `api.attempt1` through `api.attempt4`, endpoint aliases and flow budget.
- `http.stage`, elapsed/remaining transport budget, deadline flag and `web.status`.
- `curl.reason`, exit/native error and pre-fallback elapsed time.
- Selected mode, last observed active mode and observation age; a direct HTTP
  request means no explicit HTTP proxy, not that TUN routing is bypassed.

Installation identity is a random GUID in the existing per-user Sentry cache,
not an account or hardware identifier. Read-only profiles use a session identity
and advertise `identity.scope=session`. Clearing this cache resets identity.
No raw logs, URLs, query strings, headers, bodies, IPs or account credentials are
added to events. Existing reporting limits still apply. Previously received
events cannot be enriched retroactively.


## Core RPC diagnostics (1.4.61)

`python run.py DOTNET CSC NET8_REFS NET48_REFS SENTRY_NET462_DLL` additionally
compiles the actual production retry wrapper and startup annotator extracted from
TunnelPlusService, using inert app/lifecycle boundaries. Both .NET 8 and real
.NET Framework 4.8 reference builds use the exact Sentry 6.11.0 net462 assembly.
The net48-targeted executable is hosted on .NET 8 for offline execution on Linux.

The native loopback cases stall a real RPC response header/body, exercise Stop's
legacy deadline, and cancel a real Start socket. Full startup cases run the actual
identity/Stop/Start sequence with a timeout followed by accepted/rejected payloads. Checks verify typed failures,
measured RPC budget/stage/close reason, socket drain, retry success/failure, no
premature capture, deferred response validation, first-failure retention,
request isolation, grouping/severity and defensive before-send filtering.

The SDK project excludes OperationChecks.cs, which requires generated production
method extraction; it still runs the existing and native RPC/filter tests. The
Python runner executes every check. Neither path initializes Sentry or sends
production events. Full Windows build/native behavior and actual Sentry receipt
remain release smoke checks. See docs/sentry-core-rpc-diagnostics.md for fields.
