# Password change re-login checks

Run `python tests/PasswordReloginChecks/run.py [path-to-dotnet]` with .NET 8.
The runner compiles the actual acceptance, re-login, login and session-refresh
method bodies with fake UI/network/storage dependencies. No live API calls occur.

Checks cover immediate verification after 200/st=true/code=0, using the new
password including leading zeroes, preserving Remember preference, failed login
leaving the masked credential available for retry, persistence after successful
login, rejected changes, account switches, duplicate submission and stale refresh
responses during re-login.

Windows acceptance: open Settings > Change password, submit a valid change,
confirm both dialogs close and the existing account verification view starts.
Successful login should show the server list. If the queued password has not been
applied yet, the login form should keep the new password for manual retry without
resubmitting the password mutation. Verify Remember on/off and device-limit UI.
