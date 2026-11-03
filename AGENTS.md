# Repository navigation and source of truth

Before investigating application source, read `docs/codegraph/overview.md` and the relevant module
in `docs/codegraph/modules.md`. Use `entrypoints.md` and `dependencies.md` to choose the smallest
useful set of files; inspect only the needed manifest metadata/module/edges rather than dumping
its full file inventory on every request.

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
