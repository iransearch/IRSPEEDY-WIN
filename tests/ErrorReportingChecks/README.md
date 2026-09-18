# Self-contained Sentry exception diagnostics

Run on Windows with the .NET Framework 4.8 developer pack and .NET SDK:

```powershell
dotnet run --project tests/ErrorReportingChecks/ErrorReportingChecks.csproj
```

The checks link the production ErrorReporting implementation and the exact
Sentry 6.11.0 package. They do not initialize Sentry or send events. They cover
wrapper metadata, before-send filtering, request isolation, safe exception
status, severity, identity and endpoint aliasing.

Validation in the editing environment: ErrorReporting and these tests compiled
with C# 7.3, real .NET Framework 4.8 references and Sentry's net462 assembly. All
13 checks passed when that executable was hosted on .NET 8 (not Windows/net48).
HttpFallback, ConnectionDiagnostics and the tunnel diagnostic partial also
compiled against net48 with fixtures for the surrounding application types.
All six changed production files passed Roslyn C# 7.3 syntax checks. Full Windows
build, native runtime behavior and actual receipt in Sentry still require validation.

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
