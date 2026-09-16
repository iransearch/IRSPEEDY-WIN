# Sentry error reporting

Sentry 6.11.0 is initialized once, after the single-instance check, with the configured project DSN. Existing WPF, AppDomain and task exception handlers remain responsible for application behavior. No new exception is marked handled.

## Reports

- Exceptions passed to LogHelper: type, HRESULT, method-only stack, fixed error category and release.
- Failed ProbeDetail attempts: numeric country/member IDs, protocol, phase and fixed error category. These describe individual attempts, not necessarily a final failed connection.
- Raw exception messages, local log files, credentials, server URLs, user identities and source file paths are not included. BeforeSend rebuilds events from allowed metadata.
- Duplicate reports are throttled for 10 minutes. Probe reports are capped at 20 per process; the rate-limit key table is capped at 100 entries.
- Logs, tracing, automatic sessions and automatic failed HTTP request capture are disabled.

Events use the SDK background queue. The local cache is under LocalAppData/IRSpeedy/Sentry and is limited to 30 items. Fatal error reporting and SDK shutdown can wait up to two seconds each. Network delivery is best effort.

## Verify on Windows

1. Restore/build the usual Release single-file package with UseCosturaSingleFile=true. Costura remains configured to embed managed dependencies.
2. Close any existing app instance, then run:
   `IRSpeedyVPN.exe --sentry-test`
3. Look for `IRSpeedy Sentry verification` in the configured Sentry project's Issues feed. The local "verification queued" line confirms queuing only, not server receipt.
4. Repeat with the SmartAssembly-protected executable. Supply Sentry and its restored transitive dependencies to SmartAssembly's analysis stage as required by the existing packaging workflow.
5. Verify normal login/probes and offline startup. A primary probe failure may be followed by a successful alternate probe; phase tags distinguish those attempts.

Keep release symbols and SmartAssembly mapping files for diagnosing obfuscated method names. This integration does not deobfuscate stack traces.

Windows build, protected single-file execution and live Sentry receipt must be verified on Windows; source checks alone do not establish these.
