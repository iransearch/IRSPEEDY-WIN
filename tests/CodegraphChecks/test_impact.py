"""Regression tests for the read-only Codegraph impact report.

Synthetic temporary repositories; no network, VPN Core, credentials or Windows runtime.
"""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools' / 'codegraph'))
from impact import analyze, markdown
from update import Git, digest


class ImpactAnalysisChecks(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.git('init', '-q')
        self.git('config', 'user.email', 'codegraph-test@example.invalid')
        self.git('config', 'user.name', 'Codegraph Tests')
        self.write('A.cs', '// A -> B\n')
        self.write('B.cs', '// B changed later\n')
        self.write('C.cs', '// C -> A\n')
        self.write('Legacy.cs', '// Legacy -> B\n')
        self.write('Unmapped.cs', '// indexed, no relations\n')
        self.write('tests/ExampleChecks/run.py', '# direct source witness\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'base')
        self.base = self.git('rev-parse', 'HEAD').strip()
        inventory = {p: self.git('rev-parse', 'HEAD:'+p).strip()
                     for p in ('A.cs', 'B.cs', 'C.cs', 'Legacy.cs', 'Unmapped.cs', 'tests/ExampleChecks/run.py')}
        def edge(eid, source, target, status='verified'):
            blob = inventory[source]
            record = {
                'id': eid, 'module': 'synthetic', 'kind': 'calls',
                'from': source, 'to': target,
                'evidence': {'path': source, 'line': 1, 'text': '// test witness',
                             'blob': blob, 'commit': self.base},
                'source_blob': blob, 'target_blob': inventory[target],
                'status': status, 'last_verified_commit': self.base,
                'origin': 'manual',
            }
            if status == 'stale':
                record['stale_reason'] = 'old endpoint changed'
            return record
        self.manifest = {
            'schema_version': '2.0.0', 'repository': 'iransearch/IRSPEEDY-WIN',
            'base_branch': 'agent/core-startup-diagnostics-fix',
            'base_commit': self.base, 'file_inventory': inventory,
            'validity': {'status': 'needs-review'},
            'relationships': [edge('R001', 'A.cs', 'B.cs'),
                              edge('R002', 'C.cs', 'A.cs'),
                              edge('R003', 'Legacy.cs', 'B.cs', 'stale')],
            'modules': [{'id': 'synthetic', 'files': ['A.cs', 'B.cs', 'C.cs', 'Legacy.cs'],
                         'entrypoints': ['A.cs'], 'relation_ids': ['R001', 'R002', 'R003']}],
            'pending_review_files': {'Legacy.cs': 'requires-source-review'},
        }
        self.manifest['validation_digest'] = digest(self.manifest)
        self.write('docs/codegraph/manifest.json', json.dumps(self.manifest))
        self.git('add', 'docs/codegraph/manifest.json')
        self.git('commit', '-qm', 'graph checkpoint')

    def write(self, path, content):
        dest = self.root / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(content, encoding='utf8')

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root,
                                       stderr=subprocess.PIPE).decode('utf8')

    def commit_change(self, path, content):
        self.write(path, content)
        self.git('add', '-A')
        self.git('commit', '-qm', 'change '+path)

    def scan(self, **kwargs):
        return analyze(Git(self.root), self.manifest, **kwargs)

    def test_reverse_dependency_impact_only(self):
        self.commit_change('B.cs', '// changed B\n')
        result = self.scan()
        self.assertEqual(result['changed_file_count'], 1)
        self.assertEqual(result['changed_files'][0]['path'], 'B.cs')
        paths = [r['path'] for r in result['potential_dependents']]
        self.assertEqual(paths, ['A.cs', 'Legacy.cs', 'C.cs'])
        self.assertEqual(result['potential_dependents'][0]['via_relations'], ['R001'])
        self.assertTrue(next(x for x in result['potential_dependents']
                             if x['path'] == 'Legacy.cs')['has_stale_evidence'])
        self.assertEqual(result['preexisting_stale_relations'], 1)
        self.assertEqual(result['preexisting_pending_review_files'], 1)
        self.assertIn('not runtime proof', markdown(result))

    def test_graph_only_commit_is_noop(self):
        result = self.scan()
        self.assertEqual(result['changed_files'], [])
        self.assertEqual(result['potential_dependents'], [])
        self.assertEqual(result['directly_touched_relations'], [])

    def test_unmapped_file_is_reported_without_made_up_edges(self):
        self.commit_change('New.cs', '// unrelated new file\n')
        result = self.scan()
        self.assertEqual(result['potential_dependents'], [])
        self.assertIn('New.cs', result['unmapped_changed_paths'])
        self.assertEqual(result['affected_modules'], [])

    def test_inventory_only_file_has_no_assumed_edges(self):
        self.commit_change('Unmapped.cs', '// changed but not mapped\n')
        result = self.scan()
        self.assertEqual(result['potential_dependents'], [])
        self.assertIn('Unmapped.cs', result['unmapped_changed_paths'])

    def test_renamed_endpoint_uses_both_paths(self):
        self.git('mv', 'B.cs', 'Renamed.cs')
        self.git('commit', '-qm', 'rename')
        result = self.scan()
        self.assertEqual(result['changed_file_count'], 1)
        self.assertEqual(result['changed_files'][0]['old_path'], 'B.cs')
        self.assertIn('A.cs', [x['path'] for x in result['potential_dependents']])

    def test_deleted_target_remains_impactful(self):
        self.git('rm', 'B.cs')
        self.git('commit', '-qm', 'delete')
        result = self.scan()
        self.assertEqual(result['changed_files'][0]['status'], 'D')
        self.assertIn('A.cs', [x['path'] for x in result['potential_dependents']])

    def test_depth_limit_is_explicit(self):
        self.commit_change('B.cs', '// changed\n')
        result = self.scan(max_depth=1)
        self.assertTrue(result['depth_limit_reached'])
        self.assertNotIn('C.cs', [x['path'] for x in result['potential_dependents']])

    def test_tampered_graph_fails_closed(self):
        broken = copy.deepcopy(self.manifest)
        broken['pending_review_files'].clear()
        with self.assertRaisesRegex(ValueError, 'digest mismatch'):
            analyze(Git(self.root), broken)

    def test_main_branch_is_rejected(self):
        broken = copy.deepcopy(self.manifest)
        broken['base_branch'] = 'main'
        with self.assertRaisesRegex(ValueError, 'development branch'):
            analyze(Git(self.root), broken)

    def test_dirty_tracked_checkout_refused(self):
        self.write('A.cs', '// uncommitted\n')
        with self.assertRaisesRegex(ValueError, 'dirty'):
            self.scan()

    def test_unrelated_target_ancestry_is_not_assumed(self):
        # Separate orphan root is not a descendant of the analyzed graph base.
        self.git('checkout', '--orphan', 'unrelated')
        self.git('rm', '-rf', '.')
        self.write('Other.cs', '// unrelated history\n')
        self.git('add', '-A')
        self.git('commit', '-qm', 'unrelated history')
        with self.assertRaisesRegex(ValueError, 'ancestor'):
            self.scan()


    def test_advisory_suite_is_reported_without_assertion_coverage_claim(self):
        witness = 'tests/ExampleChecks/run.py'
        dest = 'B.cs'
        source_blob = self.manifest['file_inventory'][witness]
        self.manifest['relationships'].append({
            'id': 'T001', 'module': 'tests', 'kind': 'reads_source',
            'from': witness, 'to': dest, 'origin': 'reviewed-test-witness',
            'status': 'verified', 'source_blob': source_blob,
            'target_blob': self.manifest['file_inventory'][dest],
            'evidence': {'path': witness, 'line': 1,
                         'text': '# direct source witness', 'blob': source_blob,
                         'commit': self.base},
            'last_verified_commit': self.base,
            'verification': 'literal-harness-file-read-only',
        })
        self.manifest['validation_digest'] = digest(self.manifest)
        self.commit_change('B.cs', '// changed target\n')
        result = self.scan()
        self.assertEqual(len(result['related_test_suites']), 1)
        suite = result['related_test_suites'][0]
        self.assertEqual(suite['suite'], 'tests/ExampleChecks')
        self.assertTrue(suite['direct_source_change'])
        self.assertEqual(suite['evidence_level'], 'source-inclusion-or-read-only')
        self.assertEqual(suite['relation_ids'], ['T001'])
        self.assertIn('Related test suites', markdown(result))
        self.assertIsNone(result['pr_change_count'])

    def test_pr_only_changes_are_separate_from_checkpoint_diff(self):
        self.commit_change('A.cs', '// earlier commit\n')
        pr_start = self.git('rev-parse', 'HEAD').strip()
        self.commit_change('B.cs', '// this PR\n')
        result = self.scan(pr_base=pr_start)
        self.assertEqual(result['changed_file_count'], 2)
        self.assertEqual(result['pr_change_count'], 1)
        self.assertEqual(result['pr_changed_files'][0]['path'], 'B.cs')
        self.assertEqual(result['pr_application_change_count'], 0)
        self.assertIn('PR-only changes', markdown(result))

    def test_invalid_pr_base_fails_closed(self):
        self.git('checkout', '--orphan', 'unrelated-pr-start')
        self.git('rm', '-rf', '.')
        self.write('Other.cs', '// unrelated\n')
        self.git('add', '-A')
        self.git('commit', '-qm', 'unrelated')
        orphan = self.git('rev-parse', 'HEAD').strip()
        self.git('checkout', '-q', self.base)
        with self.assertRaisesRegex(ValueError, 'PR base'):
            self.scan(pr_base=orphan)


if __name__ == '__main__':
    unittest.main()
