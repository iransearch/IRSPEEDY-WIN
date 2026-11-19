# Display server scan checks

Run the scripts below from the repository root with .NET 8. Each takes the
path to `dotnet` as its first argument.

| Script | Coverage |
| --- | --- |
| `run.py` | Startup, login, disconnect and rebind against the production controller, scheduler and persistent cache. Retains coverage of v1/v2/v3 cache formats. |
| `manual_refresh.py` | API refresh and replacement, rechecking cached winners, skipped members, cancellation during fallback after a successful primary, and Residential catalog refresh. |
| `core_dispatch.py` | Production single-config runner and existing `UrlTestFull` with fake worker/RPC boundaries. Checks tag mapping, chain/SNI/Xray metadata, cancellation, port release and admission failures. |
| `api_refresh.py` | The production authenticated API phase and factory replacement with a delayed response, session and connection guards, and a shared maintenance gate. |
| `connection_handoff.py` | Production connection handlers with delayed VPN cleanup and newer clicks. |
| `url_priority.py` | Existing `UrlTestFull` member ordering and stable fallback order. |
| `net48_compile.py` | Controller, runner, picker, header and animation compile against .NET Framework 4.8/WPF references. |

Pass `--net48` to `api_refresh.py` or `core_dispatch.py` after the `dotnet`
argument for the .NET Framework compile check. `DisplayServerProbeChecks`
covers the scheduler's concurrency, deadlines, retries, tie-breaking and
display cache identity. These offline tests use fake configs and responses.

## Windows acceptance

- Start with no cached results. Check that independent rows begin testing
  within the global limit of eight. With eight eligible rows, each has
  one slot. With two eligible rows, each can use four slots.
- A measurement below 300 ms skips the waiting links in that row. A row
  reorders once its running probes finish. Successful rows sort by measured
  latency and title, checking rows follow, then rows without success. Smart
  and the last successful connection keep their existing positions.
- Log in again, restart and disconnect. Confirm successful rows retain pings
  and only failed, missing or changed rows are rechecked.
- In VPN and Residential, a manual refresh drains the old scan and prepares
  the catalog. It clears the bound rows' results before probing. The previous
  list is used if fetching fails. Each row shows its fresh result on completion.
  Search filtering does not limit testing. Repeat clicks share the refresh.
  Confirm the header icon's busy animation stops on completion and cancellation.
- Connect or log out while a probe is running. Check that the old scan
  drains, finished country results survive, and the interrupted country
  does not publish a late result. Ordinary failed-connect cleanup does not
  resume the scan. The next explicit disconnect retries eligible rows.
- Check Residential ping formatting while the ordering uses measured latency.
  Verify the existing selector, two themes and header placement on Windows.

A Windows desktop, packaged Core and live VPN are required for this acceptance.
