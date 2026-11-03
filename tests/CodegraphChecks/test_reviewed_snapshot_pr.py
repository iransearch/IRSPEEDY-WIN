"""Fail-closed contract for GITHUB_TOKEN proposed Codegraph snapshots."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
WORKFLOW=ROOT/'.github/workflows/codegraph-snapshot-pr.yml'

class ReviewedCodegraphPRTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.yml=WORKFLOW.read_text(encoding='utf-8')

    def test_trusted_push_only_and_branch_scoped(self):
        y=self.yml
        self.assertIn('branches: [agent/core-startup-diagnostics-fix]',y)
        self.assertNotIn('pull_request_target:',y)
        self.assertNotIn('branches: [main]',y)
        self.assertIn('ref: agent/core-startup-diagnostics-fix',y)
        self.assertIn('base: agent/core-startup-diagnostics-fix',y)
        self.assertIn('git ls-remote origin refs/heads/agent/core-startup-diagnostics-fix',y)

    def test_no_app_or_secrets_or_direct_development_push(self):
        y=self.yml
        self.assertIn('permissions:\n  contents: write\n  pull-requests: write\n  actions: write',y)
        self.assertIn('persist-credentials: false',y)
        self.assertIn('token: ${{ github.token }}',y)
        self.assertNotIn('CODEGRAPH_APP_',y)
        self.assertNotIn('create-github-app-token',y)
        self.assertNotIn('git push',y)
        self.assertNotIn('tools/codegraph/publish.py',y)

    def test_single_draft_proposal_only_generated_paths(self):
        y=self.yml
        self.assertIn('branch: automation/codegraph-reviewed-snapshot',y)
        self.assertIn('draft: true',y)
        self.assertIn("if: steps.diff.outputs.changed == 'true'",y)
        self.assertIn("if paths-allowed:",y)
        for filename in ('manifest.json','dependencies.md','status.md','ui-index.json'):
            self.assertIn('docs/codegraph/'+filename,y)

    def test_required_check_explicitly_dispatched_after_pr_update(self):
        y=self.yml
        self.assertIn('workflow_dispatch',y)
        self.assertIn('gh workflow run regression-guard.yml',y)
        self.assertIn('--ref automation/codegraph-reviewed-snapshot',y)
        self.assertIn('pull-request-head-sha',y)
        self.assertIn("pull-request-operation == 'updated'",y)
        self.assertIn("pull-request-operation == 'created'",y)
        self.assertIn('test "$ACTUAL_SHA" = "$PR_HEAD_SHA"',y)

if __name__=='__main__':
    unittest.main()
