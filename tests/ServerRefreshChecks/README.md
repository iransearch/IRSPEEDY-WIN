Run `python3 tests/ServerRefreshChecks/run.py /path/to/dotnet` with .NET 8.
The harness compiles the actual scheduler region and persistent cache against fake
UI/network dependencies, with a single-threaded synchronization context. It checks
live progress before RPC completion, repeated periodic success/failure/recovery,
cached UI restoration before cancellation drain completes, late queued progress,
API row replacement, initial enumeration, persisted rotation and connection suppression.
It does not exercise the Windows renderer, real Core RPC or Windows routing.

Manual Windows acceptance:
- Initial list checks all countries once.
- Connect during a test: probe drains before connection begins.
- Leave connected >15 minutes: no list probes; previous values remain on return.
- Disconnect: list appears immediately; no probe until cleanup completes.
- After cleanup: one country, then another after three minutes.
- Multiple rows in one country are checked together.
- API list refresh preserves measurements only for identical URL configurations.
- Rapid connect/disconnect and failed connection must not overlap a list probe with VPN.
- Include a fast successful member and a member that times out: the first ping
  appears while the remaining members/retry are still running. Ordering stays
  stable until the final result; the provisional status clears on completion.
- Cancel after a provisional ping appears: the cached result and availability
  return before connection startup. An old tooltip must not label it as a new test.
- Observe several rotation ticks without reopening the picker: each completed
  row updates, including a failed test followed by recovery.
