Run on Windows with the .NET SDK and .NET Framework 4.8 targeting pack:

```
dotnet run --project tests/UrlTestRetryChecks/UrlTestRetryChecks.csproj
```

The console checks link the actual production policy and message classes. They
cover the requested result matrix, 499/500/501 boundaries, equivalent URL
suppression, selective retries, missing/errored results, second-pass exceptions,
cancellation and publication only after the second pass returns. No network is
used by these checks.

Application acceptance: start the country-list tests using a web-service probe
URL different from the retry URL. Verify only failed/>500ms tags are retried with
http://connectivitycheck.gstatic.com/generate_204. The core API returns a batch;
the second pass follows that response immediately, without an extra delay. The
normal concurrency and per-pass timeout remain unchanged. Country refresh and
sorting occur after UrlTest returns the final merged results. Cancelled batches
must not publish their results. Confirm incompatible Xray candidates remain
isolated by the existing preflight/config-rejection handling.

Neither these checks nor a Windows application build were executed in the
editing environment; Windows verification remains pending.
