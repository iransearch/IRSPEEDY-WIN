# Windows account/session maintenance (1.4.70)

This follows Android commit `d466fe60f1570e5e577ddf3e548d4958bbf76cc0`:
account validity outlives the requesting view and VPN connection, while the
check deadline belongs to the account session rather than screen lifecycle.

## Identity and response handling

`AccountSessionMaintenance` owns an independent generation. New online or
cached accounts begin a generation; logout, password re-login, a new login
attempt and application shutdown invalidate the previous one. Username/password
checks remain, so a response from an old login cannot end a new login even if
the new credentials are identical. VPN connect/disconnect and page changes do
not advance this account generation.

The existing authenticated Login2 server-list request is reused. For a current
account, recognized 401/403/406 API replies (a parsed message) and verified
encrypted expired/invalid-account replies end the session before testing the
connection/view publication guards. HTTP 200 encrypted data must have passed
the controller's existing request-ID check; the internal VerifiedServerList
flag carries that result without changing network requests or wire payloads.
Valid data still cannot replace a newer connection or update a stale view.

DNS/timeouts/cancellation, 5xx, unrecognized/HTML/empty HTTP errors, unreadable
account data and AuthServerError preserve the account. Missing local credentials
are not treated as server proof of deletion. HTTP statuses remain final or
retryable exactly as before in the login transport/controller; no POST replay
rule, DNS order, TLS verification, endpoint, budget or credential payload changes.

## One monotonic deadline

The normal interval remains 30 minutes. An online-verified account begins with
that deadline. An account restored from cache schedules its first validation
immediately in the background, after the cached chooser is prepared; it does
not add a startup wait or change the four-second presentation. This same request
also refreshes legacy payloads missing Residential metadata.

One one-shot timer uses the remaining monotonic deadline. Opening/reopening the
chooser, reconnecting and resuming cannot buy a new 30 minutes. Only an accepted
authenticated request advances the deadline, including header refreshes; network
failure after acceptance retains that normal interval, as in Android. A busy
server-refresh gate or unavailable local credentials does not advance it; an
overdue maintenance check retries after one minute without spinning. Canceled or
ended accounts and queued callbacks after shutdown cannot issue requests.
The existing explicit local/oneclick mode remains excluded from online checks.
Residential quota-only subscription GETs do not advance the account deadline.

## Termination and connection ownership

Definitive rejection immediately marks the account logged out, stops maintenance,
invalidates queued connection requests and cancels a captured TunnelPlusService
startup/recovery. Both ordinary VPN and Residential use this same path.
Probe work is paused/drained, then the existing connection semaphore serializes
cleanup. Disconnect runs on a worker so an in-flight Core startup cannot deadlock
the Dispatcher; proxifier detach is attempted independently. Only afterwards are
saved credentials removed and the existing login/expired-account view shown.
The existing renewal path tolerates missing optional shop settings.

Account generation and credentials are checked again after waiting/cleanup.
An old rejection cannot clear a replacement account or navigate a closing app.
Cleanup exceptions are logged and do not escape the asynchronous task; failed
probe drain or disconnect/detach callbacks do not suppress the remaining logout
steps. A logged cleanup failure does not prove the native resource stopped;
verify actual Core/proxy teardown in Windows acceptance.

`[SessionMaintenance]` diagnostics contain only fixed stages/reasons, generation
and booleans. They contain no username, password, token, response body or node
link. Existing HTTP/transport diagnostics remain available. UI has no new
technical errors or banners. Shield, routing, the confirmed Proxy IPv4-only
policy, seven-provider VPN DNS race, Pool and connection recovery are unchanged.
No Core source or payload rebuild is required.

## Verification

Run the complete LoginRecoveryChecks runner plus
`tests/PasswordReloginChecks/run.py DOTNET CSC NET8_REFS NET48_REFS` and
ResidentialChecks. The authentication fixture executes production deadline,
refresh, logout, password and asynchronous cleanup code with inert platform
boundaries and compiles the same methods against .NET Framework 4.8 references.
Controller tests cover matching/stale encrypted request IDs and authorization
reply counts; native transport tests retain the existing no-replay/TLS rules.
These checks do not build/render the complete Windows app or call a real account.

Windows acceptance: remove/expire an account while connected in Proxy and TUN,
including Residential; confirm a recognized reply stops the service and opens
login. Repeat during startup and header refresh, with a connection/page switch
while validation is pending. Reopen the chooser before minute 30 and confirm the
original deadline remains. Restart with a readable deleted-account cache and
confirm immediate background validation. Compare temporary API outage and a
new login with identical credentials; neither may be revoked by an old result.
