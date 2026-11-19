Run `dotnet run --project tests/DisplayProbeChecks` with the .NET 8 SDK.
The harness links the production scheduler, URL retry policy and disk cache.
Only the service/probe boundary is faked; no network, Core, credentials or WPF
desktop is required. Throne's separate patch contains its transport-level
single/warm-handshake checks. See [the display scan contract](../../docs/server-check-scheduling.md).
