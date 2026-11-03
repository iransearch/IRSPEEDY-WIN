# Verified dependencies and relationships

Only explicit source/build relationships are graph edges. Each row links to the exact baseline
source line; the manifest also stores the witness text and full endpoint paths. `calls` describes
a static call site, not proof that it ran. `subscribes`, `constructs`, `inherits`, `implements`,
`declares_type`, `assigns_delegate`, `hosts_control`, `reads_source`, `includes_source`,
`solution_member` and `startup_type` keep other relationships distinct from calls.
An edge's `note` records guards/limitations. No omitted edge means "does not depend on".

## Project boundaries

The root solution lists IRSpeedyVPN and UpdateHelper; the nested solution lists IRSpeedyVPN and Throne.
All 24 project files were parsed for declared frameworks/references. None declares a `ProjectReference`;
solution membership does not imply an app-to-helper assembly reference. Test projects often link selected
production `.cs` files directly instead. `projects` in the manifest records each project's package
references and linked-source count (not a complete runtime or transitive dependency graph).
The hotspot helper separately targets `net8.0-windows10.0.19041.0`; the WPF app, Throne and UpdateHelper target .NET 4.8.

| WPF app declared package | Version |
| --- | --- |
| `Sentry` | `6.11.0` |
| `DotNetZip` | `1.13.8` |
| `Newtonsoft.Json` | `12.0.2` |
| `System.Buffers` | `4.5.1` |
| `System.Memory` | `4.5.5` |
| `System.Numerics.Vectors` | `4.5.0` |
| `System.Runtime.CompilerServices.Unsafe` | `6.0.0` |
| `QRCoder` | `1.4.3` |
| `Transitionals` | `2023.11.15.1` |

Source: [IRSpeedyVPN/IRSpeedyVPN.csproj](../../IRSpeedyVPN/IRSpeedyVPN.csproj). These are declared versions, not a restored lockfile or vulnerability assessment.
That project also imports `tools/HotspotPayload.targets`, embeds payload/license/font resources,
and declares an optional Release Dotfuscator target. Those are build boundaries; no payload contents
or native code were inferred from the C# wrappers. Build/package scripts and executable payload
versions need separate analysis before changing release behavior.

## Source-witnessed edge list

| ID | Kind | Source symbol → target symbol (full paths in manifest) | Baseline witness |
| --- | --- | --- | --- |
| `R001` | solution_member | `IRSpeedyVPN.sln` → `IRSpeedyVPN/IRSpeedyVPN.csproj` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN.sln#L6) |
| `R002` | solution_member | `IRSpeedyVPN.sln` → `UpdateHelper/UpdateHelper.csproj` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN.sln#L8) |
| `R003` | solution_member | `IRSpeedyVPN/IRSpeedyVPN.sln` → `IRSpeedyVPN/IRSpeedyVPN.csproj` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/IRSpeedyVPN.sln#L5) |
| `R004` | solution_member | `IRSpeedyVPN/IRSpeedyVPN.sln` → `Throne/Throne.csproj` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/IRSpeedyVPN.sln#L7) |
| `R005` | startup_type | `StartupObject` → `Program` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/IRSpeedyVPN.csproj#L10) |
| `R006` | calls | `Program.Main` → `AppServices.InitializeFast` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Program.cs#L73) |
| `R007` | constructs | `Program.RunApplication` → `MainWindow` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Program.cs#L221) |
| `R008` | calls | `Program.RunApplication` → `MainWindow.PrepareFastStartup` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Program.cs#L225) |
| `R009` | constructs | `AppServices.InitializeFast` → `NewServiceController` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/AppServices.cs#L106) |
| `R010` | constructs | `AppServices.InitializeFast` → `ServiceFactory` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/AppServices.cs#L108) |
| `R011` | constructs | `AppServices.InitializeFast` → `UCServerList` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/AppServices.cs#L113) |
| `R012` | subscribes | `MainWindow.PrepareFastStartup` → `MainWindow.UCLogin_OnCredentialEntered` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L46) |
| `R013` | raises_event | `UCLogin.btnLogin_Click` → `UCLogin.OnCredentialEntered` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCLogin.xaml.cs#L84) |
| `R014` | calls | `MainWindow.UCLogin_OnCredentialEntered` → `MainWindow.Login` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L1069) |
| `R015` | calls | `MainWindow.Login` → `NewServiceController.Login2` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L1082) |
| `R016` | calls | `NewServiceController.Login2` → `NewServiceController.getServerList` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/NewServiceController.cs#L69) |
| `R017` | constructs | `NewServiceController.getServerList` → `LoginNetworkContext` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/NewServiceController.cs#L106) |
| `R018` | calls | `NewServiceController.getServerList` → `RestHelper.SendRequest` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/NewServiceController.cs#L116) |
| `R019` | calls | `RestHelper.SendRequest` → `CurlHelper.SendLogin` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/RestHelper.cs#L120) |
| `R020` | constructs | `CurlHelper.SendLogin` → `LoginRecovery` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/CurlHelper.Login.cs#L21) |
| `R021` | calls | `CurlHelper.SendLogin` → `LoginRecovery.Send` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/CurlHelper.Login.cs#L23) |
| `R022` | calls | `LoginRecovery.Send` → `LoginRecovery.SendResolverFirst` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/LoginRecovery.cs#L175) |
| `R023` | constructs | `CurlHelper.LoginDns` → `LoginDnsCache` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/CurlHelper.Login.cs#L15) |
| `R024` | calls | `CurlHelper.FetchLoginAddresses` → `LoginPlainDns.Resolve` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/CurlHelper.Login.cs#L155) |
| `R025` | calls | `LoginPlainDns.Resolve` → `LoginDnsRace.Resolve` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/LoginPlainDns.cs#L27) |
| `R026` | calls | `LoginRecovery.LookupResolverFirst` → `LoginDnsCache.Resolve` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/LoginRecovery.cs#L262) |
| `R027` | calls | `LoginRecovery.SendResolverFirst` → `LoginRecovery.SendCore` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/LoginRecovery.cs#L238) |
| `R028` | calls | `CurlHelper.LoginTransport.Managed` → `HttpFallback.Send` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/CurlHelper.Login.cs#L39) |
| `R029` | calls | `RestHelper.SendRequest` → `DohResolver.Resolve` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/RestHelper.cs#L107) |
| `R030` | calls | `MainWindow.BeginAccountSession` → `AccountSessionMaintenance.Begin` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L339) |
| `R031` | calls | `MainWindow.ScheduleSessionMaintenance` → `AccountSessionMaintenance.DelayMilliseconds` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L378) |
| `R032` | calls | `MainWindow.SessionMaintenanceCallback` → `MainWindow.ValidateSessionAndRefreshServerList` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L399) |
| `R033` | calls | `MainWindow.ValidateSessionAndRefreshServerList` → `MainWindow.RefreshAuthenticatedServerList` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L477) |
| `R034` | calls | `MainWindow.RefreshAuthenticatedServerList` → `NewServiceController.Login2` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L536) |
| `R035` | calls | `MainWindow.RefreshAuthenticatedServerList` → `MainWindow.LogoutInvalidSession` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L554) |
| `R036` | calls | `MainWindow.LogoutInvalidSession` → `MainWindow.CompleteInvalidSessionLogoutAsync` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L430) |
| `R037` | calls | `MainWindow.CompleteInvalidSessionLogoutAsync` → `IVPNService.Disconnect` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L446) |
| `R038` | calls | `NewServiceController.CheckToken` → `RestHelper.SendRequest` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/WebServices/NewServiceController.cs#L175) |
| `R039` | calls | `MainWindow.ProcessInfo` → `ServiceFactory.RenewServiceList` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L1258) |
| `R040` | constructs | `ServiceFactory.RenewServiceList(List<Group>)` → `TunnelPlusService` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/ServiceFactory.cs#L56) |
| `R041` | implements | `TunnelPlusService` → `IVPNService` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L35) |
| `R042` | subscribes | `MainWindow.PrepareFastStartup` → `MainWindow.UCServerList_OnConnectRequest` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L41) |
| `R043` | calls | `MainWindow.UCServerList_OnConnectRequest` → `MainWindow.ApplyConnectionRequestAsync` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L535) |
| `R044` | calls | `MainWindow.ApplyConnectionRequestAsync` → `IVPNService.Connect` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L597) |
| `R045` | calls | `TunnelPlusService.Connect` → `TunnelPlusService.RunV2ray` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L166) |
| `R046` | calls | `TunnelPlusService.RunV2ray` → `SingBox.ConfigGenerator.GetConfig` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L367) |
| `R047` | calls | `TunnelPlusService.TryStartCoreWithConfig` → `ParallelDnsRouting.Apply` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L521) |
| `R048` | calls | `TunnelPlusService.TryStartCoreWithConfig` → `LibcoreServiceClient.StartWithDeadline` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L605) |
| `R049` | calls | `LibcoreServiceClient.StartWithDeadline` → `ProtorpcClient.Connect` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/Libcore/LibcoreServiceClient.cs#L52) |
| `R050` | calls | `ProtorpcClient.Call` → `LibcoreProto.EncodeRequestHeader` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/Libcore/ProtorpcClient.cs#L89) |
| `R051` | calls | `TunnelPlusService.RunV2ray` → `TunnelPlusService.BeginNetworkRecoverySession` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.cs#L420) |
| `R052` | constructs | `TunnelPlusService.BeginNetworkRecoverySession` → `NetworkRecoverySession` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs#L55) |
| `R053` | declares_type | `AccountInfoEx.residential_vpn` → `ResidentialConfig` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Models/NewService/AccountInfoEx.cs#L14) |
| `R054` | calls | `MainWindow.ProcessInfo` → `UCServerList.ConfigureResidential` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.xaml.cs#L1259) |
| `R055` | calls | `MainWindow.TryApplyCachedAccountAsync` → `UCServerList.ConfigureResidential` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/MainWindow.FastStartup.cs#L194) |
| `R056` | constructs | `UCServerList.InitializeResidential` → `ResidentialViewModel` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.Residential.cs#L29) |
| `R057` | calls | `UCServerList.ConfigureResidential` → `ResidentialViewModel.Configure` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.Residential.cs#L60) |
| `R058` | assigns_delegate | `ResidentialViewModel.ResidentialViewModel` → `ResidentialViewModel.DownloadAsync` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Residential/ResidentialViewModel.cs#L40) |
| `R059` | calls | `ResidentialViewModel.RefreshCoreAsync` → `ResidentialSubscription.Parse` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Residential/ResidentialViewModel.cs#L103) |
| `R060` | constructs | `ResidentialViewModel.RefreshCoreAsync` → `ResidentialService` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Residential/ResidentialViewModel.cs#L113) |
| `R061` | calls | `ResidentialViewModel.RefreshCoreAsync` → `ResidentialTraffic.Parse` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Residential/ResidentialViewModel.cs#L116) |
| `R062` | inherits | `ResidentialService` → `TunnelPlusService` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Residential/ResidentialService.cs#L10) |
| `R063` | calls | `UCServerList.UpdateResidentialVisuals` → `ResidentialTrafficBar.Update` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.Residential.cs#L124) |
| `R064` | hosts_control | `UCServerList` → `ResidentialTrafficBar` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.xaml#L63) |
| `R065` | calls | `UCServerList.RefreshCountry` → `ServerCountryPicker.Load` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.xaml.cs#L187) |
| `R066` | constructs | `UCServerList.RefreshCountry` → `ServerCheckCache` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.xaml.cs#L174) |
| `R067` | calls | `UCServerList.CheckServerAsync` → `ServerCountryPicker.RefreshGroup` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/UserControls/UCServerList.xaml.cs#L431) |
| `R068` | calls | `ServerCountryPicker.ResortGroups` → `ServerListRanking.Order` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs#L510) |
| `R069` | calls | `GroupItem.DisplayLatency` → `ResidentialPresentation.DisplayPing` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs#L167) |
| `R070` | constructs | `ServerCountryPicker.ServerCountryPicker` → `ServerListViewModel` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs#L354) |
| `R071` | constructs | `ServerCountryPicker.ServerCountryPicker` → `ServerListSweepAnimator` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs#L356) |
| `R072` | calls | `ServerCountryPicker.BeginCheckCycle` → `ServerListViewModel.BeginCheckCycle` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs#L18) |
| `R073` | calls | `ServerCountryPicker.RefreshDisplayedRows` → `ServerListViewModel.Publish` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Transitions.cs#L65) |
| `R074` | calls | `ServerListSweepAnimator.Prepare` → `ServerListViewModel.TryBeginPass` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs#L69) |
| `R075` | calls | `ServerListSweepAnimator.FinishPass` → `ServerListViewModel.Finish` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs#L161) |
| `R076` | includes_source | `tests/ParallelDnsChecks/ParallelDnsChecks.csproj` → `IRSpeedyVPN/Services/SingBox/ParallelDnsRouting.cs` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/tests/ParallelDnsChecks/ParallelDnsChecks.csproj#L8) |
| `R077` | includes_source | `tests/NetworkRecoveryChecks/NetworkRecoveryChecks.csproj` → `IRSpeedyVPN/Services/TunnelPlusService.NetworkRecovery.cs` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/tests/NetworkRecoveryChecks/NetworkRecoveryChecks.csproj#L7) |
| `R078` | reads_source | `tests/PasswordReloginChecks/run.py` → `IRSpeedyVPN/Common/AccountSessionMaintenance.cs` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/tests/PasswordReloginChecks/run.py#L15) |
| `R079` | reads_source | `tests/ResidentialChecks/run.py` → `IRSpeedyVPN/Residential/ResidentialTrafficBar.cs` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/tests/ResidentialChecks/run.py#L18) |
| `R080` | reads_source | `tests/LoginRecoveryChecks/run.py` → `IRSpeedyVPN/WebServices/NewServiceController.cs` | [source](https://github.com/iransearch/IRSPEEDY-WIN/blob/a57db82d05d3182eba7d98db5a47cf28d4d42f66/tests/LoginRecoveryChecks/run.py#L27) |

## Limits and verification

- `file_inventory` covers the entire pinned Git tree; listing a file is not a reviewed call edge.
- Evidence text/lines, endpoint paths, blob hashes, JSON/counts and Markdown links were checked
  against the pinned source snapshot. This documentation-only change does not run application
  or network tests and does not assert a successful Windows build.
- The listed test-source relationships identify where to start. Test coverage, all callers, framework
  overload resolution, reflection, generated code and native implementation are not exhaustively analyzed.
- Native Core is an external process behind `LibcoreServiceClient` / `ProtorpcClient`; inspect its actual
  matching repository/commit for DNS race, transport or routing internals. Do not copy assumptions here.
- Do not copy credentials, keys, tokens, raw subscriptions, customer data or logs into graph artifacts.
