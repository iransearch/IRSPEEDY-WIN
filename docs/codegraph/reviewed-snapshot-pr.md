# Reviewed Codegraph snapshot PR automation

Scope: `iransearch/IRSPEEDY-WIN` development branch `agent/core-startup-diagnostics-fix` only.

## Lifecycle

1. The existing `.github/workflows/codegraph.yml` tests and generates an ephemeral graph artifact on development pushes and PRs.
2. The independent `.github/workflows/codegraph-snapshot-pr.yml` runs **only after a trusted development-branch push**. It checks out the exact source SHA, skips superseded commits, runs the existing Codegraph unit tests, and regenerates the snapshot incrementally from the last reviewed repository checkpoint.
3. It checks that only these four files changed: `docs/codegraph/manifest.json`, `dependencies.md`, `status.md`, and `ui-index.json`. It does not publish source, tests, workflows or credentials.
4. If and only if generated files changed, a repository-scoped GitHub App token creates or updates the same **draft** PR from `automation/codegraph-reviewed-snapshot` into `agent/core-startup-diagnostics-fix`. No new PR for a no-op. Only one managed branch; successive changes update the existing PR rather than multiplying PRs.
5. A GitHub App-authored PR triggers normal `pull_request` checks (unlike a PR created by the default `GITHUB_TOKEN`). Regression Guard must pass. Reviewers inspect stale relations, witness evidence and the analyzed commit, mark the PR ready and merge manually. No auto-merge or bypass.
6. The snapshot-only merge itself triggers a development push, but the updater reports no-op when no source change occurred, so it does not reopen an empty PR. A fresh source push may create another snapshot PR later.

**Limits:** Source edits that invalidate semantic relations mark those edges stale/needs-review, not automatically re-verified. The checked-in graph may lag until the draft PR is reviewed and merged. Incremental source re-reading is from the last persisted checkpoint; a long unmerged PR can accumulate multiple source diffs but will not automatically scan all source contents.

## One-time GitHub App setup (owner required)

Create a dedicated GitHub App, install it **only** on `iransearch/IRSPEEDY-WIN`, and grant minimum repository permissions:

- **Contents:** Read and write (to push only `automation/codegraph-reviewed-snapshot`).
- **Pull requests:** Read and write (to open/update a draft PR).
- **Workflows:** No access required because the generated PR never changes `.github/workflows`.

In the GitHub repository Settings > Secrets and variables > Actions, add:

- Repository **variable** `CODEGRAPH_APP_CLIENT_ID`: the installed App's Client ID.
- Repository **secret** `CODEGRAPH_APP_PRIVATE_KEY`: the App private key, as PEM text.

Never paste a private key in PRs, chat, source or plain repository variables. A missing variable/secret or an uninstalled App makes the automatic proposal job **fail clearly**, not silently pretend the graph is persisted.

The action uses a short-lived installation token and explicitly requests `contents:write` plus `pull-requests:write` for this repository; its token is revoked after the job.

The existing development branch Ruleset requires `Fast offline regression (Linux .NET 8)`. Do not grant a bypass to the App. The bot updates its own **unprotected snapshot branch**, not the protected development branch, and humans merge the reviewed PR with normal status checks.

## Validation and incident handling

- The workflow first confirms the remote development HEAD matches the push SHA; superseded runs end without submitting an out-of-date PR.
- The generated report is checked for failure and exact target commit. Unexpected workspace modifications abort the job.
- `tests/CodegraphChecks/test_reviewed_snapshot_pr.py` checks the workflow boundaries.
- Check the **Codegraph Snapshot Pull Request** Actions run after setup and ensure a draft PR appears only if there is an actual graph diff.
- Confirm that the PR's **Fast offline regression (Linux .NET 8)** status check runs and succeeds; do not merge without it.
- If the App is misconfigured, fix only the app installation or repository secret/variable; do not fall back to pushing with `GITHUB_TOKEN` or disabling required checks.
- A branch-protection rejection or a diff containing files outside the allowlist must remain a failure requiring review.

This workflow does not alter VPN runtime, Login, DNS Recovery, token security, Windows build inputs, or `main`.
