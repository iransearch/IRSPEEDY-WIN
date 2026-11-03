"""Offline tests for incremental WPF UI index."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'tools'/'codegraph'))
import ui
from update import Git


def git(root, *cmd):
    return subprocess.check_output(['git', *cmd], cwd=root).decode().strip()


class UiMapTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        git(self.root, 'init', '-q')
        git(self.root, 'config', 'user.name', 'Graph Test')
        git(self.root, 'config', 'user.email', 'test@example.com')

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, path, value):
        file = self.root/path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(value)

    def commit(self):
        git(self.root, 'add', '-A')
        git(self.root, 'commit', '-qm', 'fixture')
        return git(self.root, 'rev-parse', 'HEAD')

    def inventory(self):
        rows = git(self.root, 'ls-tree', '-r', 'HEAD').splitlines()
        return {row.split('\t')[1]:row.split()[2] for row in rows}

    def fixture(self):
        self.write('UI/Login.xaml',
            '<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" '
            'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Test.Login">'
            '<Button x:Name="Connect" Click="Connect_Click" '
            'Background="{DynamicResource AccentBrush}"/></UserControl>')
        self.write('UI/Login.xaml.cs', 'namespace Test { public partial class Login { } }')
        return self.commit()

    def test_bootstrap_pairs_and_explicit_witnesses(self):
        sha = self.fixture()
        graph, report = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        node = graph['components']['UI/Login.xaml']
        self.assertEqual(node['codebehind']['status'], 'verified-declaration')
        self.assertEqual(node['names'][0]['name'], 'Connect')
        self.assertEqual(node['events'][0]['handler'], 'Connect_Click')
        self.assertEqual(node['events'][0]['status'], 'xaml-declaration-only')
        self.assertEqual(node['resource_references'][0]['key'], 'AccentBrush')
        self.assertEqual(report['ui_xaml_reparsed'], 1)
        self.assertTrue(node['evidence']['x_class']['line'] > 0)

    def test_unrelated_commit_reads_no_ui(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        self.write('README.md', 'unrelated')
        newer = self.commit()
        result, report = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertEqual(result, graph)
        self.assertEqual(report['ui_files_read'], 0)

    def test_incremental_xaml_change(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        self.write('UI/Login.xaml', (self.root/'UI/Login.xaml').read_text().replace('Connect_Click','ConnectNew_Click'))
        newer = self.commit()
        result, report = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertEqual(report['ui_xaml_reparsed'], 1)
        self.assertEqual(result['components']['UI/Login.xaml']['events'][0]['handler'],'ConnectNew_Click')
        self.assertLessEqual(report['ui_files_read'],2)

    def test_codebehind_change_revalidates_pair(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        self.write('UI/Login.xaml.cs', 'namespace Test { public class Login { } }')
        newer = self.commit()
        result, report = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertEqual(report['ui_xaml_reparsed'], 0)
        self.assertEqual(result['components']['UI/Login.xaml']['codebehind']['status'],'needs-review')

    def test_deleted_xaml_removes_component(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        (self.root/'UI/Login.xaml').unlink()
        newer = self.commit()
        result, _ = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertNotIn('UI/Login.xaml', result['components'])

    def test_rename_updates_path_without_inventing_class(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        git(self.root, 'mv', 'UI/Login.xaml', 'UI/NewLogin.xaml')
        newer = self.commit()
        result, _ = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertNotIn('UI/Login.xaml', result['components'])
        self.assertIn('UI/NewLogin.xaml', result['components'])
        self.assertEqual(result['components']['UI/NewLogin.xaml']['codebehind']['status'],'missing')

    def test_multi_file_change_scope(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        self.write('UI/Login.xaml', (self.root/'UI/Login.xaml').read_text().replace('Connect_Click','Tap_Click'))
        self.write('README.md', 'something')
        self.write('UI/Login.xaml.cs', 'namespace Test { public partial class Login { } } // edited')
        newer = self.commit()
        result, report = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertEqual(report['ui_xaml_reparsed'],1)
        self.assertEqual(result['components']['UI/Login.xaml']['events'][0]['handler'],'Tap_Click')

    def test_invalid_xaml_remains_pending(self):
        sha = self.fixture()
        graph, _ = ui.synchronize(Git(self.root), None, self.inventory(), sha)
        self.write('UI/Login.xaml', '<UserControl><Button>')
        newer = self.commit()
        result, _ = ui.synchronize(Git(self.root), graph, self.inventory(), newer)
        self.assertEqual(result['components']['UI/Login.xaml']['status'],'needs-review')


if __name__ == '__main__':
    unittest.main()
