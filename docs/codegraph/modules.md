# Module map

Baseline and validity: [overview.md](overview.md). Paths are repository-relative;
each item is a navigation starting point, not a claim that every method in that file is covered.
Canonical verified edges and their source lines are in [manifest.json](manifest.json).

| Module | Start here | Confirmed boundary | Tests to inspect before changing behavior |
| --- | --- | --- | --- |
| Login | [IRSpeedyVPN/MainWindow.xaml.cs](../../IRSpeedyVPN/MainWindow.xaml.cs)<br>[IRSpeedyVPN/WebServices/NewServiceController.cs](../../IRSpeedyVPN/WebServices/NewServiceController.cs)<br>[IRSpeedyVPN/WebServices/RestHelper.cs](../../IRSpeedyVPN/WebServices/RestHelper.cs)<br>[IRSpeedyVPN/WebServices/CurlHelper.Login.cs](../../IRSpeedyVPN/WebServices/CurlHelper.Login.cs) | UCLogin event → MainWindow.Login → Login2/server-list request → login-specific curl transport | [tests/LoginRecoveryChecks/run.py](../../tests/LoginRecoveryChecks/run.py)<br>[tests/LoginPresentationChecks/run.py](../../tests/LoginPresentationChecks/run.py) |
| Token / session validation | [IRSpeedyVPN/MainWindow.FastStartup.cs](../../IRSpeedyVPN/MainWindow.FastStartup.cs)<br>[IRSpeedyVPN/Common/AccountSessionMaintenance.cs](../../IRSpeedyVPN/Common/AccountSessionMaintenance.cs)<br>[IRSpeedyVPN/WebServices/NewServiceController.cs](../../IRSpeedyVPN/WebServices/NewServiceController.cs) | Active maintenance validates through Login2; CheckToken is a separately defined endpoint, not an asserted active timer path | [tests/PasswordReloginChecks/run.py](../../tests/PasswordReloginChecks/run.py)<br>[tests/LoginRecoveryChecks/run.py](../../tests/LoginRecoveryChecks/run.py) |
| DNS Recovery | [IRSpeedyVPN/WebServices/LoginRecovery.cs](../../IRSpeedyVPN/WebServices/LoginRecovery.cs)<br>[IRSpeedyVPN/WebServices/LoginDnsCache.cs](../../IRSpeedyVPN/WebServices/LoginDnsCache.cs)<br>[IRSpeedyVPN/WebServices/LoginPlainDns.cs](../../IRSpeedyVPN/WebServices/LoginPlainDns.cs)<br>[IRSpeedyVPN/WebServices/LoginDnsRace.cs](../../IRSpeedyVPN/WebServices/LoginDnsRace.cs)<br>[IRSpeedyVPN/Services/SingBox/ParallelDnsRouting.cs](../../IRSpeedyVPN/Services/SingBox/ParallelDnsRouting.cs) | Credential resolver-first classic DNS, non-login DoH and native-tunnel DNS configuration are three distinct paths | [tests/LoginRecoveryChecks/run.py](../../tests/LoginRecoveryChecks/run.py)<br>[tests/ParallelDnsChecks/ParallelDnsChecks.csproj](../../tests/ParallelDnsChecks/ParallelDnsChecks.csproj) |
| VPN Connection | [IRSpeedyVPN/MainWindow.xaml.cs](../../IRSpeedyVPN/MainWindow.xaml.cs)<br>[IRSpeedyVPN/Services/TunnelPlusService.cs](../../IRSpeedyVPN/Services/TunnelPlusService.cs)<br>[IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs](../../IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs)<br>[IRSpeedyVPN/Services/Libcore/LibcoreServiceClient.cs](../../IRSpeedyVPN/Services/Libcore/LibcoreServiceClient.cs)<br>[IRSpeedyVPN/Services/Libcore/ProtorpcClient.cs](../../IRSpeedyVPN/Services/Libcore/ProtorpcClient.cs) | Serialized UI request → IVPNService.Connect → TunnelPlus startup → local protorpc; common network recovery | [tests/ConnectionStartChecks/ConnectionStartChecks.csproj](../../tests/ConnectionStartChecks/ConnectionStartChecks.csproj)<br>[tests/NetworkRecoveryChecks/NetworkRecoveryChecks.csproj](../../tests/NetworkRecoveryChecks/NetworkRecoveryChecks.csproj) |
| Residential VPN | [IRSpeedyVPN/UserControls/UCServerList.Residential.cs](../../IRSpeedyVPN/UserControls/UCServerList.Residential.cs)<br>[IRSpeedyVPN/Residential/ResidentialViewModel.cs](../../IRSpeedyVPN/Residential/ResidentialViewModel.cs)<br>[IRSpeedyVPN/Residential/ResidentialSubscription.cs](../../IRSpeedyVPN/Residential/ResidentialSubscription.cs)<br>[IRSpeedyVPN/Residential/ResidentialService.cs](../../IRSpeedyVPN/Residential/ResidentialService.cs)<br>[IRSpeedyVPN/Residential/ResidentialModels.cs](../../IRSpeedyVPN/Residential/ResidentialModels.cs)<br>[IRSpeedyVPN/Residential/ResidentialTrafficBar.cs](../../IRSpeedyVPN/Residential/ResidentialTrafficBar.cs) | Account subscription metadata → fetched catalog/traffic → ResidentialService inheriting TunnelPlusService | [tests/ResidentialChecks/run.py](../../tests/ResidentialChecks/run.py) |
| UI / row transitions | [IRSpeedyVPN/UserControls/UCServerList.xaml.cs](../../IRSpeedyVPN/UserControls/UCServerList.xaml.cs)<br>[IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs)<br>[IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs)<br>[IRSpeedyVPN/Components/ServerListControl/ServerListViewModel.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListViewModel.cs)<br>[IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs)<br>[IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs)<br>[IRSpeedyVPN/Services/ServerCheckCache.cs](../../IRSpeedyVPN/Services/ServerCheckCache.cs) | Both tab scopes reuse cache/queue, picker, ranker, immutable snapshots and sweep planner | [tests/ServerListTransitionChecks/run.py](../../tests/ServerListTransitionChecks/run.py)<br>[tests/ResidentialChecks/run.py](../../tests/ResidentialChecks/run.py) |

## Rules that matter when choosing a path

- Login uses `LoginResolutionOrder = ResolverFirst`. `FetchLoginAddresses` calls `LoginPlainDns`,
  not `DohResolver`; the existence of a DoH class is not evidence that credential login uses it.
- `LoginCurlResult` owns send evidence and recovery eligibility. Unknown metrics are conservative;
  the narrow complete-metrics HTTPS `exit=6` proof does not authorize other sent POST replays.
- Session maintenance uses `AccountSessionMaintenance` generation/deadline state, not a connection
  version as account identity. Recognized rejection is handled before connection/view freshness gating.
- Residential service/probe/connection recovery uses the shared TunnelPlus pipeline. Its display
  ping alone uses `ResidentialPingDisplayFactor = 0.7`; city/ISP and traffic have their own presentation.
- Each picker owns its own `ServerListViewModel` / `ServerListSweepAnimator`; this is shared
  implementation, not one shared mutable list instance across both tabs.
- The row ranker and the transition planner do different jobs: ranking produces latest order;
  `PresentationOrder` permits only completed rows to move, preserves waiting rows, puts failed rows
  after waiting rows, and keeps the pinned row first. Selection identity is preserved in snapshots.
- Existing test locations are navigation pointers, not a claim of exhaustive test coverage or a new
  test run. Read runner prerequisites and execute relevant checks for an actual behavioral change.

## Deliberately unexpanded

Shield, routing details, Split Tunnel, hotspot, general settings, every service adapter, every native
RPC operation, all WPF resources and all async/error branches are inventory-only or partially traced.
Inspect those sources and tests when a task touches them; do not fill gaps with Android assumptions.
