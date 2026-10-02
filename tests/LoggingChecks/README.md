# Daily logging checks

Run `python3 tests/LoggingChecks/run.py` with .NET 8 on PATH. The runner isolates
local application data in a temporary directory and tests production logging code.

Checks cover the seven-date retention boundary, midnight rotation and cleanup,
unrelated files, concurrent writes, routine-message filtering, preserved errors and
pool diagnostics, error-reporting observation, and storage failure isolation.

Windows resolves the folder as `%LOCALAPPDATA%\IRSpeedyVPN\Logs\`.
Files are named `log-yyyy-MM-dd.txt`. Cleanup runs on the first write each day.
The legacy installation-directory `log.txt` is left untouched.
