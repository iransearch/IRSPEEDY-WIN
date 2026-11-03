# IRSPEEDY-WIN Impact Analysis (read-only)

This feature reports **potential code impact only**. It does not recommend tests, mutate the
Codegraph manifest, infer new calls, promote stale edges or change any VPN application logic.

Run from a clean checkout of the development branch:

```sh
python tools/codegraph/impact.py --target HEAD \
  --report impact-report.json --summary impact-report.md
```

The output is based on the **committed** Git diff from `manifest.json`'s `base_commit`
to the requested target, plus the **existing** evidence-backed graph. Generated-only
`docs/codegraph/**` changes are ignored. Each changed file is listed; the tool follows
**reverse known dependency edges** (caller -> dependency, traversed backward) to show
possible affected callers up to four levels by default (`--max-depth 1..8`). It
separately identifies directly touched relationships, module hints, and files without
known graph coverage. Rename/deletion paths are handled conservatively.

Limitations and safety:

- The manifest is validated (schema, edge witness/blob pins and `validation_digest`)
  before reporting. Failed/unvalidated metadata, an unrelated history, or a dirty
  tracked checkout stop with an error.
- A potential dependent is **not** proof of a runtime effect, and paths through
  stale edges are explicitly marked. A verified relation at the old checkpoint is
  **not** automatically verified for the target commit.
- Missing relationships never mean that a file is unaffected. The graph is partial.
- Previously stale relationships and pending-review files remain visible; reporting
  never silently clears them.
- Analysis is read-only. No source, graph files, commits, PRs or CI checks are created.
- No test-selection or test-recommendation feature is included.

Existing project workflow and Regression Guard rules continue unchanged. Test the
impact module itself with:

```sh
python -m unittest discover -s tests/CodegraphChecks -p 'test_impact.py' -v
```
