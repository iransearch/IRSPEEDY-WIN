# Incremental graph automation

Development branch: `agent/core-startup-diagnostics-fix`. Workflow: `.github/workflows/codegraph.yml`.
No workflow writes to `main`, VPN application sources, security settings or release/build inputs.

## Activation and trial

The initial graph and this automation are proposed together in PR #3. Nothing is merged by this task.
PR checks run tests plus a read-only trial. After merge, each push to the development branch runs
the checks, then a scoped graph synchronization using the repository `GITHUB_TOKEN`.
GitHub's repository token does not trigger another push workflow, as documented in
[GitHub token guidance](https://docs.github.com/en/actions/concepts/security/github_token).
Generated-only diffs also produce a no-op without another commit, independently preventing loops.
Only the sync job has `contents: write`; the PR/check job has read-only permissions.
If repository rules deny bot pushes, the job reports failure; it does not bypass branch protection.

```sh
python3 -m unittest discover -s tests/CodegraphChecks -p 'test_*.py' -v
python3 tools/codegraph/update.py --dry-run --report /tmp/codegraph-trial.json
```

`--dry-run` never writes tracked files. Every run prints counts and writes a JSON report. GitHub jobs
also append a Step Summary and upload that report as an artifact, including analysis/publication failures.
An initial seed remains `pending-validation` in this PR; it cannot claim to validate a future merge commit.

## What is incremental

1. Read the manifest checkpoint and run `git diff --raw -z --no-abbrev --find-renames=50%` to the push commit.
   Use both old/new Blob SHAs; compare the old SHA to the inventory before updating it.
2. Update inventory entries from the diff. Do not enumerate/read all 981 source contents. Parsing the JSON
   inventory and walking relationship metadata to compute counts is not source-content reprocessing.
3. Read changed C#/XAML/build files only; old contents are needed only for changed build declarations.
   Mode-only changes with identical regular-file blobs do not require content analysis.
4. Recheck affected source/target/evidence endpoints and literal project dependency scopes, including
   transitive project references. Unchanged endpoints retain their pinned, previously reviewed evidence.
5. Rename paths in the inventory and module entrypoints. Keep deleted/renamed runtime relations as stale
   audit records with their original witness path/commit; do not silently delete history or infer new calls.
6. Refresh only changed project metadata. Add new edges only for literal Solution members, ProjectReference,
   Compile, Import or PackageReference declarations with exact source witnesses and known targets.
7. Validate pin/status/schema invariants, then set the validity label and write the generated outputs.

One-time schema 1 → 2 migration reads the existing graph's witness files (32 for the current 80 edges),
not every indexed file. It verifies the original reviewed witness text against its pinned blob before
carrying it forward. Subsequent unchanged/generated-only runs read zero application source contents.
No full-scan fallback runs when history is missing or rewritten: fetch history or explicitly rebaseline.

## Schema 2 and validity

- `base_commit` / `base_tree`: latest analyzed input commit/tree. The subsequent bot commit contains
  only graph outputs and necessarily cannot put its own SHA inside itself. A generated-only later HEAD
  is source-equivalent; the report distinguishes the observed target from the analyzed checkpoint.
- `status` on each relation: `verified` or `stale`; `stale_reason` explains invalidation.
- `source_blob`, `target_blob`, `evidence.blob`, `evidence.commit`, `last_verified_commit`: provenance pins.
  Stale witnesses intentionally refer to their last verified snapshot, not the current file's line.
- `origin` / `verification`: prior manual review versus a literal build declaration. A verified build
  declaration is not a statement that a conditional MSBuild item actually ran or a package was restored.
- `external_nodes`: package identities/declared versions from literal package declarations; no network lookup.
- `pending_review_files` / `unresolved_build_items`: changed runtime/UI sources and declarations that cannot
  be proved automatically (variables, wildcards, missing targets, malformed XML).
- `validation_digest`: detects graph edits made after validation, including fabricated verified witnesses.
  This is a consistency guard, not a cryptographic signature or a replacement for review.

Global status becomes `validated-for-analyzed-commit` only after validation and with no stale/pending items.
Otherwise it is `needs-review`; any infrastructure/invariant error reports `failed` and publishes nothing.
`analysis_complete` and `runtime_validated` stay false. This remains a partial code map.

An unchanged line of C# is **not** semantic proof that receiver resolution, scope, overloads, conditional
compilation or surrounding behavior stayed the same. Changed runtime endpoints and affected project
dependencies are conservatively stale even if the old text survives. New executable call relationships
are not guessed by regex/name matching. They require an actual source/caller/test review and a deliberate
reviewed rebaseline of witnesses, pins and pending items. Do not clear a warning merely to obtain green status.

## Output and publication safety

Reports include changed/examined/content-read file counts, checked/changed/new/stale relation counts,
dependency declaration changes, pending paths, validity and publication result. The generated
`status.md`, `dependencies.md` and `manifest.json` are the only allowed automatic commit paths.
The manually written overview/modules/entrypoints stay explicitly labeled historical.

Publication checks the exact development ref, analyzed checkout SHA and remote branch head, refuses
staged non-graph files, and performs a normal non-force push. Concurrent developer pushes win; an older
run reports `superseded` or a push failure. A later run diffs from the last saved checkpoint and includes
all missed changes. Concurrency groups prevent simultaneous writers but are not assumed to preserve
every intermediate pending run; the persisted checkpoint is the authority.

Tests use temporary local Git repositories/bare remotes without Internet access. They cover rename,
delete, new/removed/versioned/transitive dependencies, multiple files, fake/commented evidence, missing
history, incomplete/malformed inputs, 981-file scaling, read-only trial, main rejection, restricted
publication, no-op loop prevention and concurrent developer updates.
