# IRSPEEDY-WIN Codegraph — reviewed dependencies and change impact

This is a **partial, evidence-backed navigation graph**, not a complete call graph or a
measurement of behavioral test coverage. The Impact Analysis remains read-only and
never edits VPN application source or bypasses required regression checks.

## Running the analysis

From a clean repository checkout, before generating a new graph snapshot:

```sh
python tools/codegraph/impact.py --target HEAD \
  --report impact-report.json --summary impact-report.md
```

To distinguish this PR's changes from accumulated changes since the recorded graph
checkpoint, supply the **real base commit SHA of the PR**, not a guessed date or
moving branch name:

```sh
python tools/codegraph/impact.py --target HEAD --pr-base <PR_BASE_SHA> \
  --report impact-report.json --summary impact-report.md
```

- **Checkpoint changes**: Git diff from `manifest.json.base_commit` to the target;
  includes pre-existing tooling/test commits since the last graph snapshot. Generated
  `docs/codegraph/**` files are excluded.
- **PR-only changes**: Git diff from explicit `--pr-base` to target, independently
  counted. Without a supplied base, these counts say **Not available**, not zero.
- **PR application paths**: PR-changed files under `IRSpeedyVPN/` only; this path
  metric is not a behavioral-risk score or full source-code coverage.
- **Related test suites**: test-harness → source file links that already exist in
  the manifest. These are navigation hints, not proof of assertion coverage or
  an automatic test-selection/gating feature. The mandatory Regression Guard runs
  regardless of which (if any) suites are shown here.
- **Potential dependent files**: traversal of reverse, already-known dependency edges
  to depth 4 by default (`--max-depth` range: 1..8). Renames/deletions and stale
  historical witnesses are represented conservatively. An omitted edge is unknown,
  not proof of no effect.

## Reviewed test-to-source edges

`tools/codegraph/test_links.py` contains **11 manually reviewed, literal file
read/compilation witnesses**, including cache, startup, country selection, connection
UI, device-limit and server transition harnesses. They are deliberately pinned to
reviewed harness AND application file blobs, exact witness line and witness commit.
The incremental updater adds new `reads_source` graph relations **once** only if
both pins match and the literal witness still exists. The first subsequent generated
snapshot will contain these edges after review and normal PR checks; adding the tool
source alone does not retroactively update a previously committed manifest.

If either endpoint changes, the updater does NOT automatically re-verify or revive the
edge. Previously stale edges stay stale until a fresh source/caller/test review and
new explicit review pins. A link marked `reads_source` only means the runner references
or reads a production file; it never implies a test asserted every behavior in that
file. Existing `includes_source` relations express build inclusion only.

## Security and validity

- Refuse a dirty tracked checkout, unknown graph schema, invalid validation digest,
  wrong development branch, unvalidated manifest or unrelated commit ancestry.
- Never infer code relationships from filenames, names of test suites, regex
  similarities or Android behavior.
- Do not clear `stale` / `pending_review_files` to make a report green.
- All graph publication continues to use reviewed PRs and the existing four-file
  generated allowlist. A `needs-review` snapshot can be persisted honestly with
  stale records, never relabeled as source-verified without review.
- No CI or security changes: Windows desktop, packaged Core and live integration
  require separately executed tests and must be reported `Not Run` otherwise.

Run only the Codegraph suite with:

```sh
python -m unittest discover -s tests/CodegraphChecks -p 'test_*.py' -v
```
