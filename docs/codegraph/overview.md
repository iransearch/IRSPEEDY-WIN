# Code Knowledge Graph: navigation first

> Historical manually reviewed baseline notes. For current validity, stale edges and the analyzed commit, read [status.md](status.md), [automation.md](automation.md) and `manifest.json`.

Source baseline: `a57db82d05d3182eba7d98db5a47cf28d4d42f66` on `agent/core-startup-diagnostics-fix` (assembly `1.4.70`).
Generated: `2026-11-03T15:36:20Z`. Schema: `1.0.0`.
Status: **verified for this source commit; targeted and incomplete; not runtime validation**.
This is a documentation index and an evidence-backed file/symbol graph, not an automatic call-graph engine.

## Read only what the task needs

1. Read this page, then the relevant row in [modules.md](modules.md).
2. Check [manifest.json](manifest.json) metadata and Git freshness before trusting a path.
3. Use [entrypoints.md](entrypoints.md) to pick a flow; use [dependencies.md](dependencies.md) for its source witnesses.
4. Open the actual changed methods, callers and relevant tests. Source and tests take precedence over the map.

Do not dump the full manifest on each task. `file_inventory` is a path-to-Git-blob-SHA index;
`relationships` is the canonical edge list. Select a module's `files` / `relation_ids` or search one path.
No relationships come from filenames alone, conversation history, Android behavior or package-name guesses.

## Scope and coverage

| Measure | Count |
| --- | ---: |
| Baseline tracked files indexed (all blob entries) | 981 |
| C# files / XAML files in inventory | 382 / 55 |
| Solutions / projects | 2 / 24 |
| Test projects / `tests/*/run.py` files | 20 / 19 |
| Requested modules with a primary static path | 6 / 6 |
| Explicit verified graph edges / source files providing edge witnesses | 80 / 32 |

Inventory coverage is complete for the baseline tree. Behavioral/call coverage is deliberately partial;
six mapped primary paths do **not** mean every branch or every file was analyzed.
The six added graph documents are not counted as baseline files.

## Repository shape

| Path | Confirmed role |
| --- | --- |
| [IRSpeedyVPN.sln](../../IRSpeedyVPN.sln) | Solution containing the WPF app and UpdateHelper |
| [IRSpeedyVPN/IRSpeedyVPN.sln](../../IRSpeedyVPN/IRSpeedyVPN.sln) | Separate solution containing the WPF app and Throne |
| [IRSpeedyVPN/IRSpeedyVPN.csproj](../../IRSpeedyVPN/IRSpeedyVPN.csproj) | SDK WindowsDesktop `net48`, WPF, startup type `IRSpeedyVPN.Program` |
| `IRSpeedyVPN/WebServices/` | Login HTTP, credential DNS recovery, general DoH and password APIs |
| `IRSpeedyVPN/Services/` | Service factory, tunnel lifecycle, local Core RPC and other adapters |
| `IRSpeedyVPN/Residential/` | Subscription catalog, shared-tunnel adapter, traffic data and control |
| `IRSpeedyVPN/UserControls/`, `Components/ServerListControl/` | UI orchestration, scopes, row snapshots and light sweep |
| `tests/` | Standalone C# projects and Python harnesses; not assumed to be solution members |
| `Throne/`, `UpdateHelper/`, `tools/IRSpeedyHotspotHelper/` | Separate executable projects; detailed runtime integration not mapped |

## Freshness before source work

Read `base_commit` and `base_branch` from the manifest; verify `git branch --show-current`
and `git rev-parse HEAD`. If the baseline object is missing in a shallow checkout, fetch it
before concluding that the map is current. Then inspect:

```sh
git diff --name-status a57db82d05d3182eba7d98db5a47cf28d4d42f66 HEAD -- . ':!docs/codegraph/**' ':!AGENTS.md'
git status --short
```

A different HEAD is expected for this documentation-only commit. No source/test/build changes
relative to the baseline means its source map can still be used. Changes outside the six graph
documents, including uncommitted/untracked relevant files, require review: inspect the diff,
reread affected callers/tests, and update or explicitly mark affected edges stale before relying on them.
Do not automatically call every edge current because the branch name matches.

## Maintaining the map

Keep the map small: add only task-relevant, source-confirmed edges and preserve explicit gaps.
For a source change, record the new source commit/tree after it exists, refresh inventory hashes,
recheck touched edge witnesses, update counts/date/status and the relevant Markdown together.
For schema changes, bump `schema_version` and describe the changed fields here.
Never mark `analysis_complete` or `runtime_validated` true merely because an index check passes.

Current gaps: exhaustive callers/async interleavings; protocol-specific Pool/Xray paths; native Core
DNS/routing internals outside this repository; other adapters and settings; full WPF binding/input
coverage; live API/subscription behavior. See each module's `gaps` in the manifest.
