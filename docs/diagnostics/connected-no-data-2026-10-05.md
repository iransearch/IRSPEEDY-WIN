# Connected UI, no received IP or usable traffic (1.4.6.7)

The supplied report is from **1.4.6.4**, before the public-IP routing, retry and
page-lifecycle fix. It shows an established Proxy connection, live Core and
loopback listeners, unchanged physical interface, outbound dial timeouts and an
IP lookup timeout before manual disconnect. It does not record the runtime
selector membership/health or the actual flow routes. This evidence does not
establish whether selection, Xray transport, routing or Windows socket filtering
caused the loss. The small UI byte total is not proof of end-to-end connectivity.

## Added Core observations

The application reads the existing **idempotent** `QueryAutoSelectors` and
`QueryConnections` RPCs. It never invokes `AutoSelectorAction`, URL tests or
`QueryStats` for diagnostics. Selector health checks, sampling, round-robin,
AI isolation, reconnect and UI connection-success behavior are unchanged.
The reviewed Throne-G Core already exposes these queries; no new Core API or
binary change is required for this reader.

One snapshot is requested three seconds after establishment. Further snapshots
are requested on the first failed IP provider, the final IP lookup failure,
an all-failed user TCP/TLS test, or a recognized Core dial/transport error.
There is no periodic status reader or added network health probe. Core error
bursts are sampled at most once per 30 seconds; explicit failures share a
two-second minimum interval. One snapshot can be in flight per service. Requests
use dedicated loopback RPC sockets with 750ms deadlines and a shared two-second
budget, outside the connection lifecycle lock. Stale results are discarded when
the connection changes or disconnects. An unavailable query cannot stop the VPN.

| Stage / field | Interpretation |
| --- | --- |
| `connection-state-begin/end` | Snapshot identity, fixed cause, service/connection, configured main/AI sizes. `relatedRequest` joins the public-IP request GUID. |
| `pool-state-summary` | Number of groups actually returned; an empty response is explicit. |
| `pool-state` | Main and AI reported separately: phase, suspended state, probed/alive/qualified/cooldown counts, TCP/UDP selection, rounds and ages. |
| `pool-member-state` | Member rank, active tier, qualification, probe RTT/sample counts, failed dials, last probe/success age, cooldown and classified last dial error. |
| `flow-state-summary` | Active and recently closed flow counts; cumulative counters for retained connections, not rates or session totals. |
| `flow-route-state` | Counts/bytes grouped by safe outbound chain, TCP/UDP and fixed IP-provider alias. `zeroDownload` means no received bytes recorded for those flows. |
| `pool-state-unavailable` / `flow-state-unavailable` | RPC failure, safe exception/socket code and elapsed time. Missing API is not an unhealthy/empty pool. |
| `core-output` | Fixed `failureReason`, generated `failureMembers` and existing request/fingerprint. Exact IP-provider route decisions are retained; ordinary browsing routes remain filtered. |
| `public-ip-attempt-failed/error` | Provider, round, elapsed time and inner socket/native error where available; exception messages are excluded. |

Raw domains, IPs, source addresses, user process names/paths, credentials and
Core errors are not copied into these records. Only generated tags and fixed
enums are shown; unknown tags/errors use fingerprints. Each snapshot caps groups
at eight, member rows at 64 per group, flow buckets at 20 and shown chain tags at
six, and records truncation. Selected/qualified members and IP-provider flows
have priority. Queue and category limits remain in place.

`alive` is not the same as `qualified`. In round-robin, `selectedTcp` is the
selector's reported selection, not proof that every flow uses that member;
inspect member counters and flow chains. A successful local SOCKS dial may
precede a downstream Xray transport failure. Flow chains depend on metadata
available in the packaged Core and are explicitly empty when unavailable.
`lastDialReason` describes the last recorded dial error; its presence does not
prove the member's current health. Missing timestamps have age -1. These are
observations, not a claimed repair of the unknown transport failure.

## Validation and next report

Portable checks exercise the production reader/decoder against a loopback RPC
fixture, unsupported APIs, deadlines/cancellation, late responses, rate limits,
large snapshots and privacy. Public-IP page tests verify one first/final snapshot
request across all provider failures. Traffic, startup/AI recheck, stop and log
retention regressions run separately. Production reader/RPC and public-IP UI
code also compile against real net48/WPF reference assemblies.

Windows VPN execution and complete packaging cannot be verified in the Linux
workspace. Rebuild **1.4.6.7** with the usual separately attached `Files.zip`.
Reproduce a failing connection, try opening a site, and collect the daily file
from `%LOCALAPPDATA%\IRSpeedyVPN\Logs\` after the failure. The final IP failure
snapshot appears within the existing 40-second lookup budget. If loss occurs
later with no Core error, the existing manual connection test can capture that
moment. Record when browsing stopped and when manual disconnect was pressed.
