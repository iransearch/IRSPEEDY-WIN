# Incremental Codegraph PR automation (built-in token)

Scope: `iransearch/IRSPEEDY-WIN`, development branch `agent/core-startup-diagnostics-fix` only.

## Zero-secret lifecycle

1. Every trusted push to the development branch runs `.github/workflows/codegraph-snapshot-pr.yml`, checks it is processing the current branch HEAD, and runs the existing incremental graph updater and source-proof/unit validation. It never scans all source file contents merely to refresh the graph.
2. The workflow aborts if generation fails, the analyzed commit does not match HEAD, or any file outside the generated allowlist changes.
3. On a real graph diff, the built-in `GITHUB_TOKEN` proposes only `docs/codegraph/{manifest.json,dependencies.md,status.md,ui-index.json}` on the fixed branch `automation/codegraph-reviewed-snapshot`. The `peter-evans/create-pull-request` action creates or updates **one draft PR** targeting the development branch, with no forced update to the protected branch and no auto-merge.
4. A push or PR authored by `GITHUB_TOKEN` does **not** normally trigger other GitHub Actions workflows. To keep the development branch's required `Fast offline regression (Linux .NET 8)` check, the workflow explicitly invokes `gh workflow run regression-guard.yml --ref automation/codegraph-reviewed-snapshot` after a successful PR branch creation/update. GitHub documents `workflow_dispatch` as an exception to recursion prevention. The Regression Guard workflow already declares `workflow_dispatch` and has been registered/executed on the development branch.
5. Required-check enforcement remains unchanged. **If dispatch fails or a successful check is not attached to the PR head, the PR must remain blocked**. Do not change rules or synthesize a fake check. A reviewer confirms the check, evidence and stale/pending relationships before making the draft ready and merging manually.
6. When the PR is merged, the generated-only commit normally produces no new graph diff. A later source push can refresh the single proposal branch again.

## GitHub repository setting

No GitHub App, PAT, or secrets are needed. In repository **Settings → Actions → General → Workflow permissions**, allow GitHub Actions to **create and approve pull requests** if the create-pull-request step is denied. (The workflow does not actually approve PRs.) If organizational policy forbids this permission, leave the existing artifact-only workflow in place instead.

Permissions of the snapshot workflow: `contents: write`, `pull-requests: write`, `actions: write` for its **built-in scoped token**. This is for writing the *proposal branch* and explicitly dispatching CI, never for bypassing branch protection. `main` is untouched.

## Safety and limitations

- Only trusted development pushes run the workflow; PR code has no access to this write-capable job.
- The action uses `persist-credentials: false`. An allowlist rejects unexpected generated paths. No source, workflow, tests, or runtime files are committed by the proposal action.
- The remote development head is checked before analysis. Run concurrency serializes proposals. A later push can supersede a previous proposal; it still must pass CI at its latest head.
- Some GitHub installations require a `workflow_dispatch` workflow file to exist on the **default branch**. Here the existing Regression Guard workflow is registered and has already run, but that does not guarantee dispatch will work. Validate the first proposal run and attached required check **before relying on automation**. If dispatch is rejected, do not bypass required checks or silently treat the PR as tested; fall back to human-triggered PR workflow or use a dedicated App.
- A draft PR is deliberately not merged automatically. The committed graph may lag until review/merge. Semantic runtime changes remain `stale` and require real source/caller/test evidence to re-verify.
- No Windows Build, executable release, VPN connection, or live credentials are used.

Review `.github/workflows/codegraph.yml` artifact output for independent evidence and `AGENTS.md` for graph freshness constraints.
