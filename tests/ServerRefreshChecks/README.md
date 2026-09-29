Run `python3 tests/ServerRefreshChecks/run.py /path/to/dotnet` with .NET 8.
Run `python3 tests/ServerRefreshChecks/connection_handoff.py /path/to/dotnet`
to check the real connection handlers against delayed VPN cleanup and newer clicks.
The harness compiles the actual scheduler region and persistent cache against fake
UI/network dependencies, with a single-threaded synchronization context. It checks
initial live progress before RPC completion, disconnect-round success/failure/recovery,
cached UI restoration before cancellation drain completes, late queued progress,
API row replacement, initial enumeration, finite persistent queues, v1/v2 cache
migration, ordered full rounds, rapid cancellation/resume and connection suppression.
It does not exercise the Windows renderer, real Core RPC or Windows routing.

Manual Windows acceptance:
- Initial list checks all countries once.
- Connect during a test: probe drains before connection begins.
- Leave connected >15 minutes: no list probes; previous values remain on return.
- Disconnect: list appears immediately; no probe until cleanup completes.
- After user disconnect cleanup: start at the first country, finish all its server
  links, then move to the next row immediately, in country/ID order.
- Multiple numbered rows in one country remain separate rows, checked consecutively.
- After every country has finished, reopening the list or refreshing the API must
  not restart testing. There is no periodic server-test timer.
- Connect during a scan: stop it; ordinary failed-connect cleanup must not resume
  that scan. The next user disconnect starts a fresh round from the first country.
- API list refresh preserves measurements only for identical URL configurations.
- Rapid connect/disconnect and failed connection must not overlap a list probe with VPN.
- During the initial scan, include a fast successful member and a member that times out: the first ping
  appears while the remaining members/retry are still running. Ordering stays
  stable until the final result; the provisional status clears on completion.
- Cancel after a provisional ping appears: the cached result and availability
  return before connection startup. An old tooltip must not label it as a new test.
- During disconnect rounds, the previous result stays visible until that row's
  full test completes, then updates/reorders, including failure and recovery.
