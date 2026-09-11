Run on Windows with the .NET SDK and .NET Framework 4.8 targeting pack:

```
dotnet run --project tests/UrlTestRetryChecks/UrlTestRetryChecks.csproj
```

Checks link the production retry policy, scheduler and RPC message classes.
The existing retry matrix, 499/500/501 boundaries, equivalent URL suppression,
partial minima, config rejection and cancellation checks are retained.
Transport I/O/socket failures also take the alternate-URL path.

Scheduler checks use synchronization gates, without Internet requests, to prove:
- Five first-round countries can be inside a test at the same time.
- A sixth cannot start until a slot is released, then fills that slot immediately.
- No second-round member starts while first-round members remain active.
- One country and one worker/core slot never overlap themselves.
- Country minima survive rounds; completion occurs once, after the last member.
- Cancellation stops queued work and suppresses progress/completion; all active
  workers drain before cleanup. Existing serial ordering checks run with limit 1.

Windows application acceptance:
1. Use at least six countries with different numbers of servers. Confirm five
   overlapping member-start/member-end pairs (slots 0-4) and up to five distinct
   worker-core-ready ports in the log. These belong to separate owned processes,
   not the normal fixed-port connection core. Cores are reused across rounds.
2. Confirm first servers of all countries finish before any second server starts.
   Within a round, a free slot immediately takes the next country. Each server's
   alternate-URL retry belongs to the same slot and finishes before it is reused.
3. Confirm first successful latency appears promptly; only smaller successes
   replace it. A country moves only after its last server and retry finish.
4. Preserve the web-service-first, failed/>500ms alternate-URL rules. A latency of
   500 or less and an equivalent initial URL must not trigger a duplicate probe.
5. Exercise mixed native, Xray and SNI members. Unique test tags and core ports
   isolate RPC results. SNI configs/ports are unique and cleanup kills only the
   owned SNI process, so one country cannot stop another country's SNI process.
6. Switch lists or connect mid-test. The previous batch must stop and drain its
   processes/ports before a new batch acquires the five slots. Verify the runtime
   connection remains separate and no cancelled row update is applied.
7. Try a rejected config, unavailable core, core exit and RPC timeout. Failures
   affect their own member; other countries continue. Check all test processes
   exit on completion/cancellation and no sixth test process is started.

The main pool/AI settings and single-file packaging are unchanged. Up to five
existing bundled core processes are used temporarily, so Windows CPU/memory and
real probe latency must be checked on target machines, including Windows 7.

C# syntax was checked in the editing environment. The executable checks, full
Windows build and bundled-core acceptance above still require Windows validation.
