"""Ensure protected-branch Codegraph CI cannot directly publish commits."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "codegraph.yml"

class GraphWorkflowSafetyTests(unittest.TestCase):
    def test_branch_scoped_and_read_only(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("branches: [agent/core-startup-diagnostics-fix]", text)
        self.assertIn("permissions:\n  contents: read", text)
        self.assertIn("persist-credentials: false", text)
        self.assertNotIn("contents: write", text)
        self.assertNotIn("tools/codegraph/publish.py", text)
        self.assertNotIn("git push", text)

    def test_actual_snapshot_is_uploaded_not_just_trial(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("python3 tools/codegraph/update.py --target", text)
        self.assertNotIn("--dry-run", text)
        for path in ("manifest.json", "dependencies.md", "status.md", "ui-index.json"):
            self.assertIn("docs/codegraph/" + path, text)
        self.assertIn("actions/upload-artifact@", text)

if __name__ == "__main__":
    unittest.main()
