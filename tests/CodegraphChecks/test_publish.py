from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/codegraph'))
import publish as pub
from test_update import Repository


class PublishingChecks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        (self.root/'work').mkdir()
        self.r=Repository(self.root/'work')
        self.remote=self.root/'remote.git'
        subprocess.run(['git','init','--bare','-q',str(self.remote)],check=True)
        self.r.git('remote','add','origin',str(self.remote));self.r.git('push','-q','origin',pub.BRANCH)
        self.head=self.r.git('rev-parse','HEAD').strip();self.ref='refs/heads/'+pub.BRANCH

    def remote_head(self):
        return subprocess.check_output(['git','--git-dir='+str(self.remote),'rev-parse',self.ref]).decode().strip()

    def test_main_or_other_ref_is_rejected_before_publication(self):
        for ref in ['refs/heads/main','refs/heads/other']:
            with self.assertRaises(ValueError):pub.publish(self.r.path,self.head,ref)
        self.assertEqual(self.head,self.remote_head())

    def test_staged_vpn_file_is_never_published(self):
        self.r.write('src/A.cs','changed');self.r.git('add','src/A.cs')
        with self.assertRaises(ValueError):pub.publish(self.r.path,self.head,self.ref)
        self.assertEqual(self.head,self.remote_head())

    def test_no_change_does_not_make_another_bot_commit(self):
        self.assertEqual('no-change',pub.publish(self.r.path,self.head,self.ref)['publication_status'])
        self.assertEqual(self.head,self.remote_head())

    def test_generated_output_only_commit_and_next_run_noop(self):
        self.r.write('docs/codegraph/status.md','generated')
        result=pub.publish(self.r.path,self.head,self.ref)
        self.assertEqual('pushed',result['publication_status'])
        files=self.r.git('diff','--name-only',self.head,result['published_commit']).splitlines()
        self.assertEqual(['docs/codegraph/status.md'],files)
        self.assertEqual('no-change',pub.publish(self.r.path,result['published_commit'],self.ref)['publication_status'])

    def test_developer_push_wins_and_old_checkout_never_force_pushes(self):
        self.r.write('docs/codegraph/status.md','newer');new=self.r.commit();self.r.git('push','-q','origin',pub.BRANCH)
        self.r.git('reset','--hard',self.head)
        self.assertEqual('superseded',pub.publish(self.r.path,self.head,self.ref)['publication_status'])
        self.assertEqual(new,self.remote_head())

    def test_edited_checkout_is_rejected(self):
        self.r.write('docs/codegraph/status.md','local');self.r.commit()
        with self.assertRaises(ValueError):pub.publish(self.r.path,self.head,self.ref)


if __name__=='__main__':unittest.main(verbosity=2)
