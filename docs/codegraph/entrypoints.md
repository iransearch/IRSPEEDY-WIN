# Entrypoints and primary flows

> Historical manually reviewed baseline notes. For current validity, stale edges and the analyzed commit, read [status.md](status.md), [automation.md](automation.md) and `manifest.json`.

All paths refer to `a57db82d05d3182eba7d98db5a47cf28d4d42f66`. These are verified static routes, not runtime traces.
Use the relation IDs / source witnesses in [dependencies.md](dependencies.md) and the manifest
before expanding a flow. Unlisted callers and branches remain unknown.

## Startup and UI

[IRSpeedyVPN/IRSpeedyVPN.csproj](../../IRSpeedyVPN/IRSpeedyVPN.csproj) sets `StartupObject=IRSpeedyVPN.Program`.
[IRSpeedyVPN/Program.cs](../../IRSpeedyVPN/Program.cs): `Main` calls `AppServices.InitializeFast`; `RunApplication` constructs
`MainWindow` and calls `PrepareFastStartup` before running WPF.
[IRSpeedyVPN/MainWindow.FastStartup.cs](../../IRSpeedyVPN/MainWindow.FastStartup.cs): `PrepareFastStartup` installs login/connection event handlers and sets
`Initialized` so the legacy `Window_Activated` heavy initialization path is suppressed in normal startup.
Do not choose the old Activated method as the sole entrypoint based on its XAML handler name.

## Login

[IRSpeedyVPN/UserControls/UCLogin.xaml.cs](../../IRSpeedyVPN/UserControls/UCLogin.xaml.cs): `btnLogin_Click` raises `OnCredentialEntered`.
[IRSpeedyVPN/MainWindow.xaml.cs](../../IRSpeedyVPN/MainWindow.xaml.cs): `UCLogin_OnCredentialEntered` → `Login` → `ProcessInfo` on accepted account data.
[IRSpeedyVPN/WebServices/NewServiceController.cs](../../IRSpeedyVPN/WebServices/NewServiceController.cs): `Login2` → `getServerList` → `ExecuteWithFailover` with one `LoginNetworkContext`
for the login. The request is `api/server/list/all`; successful encrypted response validation checks
the request ID and records `VerifiedServerList`.
[IRSpeedyVPN/WebServices/RestHelper.cs](../../IRSpeedyVPN/WebServices/RestHelper.cs) selects `CurlHelper.SendLogin` when `loginContext` is non-null.
See the DNS path below for transport fallback; do not infer safe replay from HTTP/network failure alone.

## Token / account-session validation

[IRSpeedyVPN/MainWindow.FastStartup.cs](../../IRSpeedyVPN/MainWindow.FastStartup.cs): `BeginAccountSession` / `ScheduleSessionMaintenance` → `SessionMaintenanceCallback`
→ `ValidateSessionAndRefreshServerList` → `RefreshAuthenticatedServerList` → `Login2`.
[IRSpeedyVPN/Common/AccountSessionMaintenance.cs](../../IRSpeedyVPN/Common/AccountSessionMaintenance.cs) owns a monotonic 30-minute deadline and independent account generation;
cached restoration requests immediate background validation, and an overdue busy retry waits 60 seconds.
Parsed, recognized 401/403/406 rejection or verified account invalidity calls `LogoutInvalidSession`;
`CompleteInvalidSessionLogoutAsync` drains probes, serializes with connection requests, disconnects,
detaches, then reaches the existing logout/renewal cleanup. Old account generations cannot end a new login.
Unknown, transient, unverified responses are not proof of account deletion.

`NewServiceController.CheckToken` defines `api/check/token` in [IRSpeedyVPN/WebServices/NewServiceController.cs](../../IRSpeedyVPN/WebServices/NewServiceController.cs). No edge connects it
to the active maintenance scheduler in this graph; the reviewed scheduler calls `Login2` instead.

## DNS Recovery: keep the three paths separate

1. **Credential login:** [IRSpeedyVPN/WebServices/CurlHelper.Login.cs](../../IRSpeedyVPN/WebServices/CurlHelper.Login.cs) → [IRSpeedyVPN/WebServices/LoginRecovery.cs](../../IRSpeedyVPN/WebServices/LoginRecovery.cs) (`SendResolverFirst`) → [IRSpeedyVPN/WebServices/LoginDnsCache.cs](../../IRSpeedyVPN/WebServices/LoginDnsCache.cs)
   → `FetchLoginAddresses` → [IRSpeedyVPN/WebServices/LoginPlainDns.cs](../../IRSpeedyVPN/WebServices/LoginPlainDns.cs) → [IRSpeedyVPN/WebServices/LoginDnsRace.cs](../../IRSpeedyVPN/WebServices/LoginDnsRace.cs). Classic UDP/53 queries for A/AAAA race
   across seven configured resolver IPs; first valid public answer wins, losing workers are canceled/joined.
   Lookup cap is 5 seconds, endpoint cap 20 seconds, overall login cap 45 seconds, recovery POST reserve 3 seconds.
   Curl pins the chosen set via `--resolve` with redirects disabled. System DNS fallback follows only eligible
   unsent failures; `MayHaveBeenSentFor` blocks replay after possible send. A cached failed answer can be invalidated.
2. **Other HTTP calls:** [IRSpeedyVPN/WebServices/RestHelper.cs](../../IRSpeedyVPN/WebServices/RestHelper.cs) may call [IRSpeedyVPN/WebServices/DohResolver.cs](../../IRSpeedyVPN/WebServices/DohResolver.cs) only with null login context, `!skipDoh`
   and available curl. Its Google DoH helper is not the credential DNS race.
3. **Inside VPN / Proxy:** [IRSpeedyVPN/Services/TunnelPlusService.cs](../../IRSpeedyVPN/Services/TunnelPlusService.cs) applies [IRSpeedyVPN/Services/SingBox/ParallelDnsRouting.cs](../../IRSpeedyVPN/Services/SingBox/ParallelDnsRouting.cs) before Core Start. This configures
   `vpn-dns-race`; actual native winner selection is outside this repository. Login DNS results do not
   establish tunnel DNS behavior. Proxy-only IPv4 policy is also configured here; do not alter it from this map.

`SendCore` also retains a SystemFirst/prefetch branch. Current production `LoginResolutionOrder` selects
ResolverFirst for normal HTTPS hostnames; the presence of `LoginPrefetch.cs` does not make that branch active.

## VPN Connection

[IRSpeedyVPN/MainWindow.FastStartup.cs](../../IRSpeedyVPN/MainWindow.FastStartup.cs) subscribes `OnConnectRequest`. [IRSpeedyVPN/MainWindow.xaml.cs](../../IRSpeedyVPN/MainWindow.xaml.cs):
`UCServerList_OnConnectRequest` → `ApplyConnectionRequestAsync` → `IVPNService.Connect`.
The shared connection gate drains probes and disconnects the old service before starting the selected service.
[IRSpeedyVPN/Services/ServiceFactory.cs](../../IRSpeedyVPN/Services/ServiceFactory.cs) creates group-backed services; the tunnel implementation is [IRSpeedyVPN/Services/TunnelPlusService.cs](../../IRSpeedyVPN/Services/TunnelPlusService.cs):
`Connect` → `RunV2ray` → config generation → `TryStartCoreWithConfig` →
[IRSpeedyVPN/Services/Libcore/LibcoreServiceClient.cs](../../IRSpeedyVPN/Services/Libcore/LibcoreServiceClient.cs) `StartWithDeadline` → [IRSpeedyVPN/Services/Libcore/ProtorpcClient.cs](../../IRSpeedyVPN/Services/Libcore/ProtorpcClient.cs) TCP/protorpc → external Core.
[IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs](../../IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs) starts a shared `NetworkRecoverySession` after connection. This graph does not
expand every Pool/Xray/Shield branch or claim the external Core's runtime behavior was tested.

## Residential VPN

[IRSpeedyVPN/Models/NewService/AccountInfoEx.cs](../../IRSpeedyVPN/Models/NewService/AccountInfoEx.cs) carries `residential_vpn`.
Normal login and cached startup call [IRSpeedyVPN/UserControls/UCServerList.Residential.cs](../../IRSpeedyVPN/UserControls/UCServerList.Residential.cs) `ConfigureResidential`.
[IRSpeedyVPN/Residential/ResidentialViewModel.cs](../../IRSpeedyVPN/Residential/ResidentialViewModel.cs) reads the root or nested user configuration, uses its default `DownloadAsync` fetch delegate,
parses the response through [IRSpeedyVPN/Residential/ResidentialSubscription.cs](../../IRSpeedyVPN/Residential/ResidentialSubscription.cs), and creates [IRSpeedyVPN/Residential/ResidentialService.cs](../../IRSpeedyVPN/Residential/ResidentialService.cs) country services.
`ResidentialService : TunnelPlusService` is the confirmed shared connection/recovery boundary.
Traffic comes from the subscription HTTP `subscription-userinfo` header and [IRSpeedyVPN/Residential/ResidentialModels.cs](../../IRSpeedyVPN/Residential/ResidentialModels.cs) `ResidentialTraffic.Parse`.
The view model retries/refreshes through its 30-second timer with a 10-second fetch cancellation budget;
`VisibleTraffic` controls whether [IRSpeedyVPN/Residential/ResidentialTrafficBar.cs](../../IRSpeedyVPN/Residential/ResidentialTrafficBar.cs) receives valid visible data.

## Shared row checking, ranking and light sweep

[IRSpeedyVPN/UserControls/UCServerList.xaml.cs](../../IRSpeedyVPN/UserControls/UCServerList.xaml.cs) `RefreshCountry` selects the active scope and binds its [IRSpeedyVPN/Services/ServerCheckCache.cs](../../IRSpeedyVPN/Services/ServerCheckCache.cs).
`RunBackgroundUrlTests` / `CheckServerAsync` consume the queue and update the active picker after each result;
connection/probe gates and cancel rollback stay in this shared code.
[IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs) calls [IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs) and creates a view model plus animator.
[IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs) publishes snapshots to [IRSpeedyVPN/Components/ServerListControl/ServerListViewModel.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListViewModel.cs).
[IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs) calls `TryBeginPass`, reveals row parts and calls `Finish`; `FinishPass` queues the next
prepare pass. The model re-syncs the latest data after finishing a frozen pass.
For changes to animation order, inspect both the ranker and the completed-row gating in `PresentationOrder`.
