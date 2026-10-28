# Windows Residential (1.4.54)

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

