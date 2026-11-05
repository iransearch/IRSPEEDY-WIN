"""Fail-closed tests for reviewed literal test-suite graph witnesses."""
import copy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools' / 'codegraph'))
import test_links
from update import validate


class SourceReader:
    def __init__(self, lines):
        self.lines = lines
        self.reads = 0

    def text(self, blob, path):
        self.reads += 1
        return self.lines


class ReviewedTestLinksTests(unittest.TestCase):
    def setUp(self):
        self.source = 'tests/FooChecks/run.py'
        self.dest = 'IRSpeedyVPN/Foo.cs'
        self.witness = "text = (root / 'IRSpeedyVPN/Foo.cs').read_text()"
        self.record = (self.source, self.dest, 'a' * 40, 'b' * 40, 1, self.witness)
        self.manifest = {
            'schema_version': '2.0.0', 'file_inventory': {
                self.source: 'a' * 40, self.dest: 'b' * 40,
            }, 'external_nodes': {}, 'modules': [],
            'relationships': [],
        }
        self.reader = SourceReader(self.witness + '\n')

    def run_seed(self, manifest=None, reader=None):
        report = {}
        with patch.object(test_links, 'WITNESSES', (self.record,)):
            test_links.seed_reviewed_test_links(reader or self.reader,
                                                manifest or self.manifest,
                                                'unused-target', report)
        return report

    def test_exact_reviewed_pins_create_evidence_backed_edge(self):
        result = self.run_seed()
        self.assertEqual(result['new_reviewed_test_relations'], 1)
        self.assertEqual(self.reader.reads, 1)
        rel = self.manifest['relationships'][0]
        self.assertEqual(rel['kind'], 'reads_source')
        self.assertEqual(rel['status'], 'verified')
        self.assertEqual(rel['evidence']['line'], 1)
        self.assertEqual(rel['evidence']['blob'], 'a'*40)
        self.assertEqual(rel['target_blob'], 'b'*40)
        self.assertEqual(rel['verification'], 'literal-harness-file-read-only')
        validate(self.manifest)

    def test_rerun_does_not_revalidate_or_read(self):
        self.run_seed()
        self.reader.reads = 0
        result = self.run_seed()
        self.assertEqual(result['new_reviewed_test_relations'], 0)
        self.assertEqual(self.reader.reads, 0)
        self.assertEqual(len(self.manifest['relationships']), 1)

    def test_stale_link_never_promoted(self):
        self.run_seed()
        rel = self.manifest['relationships'][0]
        rel.update(status='stale', stale_reason='endpoint-or-evidence-changed')
        result = self.run_seed()
        self.assertEqual(result['new_reviewed_test_relations'], 0)
        self.assertEqual(rel['status'], 'stale')
        self.assertEqual(rel['stale_reason'], 'endpoint-or-evidence-changed')

    def test_changed_harness_blob_requires_review(self):
        manifest = copy.deepcopy(self.manifest)
        manifest['file_inventory'][self.source] = 'c'*40
        result = self.run_seed(manifest=manifest)
        self.assertEqual(result['new_reviewed_test_relations'], 0)
        self.assertEqual(len(result['test_links_pending_re_review']), 1)
        self.assertEqual(self.reader.reads, 0)

    def test_changed_application_blob_requires_review(self):
        manifest = copy.deepcopy(self.manifest)
        manifest['file_inventory'][self.dest] = 'd'*40
        result = self.run_seed(manifest=manifest)
        self.assertEqual(result['new_reviewed_test_relations'], 0)
        self.assertEqual(len(manifest['relationships']), 0)

    def test_mismatched_witness_fails_closed(self):
        bad = SourceReader('not the reviewed witness\n')
        with self.assertRaisesRegex(ValueError, 'witness'):
            self.run_seed(reader=bad)
        self.assertEqual(self.manifest['relationships'], [])


if __name__ == '__main__':
    unittest.main()
