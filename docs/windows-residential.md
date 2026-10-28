# Windows Residential (1.4.59)

## 1.4.59: traffic card reference redesign

The supplied glowing dashboard image is the visual reference: blue gradient shell,
yellow remaining-traffic ring with database glyph on the left, larger remaining
amount, muted usage line, consumption percentage and horizontal consumption track.
The later `IRSpeedy-TrafficCard-WPF-Matched.zip` supersedes the first ZIP, which
implemented a different full-card fill. Its native ring/bar layout, three-stop
blue/yellow palettes and storage glyph are adapted to the existing compact window;
demo bytes, fixed 420-DIP minimum, standalone window/slider and .NET 8 project are
not imported. Production remains .NET Framework 4.8, using the existing byte formatter.

The card is 100 DIPs tall (110 including its entrance gap), with a 58-DIP ring,
25-DIP radius, 5-DIP stroke and explicit edge clearance. Physical LTR columns keep
this reference orientation inside the RTL app. The existing shared byte formatter
still supplies actual remaining/used amounts. The ring shows remaining fraction;
the separate bar/percentage show the complement consumed. Unlimited traffic has
a full remaining ring, infinity label and zero consumption fill. Missing/invalid
traffic stays collapsed. The info glyph only explains the two measures on hover;
there is no new click action. The green in-use pill replaces it only while the
actual connection is Residential.

Initial display, later 250ms fade/height/translation entrances, reduced-motion,
account caching, fetch timers, cancellation, connection and recovery are unchanged.
Consumption fill follows resize and refresh, cancels stale width clocks, and has
no timer/render-loop subscription. No row or connection code changes are included.

## 1.4.58: ring bounds, centered ping and initial pool recovery

The 64-DIP traffic card previously left only 42 DIPs after its border and vertical
padding for a 44-DIP ring. Vertical padding is now 9 DIPs, leaving exactly 44;
the arc's 19-DIP radius and 5-DIP stroke also retain a half-DIP edge margin.
The remaining fraction is unchanged: a 98% arc intentionally leaves 2% unfilled.
Residential ping no longer has the -8-DIP name-line offset; it shares the flag's
vertical center. VPN row appearance and all row dimensions remain unchanged.

The supplied 1.4.57 log proves two different Turkey startup outcomes for the same
one-member main pool: first, the completed initial round marks smart-proxy-0 dead
with lastDialReason=timeout, zero alive/qualified members, and about 894 seconds
until the next round; public-IP attempts fail with ConnectionReset. AI members
are healthy independently. After manual reconnect, the main member is healthy
(1129ms) and api.ipify.org returns HTTP 200 in 819ms. These observations do not
identify the underlying cause of that first timeout, or prove an ISP DNS block.

A Residential-only background worker now observes the initial round for up to
20 seconds, waits for all probes to finish, and if every main member failed,
gives the existing `proxy` selector one scoped recheck after a one-second grace.
It revalidates status/generation under the existing lifecycle gate, never restarts
Core or changes country, and never rechecks AI. Healthy, suspended, incomplete,
missing/unsupported status and cancellation do not trigger recovery. Query/action
RPCs retain 750ms bounds; disconnect/replacement invalidates the worker. A busy
lifecycle gate skips recovery immediately rather than leaving a blocked worker. One
recheck is a mitigation for transient startup failure, not a guarantee that an
unreachable endpoint will connect. Existing periodic policy remains unchanged.
Diagnostic events record accepted/unavailable/stopped results without links or
credentials. Verify the second round and real connectivity on Windows/affected ISP.

## 1.4.57: contiguous location metadata

Residential location metadata now uses a content-sized, right-aligned LTR Grid
with two columns (`Auto`, `*`), rather than the full-width RTL three-column layout.
The untrimmed city and conditional separator occupy the first column; only the
ISP in the second column can ellipsize. Natural-width rows have no space between
these parts. The existing 66-DIP row height, ping/radio offsets, selection and
light-sweep ownership are unchanged. Very long cities can exhaust the available
line width; they are never deliberately ellipsized to make room for an ISP.

`LocationLine`, `LocationCity` and `LocationSeparator` share model formatting for city + ISP,
city only, ISP only and empty values. The same immutable row snapshot carries these parts;
there are no XAML separator conditions. City stays muted and the separator/ISP use #8793B3. `DisplayIsp` continues removing ASN and
trailing delimiters. RowToolTip retains the full original ISP including ASN.
Offline checks cover all four combinations, actual GroupItem forwarding and the
new two-column XAML contract. WPF rendering remains a Windows smoke check.

## 1.4.56: shared VPN probe and selection policy

The superseded 1.4.55 selectable-failure and row-retry policy has been removed.
There is no fastest badge or IsFastest row-model state. The optional Residential
initial auto-choice remains a controller concern, not row presentation.

| Area | VPN behavior and Residential alignment |
| --- | --- |
| Queue and scheduling | Same scoped bootstrap queue, sequential country worker, initial shuffle, failed-first disconnect order, member concurrency and retry policy. Separate caches and once-per-login scope initialization remain. |
| Queued row | No positive result: disabled, arrow cursor, muted dash. |
| Checking | Cached/current positive result remains visible and selectable. Without one, Residential displays the explicitly requested `در حال تست…` and stays disabled; VPN retains its checking flag sheen/dash. Real positive member progress follows the same shared eligibility rule. |
| Success/failure | Positive current/cached result permits selection. Failed current results disable/dim the row even if display-only success history exists. VPN's muted dash and failure tooltip are used; the orange failure label is removed. |
| Selection input | The same live GroupItem gate rejects mouse, Enter and Space on unhealthy rows without changing the selected card/service. WPF also skips disabled rows during keyboard navigation. |
| Selected row later fails | VPN preserves its existing selection; Residential also preserves it, but its Connect action is disabled with `یک سرور انتخاب کنید` until the selection has a positive result, as explicitly requested. VPN Connect/default-selection behavior is unchanged. |
| Ordering and animation | Both use IsSelectable to partition completed successes/failures. Only completed countries move; pending order, star pin and the shared light-sweep planner remain unchanged. HasSuccessfulTest is removed. |
| Header refresh | Both drain work, fetch current service data, clear/reset only after a successful fetch, then use the shared ServerRefreshPlan. Residential fetches its subscription rather than the VPN account endpoint. A failed fetch retries the old list and preserves its previous results until probes finish. No row retry handler exists. |
| Connection gating | One shared probesRequireDisconnect gate and CurrentService check; per-tab consumed-state overrides are removed. Neither tab probes during a live connection. |
| Cancel / tab switch | Same cancel, drain, committed-result rollback and resume path. A manual subscription refresh joins an existing GET, honors cancellation and does not start a parallel request. |
| Default selection | VPN retains its global Smart card and existing selection rules. Residential's retained auto-choice considers only positive results in rows that are not checking, and never replaces an existing manual choice. A last-connection star alone does not preselect an untested country. |

The test-specific data/presentation differences are the 0.7 Residential display
factor, city/ISP metadata, the explicitly requested checking text, and the retained
healthy initial auto-choice. Country pools remain protocol-compatible with the
existing Windows Core. The restored status-page route and the remaining quota,
keyboard focus, country-name, radio and footer presentation are unchanged.

Scoped caches preserve committed results, fragment-only metadata normalization
and migration of prior full-URI fingerprints. The log analysis from 1.4.55 remains:
the supplied log loaded four countries but contained only canceled VPN probes;
it did not prove Residential endpoint failure or deletion of cache files.

Tests execute the production row mouse/key handlers and initial auto-choice, real
persisted caches, forced subscription refresh/failure/cancellation and the shared
planner. All relevant UI C# is also compiled against .NET Framework 4.8/WPF
references. Actual Windows rendering/BAML/input automation and live Core probes
remain manual checks.

## 1.4.54 status page and row corrections

Successful Residential connections now open the existing `UCUserInfo` status page,
just like VPN. Remove the Residential-only navigation override that kept the
chooser visible. Existing success/cancellation guards, four-second presentation,
public IP lookup, status timer, disconnect and recovery flow are unchanged.
The status page applies the same 0.7 Residential display factor as the list;
underlying measured values and VPN display remain unchanged.

Remove the fastest badge and its unused snapshot/ranking notifications. Country
sorting, pinned star, selection and sweep behavior remain unchanged. The city/ISP
line is right-aligned LTR so English city names, provider names and ASN parentheses
are displayed in their original order, with ellipsis at the line's end.

## 1.4.53 loading correction

The remembered-account path (`TryApplyCachedAccountAsync`) bypasses `ProcessInfo`.
1.4.52 configured Residential in manual login and maintenance refresh but omitted
this cached startup path, leaving the repository unconfigured after reopening the
app. It now configures Residential before showing the chooser. If an older cached
account has no `residential_vpn` object in either supported location, schedule one
background refresh through the existing authenticated API and `serverRefreshGate`;
do not wait for that request in startup or repeat authentication in a loop.

Match the subscription parsing in Android commit
`1864ddaedf7f79f564b19bee1d54f425c5b2aaa1`: remove UTF-8 BOM before plain/Base64
detection, parse the connection URI separately from display/private fragment tags,
and preserve complete original links. HTTP gzip/deflate decompression is enabled.
Publish every parsed country independently of Windows pool protocol compatibility;
unsupported links are excluded only from executable pool candidates. Missing quota
metadata never prevents publishing the country list. Explicit JSON wire names and
rename exclusions protect the new subscription settings in packaged builds.

Sanitized diagnostics identify configure, download, HTTP status/byte count,
parse, published-country count and failures. No URL, token, node link, account
payload, exception message or credentials are logged. The original screenshot
alone cannot establish whether the live account additionally has an API, network
or subscription-format failure; these stages make that distinction visible.

## Data and connection contract

The Android source on `vless-xhttp-download-tls-fix` is the contract reference:
`ResidentialConfig`, `ResidentialSubscriptionClient`, `ResidentialSubscription`,
`ResidentialRepository`, and `ResidentialAccessPolicy`.

Read `residential_vpn.enabled` and `residential_vpn.subscription_url` from the
account root, falling back to `user_data.residential_vpn`. ECO group 39 or a whole
ECO/اکو token in the group name is excluded. No endpoint or credentials are invented.

Fetch the supplied HTTPS subscription using a separate credential-free GET,
`Accept: text/plain`, and `Cache-Control: no-cache`. Preserve certificate checking,
reject userinfo/fragments and non-HTTPS redirects, allow at most three redirects,
and cap the complete download at 10 seconds / 4 MiB / 2048 entries. Subscription
URLs and node credentials are never added to Residential diagnostics.

Accept plain or standard/URL-safe Base64 URI lines. URI fragments carry
`city, ISO-country / ISP` metadata; `UK` maps to `GB`. Country pools deduplicate
connection URIs but preserve complete original links, including XHTTP headers.
Traffic comes from `subscription-userinfo`: upload + download = used; total = 0
means unlimited. Missing, malformed, negative or duplicate quota fields hide the
bar. Saturating addition prevents overflow. Quota exhaustion blocks a new
Residential connection; it does not silently disconnect a live session.

Windows reuses `TunnelPlusService`, the normal serialized connection request,
protocol builders, routing, Shield, tests and country pool. No Core, Shield or
VPN DNS race changes are included. **Current compatibility limit:** the Windows
country pool accepts VLESS, Trojan, VMess, ss and Hysteria2/hy2. Android's AnyTLS,
TUIC, SSH and `shadowsocks://` alias are not advertised as connectable pool links
on Windows; they require separate transport work. Their countries remain visible,
and availability is determined by compatible Windows candidates and existing
probes. Unsupported links are counted without logging their values. No synthetic
servers or fake traffic are shown.

## State, presentation and lifecycle

Both tabs instantiate the same `ServerCountryPicker`, `ServerListViewModel`,
`ServerListSweepAnimator` and row template. Numeric row part IDs and the planner's
cycle / completion / pinned-star rules are unchanged. Residential metadata travels
in immutable snapshots with the name part of each sweep. Raw probe values and
the VPN list remain unchanged; Residential text and ranking use
`round(measured * ResidentialPingDisplayFactor)` with factor 0.7 (Android rounding).

Tab changes pause and drain tests, retain independent selections and search the
active list. They never invoke Disconnect. Async result/rollback handlers capture
their picker and cannot update the other tab. A generation guard also handles
rapid tab reversals. The last successful service kind is restored per account;
Residential star identities additionally include the account/subscription scope.

Traffic loads after account startup and on tab/connection entry. Fresh data is
shared in memory for 30 seconds. Periodic retries run every 30 seconds after a
failure, while the tab is active, or while actually connected to Residential.
Successful quota-only refreshes retain service objects, their ping results and
selection. Logout, password re-login, account changes and application exit cancel
downloads and clear account state; late responses cannot repopulate it.

The traffic control owns its visibility history and animation cancellation.
Initial valid display is immediate; later entrances/connection state changes use
250 ms ease-out, opacity and a -6 DIP translation. Height also transitions, so the
hidden bar reserves no space and the list does not jump on later entrances.
System reduced-motion settings and unloading stop clocks. The ring shows the
remaining fraction clockwise from 12 o'clock; unlimited quota shows a full ring.

Actual connection kind, independent of selected tab, controls the green badge.
Both Residential and VPN open the existing connection-info view after success.
Both legacy and fast-startup event paths wire Disconnect. Existing four-second
minimum Connecting presentation remains unchanged.

## Automated verification

`tests/ResidentialChecks/run.py DOTNET CSC NET8_REFS NET48_REFS` runs offline:

- 29 subscription, quota, formatting, endpoint validation and ECO model checks,
  including BOM-prefixed plain/Base64 and the exact Android private-tag fixture.
- Repository/ping checks: account cancellation, late results, shared cache,
  retained selection objects, logout, ECO, hidden failure, timer retry, real
  GroupItem Residential conversion, unchanged VPN pings, transport limits,
  complete country publication and independent missing quota metadata.
- Real loopback HTTPS GET through the production repository/download/parser:
  gzip + BOM + Android metadata publishes two country pools and quota. A temporary
  local CA is installed in the test process user's certificate store and removed
  afterward; ordinary certificate/hostname validation stays enabled. Cancellation
  closes the real socket and drains the test server worker.
- Compiles production Residential code against actual .NET Framework 4.8 / WPF
  references, then production picker, row planner, sweep animation and UCServerList
  partials against the same references with inert existing service boundaries.
- XML, immutable binding, keyboard, automation and connection-wiring contracts.
  The cached startup guard verifies configuration before list display and a
  background gated metadata refresh for pre-Residential cached payloads.

Also run `tests/ServerListTransitionChecks/run.py`, `bindings.py` and
`cycle_wiring.py`. The planner/timeline suite covers 720 permutations, rapid
result churn, pinned rows, stale generations and 4096-row passes. Test doubles
represent only existing service/account boundaries and WPF value types in the
Linux state-machine runner; they are not a Windows app build or visual test.

## Required Windows smoke checks

Build the full application on Windows with the .NET Framework 4.8 targeting pack.
The Linux checks do not compile BAML, render WPF, exercise the packaged Core or
prove a live authenticated subscription connection.

1. At the existing 420×700 design size and supported scale factors, inspect both
   tabs, selection, 34-DIP flags, city/ISP truncation, ring and status badge.
2. Test Tab/Enter/Space and visible keyboard focus; inspect Automation names for
   both tabs, selected rows and remaining percentage. Verify RTL and LTR runs.
3. Test successful/failed/cancelled Residential connects in proxy and TUN modes,
   switch tabs during a live session, and disconnect through the chooser. Confirm
   Shield and ordinary VPN/Smart connections still behave as before.
4. Finish countries out of order; verify only completed countries move with the
   shared sweep, star stays first, selected identity survives and 0.7 is applied
   once. Switch tabs rapidly during tests and during manual refresh.
5. Exercise absent/invalid traffic headers, quota exhaustion/unlimited quota,
   10-second GET timeout, 30-second retry, logout/account switching during GET,
   subscription replacement and certificate failure.
6. Check initial/cache display, later hide/recovery and connection entrances with
   animations both enabled and disabled; confirm no reserved blank space, list
   jump, stale animation or timer after logout/exit.

The authenticated visual reference could not be inspected without signing in.
The implementation follows the user's approved textual sizes, colors and behavior.

