# Persistent country checks

- Cached per Windows user, account username and service/protocol scope in LocalAppData/IRSpeedy/ServerChecks. Password changes retain the cache. Only hashed configuration identities, times, latency results and the pending country are written; no raw subscription URLs or credentials.
- Load cached results before binding the picker. New or changed configurations start untested; removed records are pruned. An invalid/missing cache safely starts fresh.
- At app opening and after connection cleanup, test the pending country immediately. A country includes all of its numbered service records (e.g. Germany 1 and Germany 2). On first use, all countries are tested sequentially without the three-minute interval. Completed bootstrap countries and overall bootstrap completion are persisted; interruption resumes at the unfinished country. Existing caches from the earlier rotating-only version infer bootstrap progress from complete country results.
- Advance to the next country only after the entire country completes. After bootstrap completes, repeat three minutes after completion while disconnected and logged in, including when the window is hidden/minimized. Logout and connection startup pause probes. No background list probes during a VPN connection.
- Connect cancels/drains the active probe and preserves an unfinished country's turn. Reloads of the API list cancel obsolete probes; their callbacks cannot update replacement rows.
- Completed failed probes keep their last successful ping separately and display “last” and “latest test failed”. Historical success does not make a failed row selectable. Successful results show their timestamp in a tooltip.
- Results and cursor use atomic file replacement after completed service/country tests. Failure to write cache does not stop in-memory operation. Abrupt termination before a country commits may repeat that country on restart.

Validation: tests/ServerRefreshChecks/run.py exercises the production scheduler/cache with fake network/UI and real temporary files. tests/PasswordReloginChecks/run.py covers password-change re-login integration. Actual Windows UI/runtime testing remains necessary.
