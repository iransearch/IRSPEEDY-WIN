"""Safety contract for reviewed, automatically proposed Codegraph snapshots."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
WORKFLOW=ROOT/'.github/workflows/codegraph-snapshot-pr.yml'

class ReviewedCodegraphPRTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.yml=WORKFLOW.read_text(encoding='utf-8')

    def test_trusted_development_push_only(self):
        y=self.yml
        self.assertIn('branches: [agent/core-startup-diagnostics-fix]', y)
        self.assertNotIn('pull_request_target:',y)
        self.assertNotIn('branches: [main]',y)
        self.assertIn('ref: agent/core-startup-diagnostics-fix',y)
        self.assertIn('base: agent/core-startup-diagnostics-fix',y)

    def test_no_direct_protected_ref_write_or_privileged_default_token(self):
        y=self.yml
        self.assertIn('permissions:\n  contents: read',y)
        self.assertIn('persist-credentials: false',y)
        self.assertNotIn('contents: write\n\nconcurrency',y)
        self.assertNotIn('git push',y)
        self.assertNotIn('tools/codegraph/publish.py',y)
        self.assertIn('permission-contents: write',y)
        self.assertIn('permission-pull-requests: write',y)
        self.assertIn('CODEGRAPH_APP_PRIVATE_KEY',y)

    def test_single_reviewed_pr_and_only_generated_paths(self):
        y=self.yml
        self.assertIn('branch: automation/codegraph-reviewed-snapshot',y)
        self.assertIn('draft: true',y)
        self.assertIn('if: steps.diff.outputs.changed == \'true\'',y)
        self.assertIn('git ls-remote origin refs/heads/agent/core-startup-diagnostics-fix',y)
        self.assertIn("if paths-allowed:",y)
        for name in ('manifest.json','dependencies.md','status.md','ui-index.json'):
            self.assertIn('docs/codegraph/'+name,y)
        self.assertNotIn('delete-branch: false',y)

if __name__=='__main__':
    unittest.main()
