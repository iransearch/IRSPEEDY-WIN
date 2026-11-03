# Initial read-only trial

Source reviewed seed: `a57db82d05d3182eba7d98db5a47cf28d4d42f66`.
Trial target: existing PR head `19d4c4500f27ebb1b02f734e1330dffcf0881ced`.
Date: 2026-11-03 UTC. Mode: `--dry-run`; no tracked files were written by the updater.

The local trial used exact SHA-verified original Git commit/tree objects and the real source blobs
needed by graph witnesses; unrelated binary/asset blobs were not downloaded. It was not a synthetic
runtime source change or a live VPN/API test.

| Measurement | Result |
| --- | ---: |
| Original indexed files | 981 |
| Non-generated files changed since seed | 1 (`AGENTS.md`) |
| Changed application/build source files examined | 0 |
| Distinct source witness files read during one-time migration | 32 |
| Existing relations checked | 80 |
| Relations changed (provenance/schema normalization) | 80 |
| New relations / stale relations | 0 / 0 |
| Trial validity for that input snapshot | validated-for-analyzed-commit |

The trial's new inventory would contain 982 entries because `AGENTS.md` did not exist in the original
981-file source snapshot. `docs/codegraph/**` is excluded from the incremental inventory.

35 local automated tests passed using disposable Git repositories and local bare remotes. The scaling
case starts with exactly 981 indexed files, changes one C# file and reads only that file, leaving at
least 980 unchanged files unread. A generated-only next commit reads zero source contents and creates
no further bot commit. Rename, deletion, dependency changes and concurrent updates are also covered.

The submitted seed/current status remains **pending-validation** until the workflow validates the
actual merge/push commit. GitHub-hosted execution and write permissions cannot be certified by this
local trial; the PR is not merged and no application build or VPN runtime test was performed.
