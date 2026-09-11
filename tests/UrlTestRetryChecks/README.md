Run on Windows with the .NET SDK and .NET Framework 4.8 targeting pack:

```
dotnet run --project tests/UrlTestRetryChecks/UrlTestRetryChecks.csproj
```

The console checks link the actual production policy and message classes. They
cover the requested result matrix, 499/500/501 boundaries, equivalent URL
suppression, selective retries, missing/errored results, second-pass exceptions,
cancellation and final completion only after both passes. Progress checks cover
immediate first success, decreasing-only updates, rejecting foreign country tags,
preserving successful partial results after a timeout, and cancelling mid-pass.
No network is used by these checks.

Application acceptance:
- Start country-list tests with a web-service URL different from the retry URL.
  The first positive result should appear while other members are still testing.
  Faster successes should update its number/color without moving the row.
- After both passes finish, the row should move according to its final minimum.
  Keep the current selection and the Smart row at the top.
- Only failed/>500ms members should retry with
  http://connectivitycheck.gstatic.com/generate_204. Retries follow the completed
  primary batch. Existing concurrency and per-pass timeout stay unchanged.
- Switch protocol/service or connect while a country is testing. Queued updates
  from the cancelled batch must not update or reorder the replacement list.
- Test two countries consecutively: cached QueryURLTest results must not leak
  across rows. Each generated test config uses unique outbound tags.
- Confirm incompatible Xray candidates remain isolated by existing preflight and
  config-rejection handling. AI/Smart runtime pool settings are unchanged.

Progress uses the existing core QueryURLTest RPC every 150 ms, through a separate
local connection with a 250 ms connect limit and a 250 ms response deadline.
It does not issue additional Internet probes. If querying is unsupported or fails,
the application logs progress-unavailable and still accepts the final Test result.
Verify progressive snapshots against the bundled Windows core as well as the UI.

Neither these checks nor a Windows application build were executed in the
editing environment; Windows verification remains pending.

