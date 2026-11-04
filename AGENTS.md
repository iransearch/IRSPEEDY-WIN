# Repository navigation and source of truth

Before investigating application source, read `docs/codegraph/overview.md` and the relevant module
in `docs/codegraph/modules.md`. Use `entrypoints.md` and `dependencies.md` to choose the smallest
useful set of files; inspect only the needed manifest metadata/module/edges rather than dumping
its full file inventory on every request.

Read `docs/codegraph/status.md` and `automation.md` for the current automated state. A `pending-validation`,
`needs-review`, failed or superseded run is not a valid-current-graph claim. The overview/module/entrypoint
prose retains its original manually reviewed snapshot; use per-edge status and witness commit in the
manifest/generated dependency table to avoid treating stale narrative as current behavior.

Check `manifest.json`'s `base_branch`, `base_commit`, `generated_at` and `validity` against the actual
checkout. Review Git changes since the baseline and `git status --short`, including local work.
The graph commit is newer than its source baseline; graph-document-only changes do not by
themselves invalidate source edges. If source/tests/build inputs changed, inspect the diff and
reread affected sources/callers/tests before trusting that part of the map. Update affected graph
entries or explicitly mark them stale. If the baseline object is unavailable, fetch/check it first;
never assume freshness from a matching branch name or an old validity label.

For any code modification, the real current source, callers, tests and build contracts are the final
authority. The graph is partial navigation, not a substitute for reading or testing the touched logic.
Do not infer relationships from names, history, another branch, Android or absence from this map.
Preserve existing behavior outside the authorized task and run relevant existing checks for changes.

When maintaining the map, record only source-confirmed relationships with file/symbol and exact
source witnesses. Keep counts, baseline/hash inventory, schema/date/validity and Markdown consistent.
Do not add credentials, tokens, keys, raw subscriptions, customer logs, or speculative edges.
Graph maintenance must not silently modify runtime files, security settings or release versions.

Development stays on `agent/core-startup-diagnostics-fix`; do not retarget maintenance to `main`.
The read-only Codegraph workflow computes downloadable graph artifacts. A separate trusted-push workflow proposes generated outputs via one draft PR using GITHUB_TOKEN and explicitly requests Regression Guard on the proposal head; it never pushes to the protected branch and never merges automatically. Checked-in graph metadata may lag the source until the snapshot PR is reviewed and merged. Compare the analyzed commit with the actual checkout. See `docs/codegraph/reviewed-snapshot-pr.md`. For changes to the updater/workflow, run
`python3 -m unittest discover -s tests/CodegraphChecks -p 'test_*.py' -v`, then run
`python3 tools/codegraph/update.py --dry-run --report /tmp/codegraph-trial.json` before publication.
Runtime relations touched by a source/dependency change remain stale until a real source/caller/test
review supplies an updated witness. Do not change statuses or recompute validation digests just to
make the graph green. New automatic edges are limited to literal build declarations.
A structurally validated generated snapshot labeled `needs-review` may be persisted through a
normal protected-branch PR while retaining all stale relations and pending-review entries; this
updates the recorded inventory, NOT the verification level of those edges. Require an exact
source baseline, intact schema/digest/provenance, four generated files only and genuine passing
`pull_request` checks before merge (a green `workflow_dispatch` alone is insufficient). Never
merge `failed` or `pending-validation` graphs or erase uncertainty to satisfy publication.

For WPF UI changes, first inspect `docs/codegraph/ui-components.md` as a navigation index; verify its pinned checkpoint and current XAML/code-behind rather than assuming it is auto-refreshed. The UI index currently covers six verified XAML/partial-class pairs only. Do not infer missing event handlers, template resources, or service dependencies. Changed XAML is tracked in the main graph as pending review. The machine-readable `docs/codegraph/ui-index.json` is regenerated incrementally by the workflow; the manually reviewed `ui-components.md` is not. Event names, resource keys and merged dictionary URIs are declarations only: do not infer runtime dependencies from them.

## Impact Analysis, test witnesses, and honest scope

Before refreshing a graph snapshot, run the read-only Impact Analysis against the
committed source checkpoint. For PR-specific counts, pass the PR's exact base
commit with --pr-base. Never label cumulative checkpoint changes as PR-only.
State 'not available' if the PR base was not supplied.

The graph supports manually reviewed test-harness -> source-file links with
exact source/target blob pins and literal line witnesses. These prove a harness
reads/includes a source file, NOT that assertions cover the file's behavior.
Only seed unchanged reviewed witnesses; never promote stale links automatically.
See docs/codegraph/impact-analysis.md and tools/codegraph/test_links.py.

Related suites are advisory navigation only. Never skip mandatory Regression
Guard or task-relevant tests merely because the graph shows no suite.
Mapped file count and relationship density are not behavioral coverage.
Windows desktop, packaged Core and live integration checks remain Not Run
unless actually executed.
