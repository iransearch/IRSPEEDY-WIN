# Password re-login and session-maintenance checks

Run `python tests/PasswordReloginChecks/run.py [path-to-dotnet]` with .NET 8.
For offline compilation without restore, pass `DOTNET CSC NET8_REFS [NET48_REFS]`.
The runner compiles the actual acceptance, re-login, login and session-refresh
method bodies, the account deadline helper and asynchronous invalidation/Logout
with fake UI/network/storage/clock dependencies. No live API calls occur.

Checks cover immediate verification after 200/st=true/code=0, using the new
password including leading zeroes, preserving Remember preference, failed login
leaving the masked credential available for retry, persistence after successful
login, rejected changes, account switches, duplicate submission and stale refresh
responses during re-login.

Session cases cover same-account rejection after connection/view changes;
old results after a replacement login with identical credentials; verified versus
unrecognized/HTML/empty HTTP replies; expired/deleted accounts; transient auth
service outages; probe/startup drain, connection-gate serialization and cleanup
failures; stop-before-cache-clear ordering; late shutdown results; immediate
cached startup, preserved minute-30 deadline, accepted manual requests and
one-minute busy-gate retries. An optional .NET 4.8 compile checks the same
production method bodies/helper against real framework references.

The complete LoginRecoveryChecks runner also verifies production controller
request-ID flags and unchanged one-POST authorization handling with an inert
transport/decryptor boundary. Windows Dispatcher/Core teardown and live API
account-deletion acceptance still require a packaged Windows smoke test.

Windows acceptance: open Settings > Change password, submit a valid change,
confirm both dialogs close and the existing account verification view starts.
Successful login should show the server list. If the queued password has not been
applied yet, the login form should keep the new password for manual retry without
resubmitting the password mutation. Verify Remember on/off and device-limit UI.
