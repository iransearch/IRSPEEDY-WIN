# UI Component Map — IRSPEEDY Windows WPF (starter)
 
**Source checkpoint:** `1e369886a89c5c48047bfaf3889d69a4866cb46b` on `agent/core-startup-diagnostics-fix`.
**Status:** manually source-checked starter index; **not** a complete or automatically maintained UI call graph.

## Navigation first

Use this index before browsing WPF presentation code. For every UI change, verify `docs/codegraph/status.md` and the latest Git diff, then inspect the current XAML, its code-behind, callers, templates/resources and relevant tests. Do not treat this snapshot as current if source changed. Unlisted controls, event handlers or view models are *unknown*, not absent.

| UI area | XAML declaration (source) | Code-behind declaration (source) | Notes |
| --- | --- | --- | --- |
| Main application window | [MainWindow.xaml](../../IRSpeedyVPN/MainWindow.xaml) — `x:Class="IRSpeedyVPN.MainWindow"` | [MainWindow.xaml.cs](../../IRSpeedyVPN/MainWindow.xaml.cs) — partial `MainWindow` | Shell, navigation and top-level actions |
| Login | [UCLogin.xaml](../../IRSpeedyVPN/UserControls/UCLogin.xaml) — `x:Class="IRSpeedyVPN.UserControls.UCLogin"` | [UCLogin.xaml.cs](../../IRSpeedyVPN/UserControls/UCLogin.xaml.cs) — partial `UCLogin` | Credential input; `OnCredentialEntered` is declared in code-behind |
| Server list | [UCServerList.xaml](../../IRSpeedyVPN/UserControls/UCServerList.xaml) — `x:Class="IRSpeedyVPN.UserControls.UCServerList"` | [UCServerList.xaml.cs](../../IRSpeedyVPN/UserControls/UCServerList.xaml.cs) — partial `UCServerList` | Search, location selection; `txtSearch_TextChanged` is declared as a XAML event binding |
| Country picker | [ServerCountryPicker.xaml](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml) — `x:Class="IRSpeedyVPN.Components.ServerListControl.ServerCountryPicker"` | [ServerCountryPicker.xaml.cs](../../IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs) — partial `ServerCountryPicker` | Country and latency UI; XAML declares `Picker_Loaded`, `Picker_Unloaded` |
| Traffic panel | [ConnectionTrafficPanel.xaml](../../IRSpeedyVPN/UserControls/ConnectionTrafficPanel.xaml) — `x:Class="IRSpeedyVPN.UserControls.ConnectionTrafficPanel"` | [ConnectionTrafficPanel.xaml.cs](../../IRSpeedyVPN/UserControls/ConnectionTrafficPanel.xaml.cs) — partial `ConnectionTrafficPanel` | Traffic table; code-behind binds `TrafficTable.ItemsSource = liveRows` |
| Settings | [SettingsHub.xaml](../../IRSpeedyVPN/Windows/SettingsHub.xaml) — `x:Class="IRSpeedyVPN.Windows.SettingsHub"` | [SettingsHub.xaml.cs](../../IRSpeedyVPN/Windows/SettingsHub.xaml.cs) — partial `SettingsHub` | Settings UI; `Methods_Click` is declared in XAML, access state computed in code-behind |

## What is actually verified

Each XAML `x:Class` was read from the source file. The corresponding partial class was checked directly in the paired C# file at the checkpoint. These are **declaration/pairing** relationships, *not* evidence that every runtime event or business-service connection was traced. The event and binding examples above are concrete lines observed in those files; handler wiring and full runtime call graphs must be checked during a task.

## Areas still to map

All remaining XAML pages, ResourceDictionaries, `UserControls`, reusable component templates, shared styles, named controls, dynamic UI built in C#, view model / data-binding sources, resource-key references, event handlers and service-to-UI update paths. In particular, expand Home, Residential VPN, traffic display and server-list animation only after inspecting their actual declarations and callbacks.

## Automated declaration index

After this PR is merged, the workflow also maintains [ui-index.json](ui-index.json), a machine-readable XAML declaration index. It records XAML classes, named controls, event-handler names **as declarations only**, resource-key definitions and references, merged ResourceDictionary URI declarations and matching C# partial-class declarations. Each entry carries a source blob SHA and line witness. The first run bootstraps all tracked XAML files; later runs read only changed XAML and their paired C# code-behind files. Deleted/renamed files are removed or reindexed. Resources in the form of DynamicResource and StaticResource are **not** resolved to runtime providers, and event-handler *calls* are not inferred.

## Freshness and automation limitation

The existing `tools/codegraph/update.py` already tracks changed `.xaml` blob SHAs and puts changed UI/runtime files into `pending_review_files`. **It automatically updates the machine-readable `ui-index.json`, but not this manually reviewed prose index or any runtime event-handler/service edges.** Do not mark it automatically current based solely on a successful GitHub Action run. A later semantic extension should resolve controls to runtime handlers, XAML resource references to actual dictionary providers and service/data-binding paths with semantic source checks; unknown links must remain unknown. Any unresolved runtime edges must remain stale/unknown.
