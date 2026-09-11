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
Scheduling checks cover A1/B1/C1 before A2/B2/C2, unequal country sizes,
completion only after the last member, preserved country minima between rounds,
retry completion before the next country, member failure isolation and cancellation.
No network is used by these checks.

Application acceptance:
- Preserve the web-service order of URLs within each country. Test the first URL
  of each country, then the second URL of each country, and so on. Countries with
  fewer URLs drop out after completion; cached fresh countries need no new tests.
- Start country-list tests with a web-service URL different from the retry URL.
  The first positive result should appear while other members are still testing.
  Faster successes should update its number/color without moving the row.
- After all members and their retries finish, move the row by its final minimum.
  Do not reorder when an individual server finishes if that country still has
  more servers. Keep the current selection and the Smart row at the top.
- Only failed/>500ms members should retry with
  http://connectivitycheck.gstatic.com/generate_204. Each scheduled call contains
  one server; its conditional retry completes before the next country starts.
  The per-request timeout and retry threshold are unchanged.
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
