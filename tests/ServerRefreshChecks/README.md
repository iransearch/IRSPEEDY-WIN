Run `python3 tests/ServerRefreshChecks/run.py /path/to/dotnet` with .NET 8.
Run `python3 tests/ServerRefreshChecks/connection_handoff.py /path/to/dotnet`
to check the real connection handlers against delayed VPN cleanup and newer clicks.
Run `python3 tests/ServerRefreshChecks/url_priority.py /path/to/dotnet`
to check the production RPC's failed-first member ordering and stable fallback order.
The harness compiles the actual scheduler region and persistent cache against fake
UI/network dependencies, with a single-threaded synchronization context. It checks
initial live progress before RPC completion, disconnect-round success/failure/recovery,
cached UI restoration before cancellation drain completes, late queued progress,
API row replacement, initial enumeration, finite persistent queues, v1/v2/v3 cache
migration, ordered full rounds, rapid cancellation/resume and connection suppression.
Disconnect priority coverage uses the latest completed row result: all-failed,
unknown or incomplete rows first, followed by rows with at least one success.
It checks numbered rows independently, stable country/ID order within both groups,
success-to-failure history, persistence/reload, cancellation with partially mutated
members and pending order after results change or an API rebind. It also checks
that only disconnect rounds ask the RPC to prioritize failed server links, and
that a response lacking fresh results for every member keeps the last complete
row outcome for priority, including after URL reorder and reload.
It does not exercise the Windows renderer, real Core RPC or Windows routing.

Manual Windows acceptance:
- Initial list checks all countries once.
- Connect during a test: probe drains before connection begins.
- Leave connected >15 minutes: no list probes; previous values remain on return.
- Disconnect: list appears immediately; no probe until cleanup completes.
- After user disconnect cleanup: first check rows whose latest complete test had
  no success, including unknown or changed configurations. Then check rows with
  at least one success. Preserve country/ID order within each group, finish all
  links in each row, then move to the next row immediately. Failed links come first
  inside each row; the initial scan keeps its existing order.
- Multiple numbered rows in one country remain independent: a failed row comes
  first even when another row in the same country succeeded.
- After every country has finished, reopening the list or refreshing the API must
  not restart testing. There is no periodic server-test timer.
- Connect during a scan: stop it; ordinary failed-connect cleanup must not resume
  that scan. The next user disconnect starts a fresh round with failure priority.
- A canceled test or an older historical success must not replace the latest
  completed outcome used for priority. Reopening the app preserves that priority.
- If a core/RPC error prevents a full row test, retain its last complete outcome
  for priority until a later full test finishes.
- UI/API reordering and new results must not reorder a round already in progress.
- API list refresh preserves measurements only for identical URL configurations.
- Rapid connect/disconnect and failed connection must not overlap a list probe with VPN.
- During the initial scan, include a fast successful member and a member that times out: the first ping
  appears while the remaining members/retry are still running. Ordering stays
  stable until the final result; the provisional status clears on completion.
- Cancel after a provisional ping appears: the cached result and availability
  return before connection startup. An old tooltip must not label it as a new test.
- During disconnect rounds, the previous result stays visible until that row's
  full test completes, then updates/reorders, including failure and recovery.
