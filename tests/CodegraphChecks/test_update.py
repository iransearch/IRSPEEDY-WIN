"""Real local Git repositories; no network, credentials or VPN processes."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2]/'tools/codegraph/update.py'
spec = importlib.util.spec_from_file_location('codegraph', SCRIPT)
cg = importlib.util.module_from_spec(spec); spec.loader.exec_module(cg)


class Repository:
    def __init__(self, path, extra=0):
        self.path = Path(path)
        self.git('init','-q','-b',cg.BRANCH)
        self.git('config','user.name','Graph Tests'); self.git('config','user.email','graph@example.invalid')
        for p, text in {'src/A.cs':'class A { void Run() { B.Go(); } }\n',
                        'src/B.cs':'class B { public static void Go() {} }\n',
                        'other/C.cs':'class C { void Run() { D.Go(); } }\n',
                        'other/D.cs':'class D { public static void Go() {} }\n',
                        'src/App.csproj':'<Project><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Alpha" Version="1" /></ItemGroup></Project>\n',
                        'other/Other.csproj':'<Project />\n'}.items(): self.write(p,text)
        for i in range(extra): self.write(f'assets/{i}.txt','never read\n')
        base=self.commit()
        inventory={}
        for record in self.git('ls-tree','-r','-z',base).split('\0'):
            if record:
                meta,path=record.split('\t');inventory[path]=meta.split()[2]
        relations=[]
        for i,(source,target) in enumerate([('src/A.cs','src/B.cs'),('other/C.cs','other/D.cs')]):
            relations.append(dict(id=f'R{i+1}',module='ui',kind='calls',**{'from':source,'to':target},
                source_symbol=source[4:-3]+'.Run' if i==0 else 'C.Run',target_symbol='B.Go' if i==0 else 'D.Go',
                evidence={'path':source,'line':1,'text':(self.path/source).read_text().strip()}))
        self.seed=dict(schema_version='1.0.0',repository='example/repo',base_branch=cg.BRANCH,base_commit=base,
            validity={'status':'verified-for-base-commit'},file_inventory=inventory,relationships=relations,
            modules=[dict(id='ui',files=['src/A.cs','src/B.cs','other/C.cs','other/D.cs'],entrypoints=['src/A.cs'],relation_ids=['R1','R2'],status='primary-path-mapped')],
            projects=[cg.build_items('src/App.csproj',(self.path/'src/App.csproj').read_text())[1],cg.build_items('other/Other.csproj','<Project />')[1]],counts={})
        m,_=cg.update(cg.Git(self.path),self.seed,base)
        self.save(m); self.commit()

    def git(self,*args):
        return subprocess.check_output(['git',*args],cwd=self.path,stderr=subprocess.PIPE).decode()
    def write(self,path,text):
        p=self.path/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text)
    def commit(self):
        self.git('add','-A');self.git('commit','-qm','fixture');return self.git('rev-parse','HEAD').strip()
    def manifest(self):return json.loads((self.path/cg.OUTPUTS[0]).read_text())
    def save(self,m):self.write(cg.OUTPUTS[0],json.dumps(m))
    def update(self):
        git=cg.Git(self.path);m,r=cg.update(git,self.manifest(),'HEAD');return m,r,git


class Checks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.r=Repository(self.temp.name)

    def test_unchanged_and_generated_only_noop_reads_zero(self):
        self.r.write('docs/codegraph/status.md','bot update');self.r.commit()
        m,r,git=self.r.update()
        self.assertTrue(r['noop']);self.assertEqual(set(),git.reads)
        self.assertEqual(self.r.manifest(),m)

    def test_changed_source_invalidates_without_guessing_calls(self):
        self.r.write('src/A.cs','class A { void Run() {} }\n');self.r.commit()
        m,r,git=self.r.update()
        self.assertEqual('stale',m['relationships'][0]['status'])
        self.assertEqual('verified',m['relationships'][1]['status'])
        self.assertEqual('needs-review',r['validity']);self.assertEqual({'src/A.cs'},git.reads)
        self.assertEqual(1,r['relationships_changed'])

    def test_matching_text_after_change_is_not_automatic_semantic_proof(self):
        self.r.write('src/A.cs','// class A { void Run() { B.Go(); } }\n');self.r.commit()
        m,_,_=self.r.update();self.assertEqual('stale',m['relationships'][0]['status'])

    def test_target_change_invalidates_its_callers(self):
        self.r.write('src/B.cs','class B { public static void Different() {} }\n');self.r.commit()
        m,_,git=self.r.update();self.assertEqual('stale',m['relationships'][0]['status'])
        self.assertNotIn('src/A.cs',git.reads)

    def test_exact_source_rename_updates_paths_and_keeps_historical_witness(self):
        self.r.git('mv','src/A.cs','src/Renamed.cs');self.r.commit()
        m,r,_=self.r.update();edge=m['relationships'][0]
        self.assertEqual('src/Renamed.cs',edge['from']);self.assertEqual('stale',edge['status'])
        self.assertEqual('src/A.cs',edge['evidence']['path'])
        self.assertNotIn('src/A.cs',m['file_inventory']);self.assertIn('src/Renamed.cs',m['modules'][0]['entrypoints'])
        self.assertTrue(any(c['status'].startswith('R') for c in r['changes']))

    def test_target_rename_invalidates_reverse_edge(self):
        self.r.git('mv','src/B.cs','src/NewB.cs');self.r.commit()
        m,_,_=self.r.update();self.assertEqual('src/NewB.cs',m['relationships'][0]['to'])
        self.assertEqual('stale',m['relationships'][0]['status'])

    def test_deletion_keeps_stale_audit_edge_and_removes_inventory(self):
        self.r.git('rm','src/B.cs');self.r.commit()
        m,_,_=self.r.update();self.assertNotIn('src/B.cs',m['file_inventory'])
        self.assertEqual('endpoint-deleted',m['relationships'][0]['stale_reason'])
        cg.validate(m)

    def test_project_reference_added_only_with_literal_source_witness(self):
        self.r.write('src/App.csproj','<Project>\n<ItemGroup>\n<ProjectReference Include="../other/Other.csproj" />\n</ItemGroup>\n</Project>\n');self.r.commit()
        m,r,_=self.r.update();edges=[x for x in m['relationships'] if x['kind']=='project_reference']
        self.assertEqual(1,len(edges));edge=edges[0]
        self.assertEqual('other/Other.csproj',edge['to']);self.assertEqual('verified',edge['status'])
        self.assertEqual(3,edge['evidence']['line']);self.assertIn('ProjectReference',edge['evidence']['text'])
        self.assertGreater(r['dependency_declarations_changed'],0)
        self.assertEqual('stale',m['relationships'][0]['status'])
        self.assertEqual('verified',m['relationships'][1]['status'])

    def test_package_version_change_updates_metadata_and_invalidates_runtime_edges(self):
        self.r.write('src/App.csproj','<Project><ItemGroup><PackageReference Include="Alpha" Version="2" /></ItemGroup></Project>');self.r.commit()
        m,r,_=self.r.update()
        self.assertIn('nuget:Alpha@2',m['external_nodes'])
        self.assertEqual('2',next(p for p in m['projects'] if p['path']=='src/App.csproj')['package_references'][0]['Version'])
        self.assertEqual('build-dependency-changed',m['relationships'][0]['stale_reason'])
        self.assertEqual(2,r['dependency_declarations_changed'])

    def test_framework_or_property_change_invalidates_project_runtime_edges(self):
        text=(self.r.path/'src/App.csproj').read_text().replace('net48','net472')
        self.r.write('src/App.csproj',text);self.r.commit();m,_,_=self.r.update()
        self.assertEqual('build-dependency-changed',m['relationships'][0]['stale_reason'])

    def test_deleted_project_invalidates_its_runtime_edges(self):
        self.r.git('rm','src/App.csproj');self.r.commit();m,_,_=self.r.update()
        self.assertEqual('stale',m['relationships'][0]['status'])
        self.assertNotIn('src/App.csproj',{p['path'] for p in m['projects']})

    def test_build_property_input_change_is_conservative_without_reading_all_sources(self):
        self.r.write('Directory.Build.props','<Project><PropertyGroup><DefineConstants>NEW</DefineConstants></PropertyGroup></Project>');self.r.commit()
        m,_,git=self.r.update();self.assertTrue(all(e['status']=='stale' for e in m['relationships']))
        self.assertEqual({'Directory.Build.props'},git.reads)

    def test_namespaced_nested_package_version_is_unresolved_not_guessed(self):
        text='<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PackageReference Include="Alpha"><Version>2</Version></PackageReference></Project>'
        items,_,unresolved=cg.build_items('src/App.csproj',text)
        self.assertEqual([],items);self.assertEqual(['nested-package-version'],unresolved)

    def test_removed_auto_dependency_becomes_stale(self):
        self.r.write('src/Inputs.targets','<Project><ItemGroup><Compile Include="B.cs" /></ItemGroup></Project>');self.r.commit()
        m,_,_=self.r.update();self.r.save(m);self.r.commit()
        self.r.write('src/Inputs.targets','<Project />');self.r.commit()
        m,_,_=self.r.update();edge=next(x for x in m['relationships'] if x['kind']=='includes_source')
        self.assertEqual('stale',edge['status'])

    def test_simultaneous_source_and_target_changes_are_processed_together(self):
        for p in ['src/A.cs','src/B.cs','other/C.cs']:self.r.write(p,'class Replacement {}\n')
        self.r.commit();m,r,git=self.r.update()
        self.assertEqual(3,r['files_examined']);self.assertEqual(2,r['relationships_changed'])
        self.assertEqual(3,len(git.reads));self.assertTrue(all(x['status']=='stale' for x in m['relationships']))

    def test_new_runtime_source_is_pending_not_an_invented_edge(self):
        self.r.write('src/New.cs','class New { void Run() { B.Go(); } }');self.r.commit()
        m,r,_=self.r.update();self.assertEqual(0,r['new_verified_relations'])
        self.assertIn('src/New.cs',m['pending_review_files']);self.assertEqual('needs-review',r['validity'])

    def test_malformed_project_is_not_valid(self):
        self.r.write('src/App.csproj','<Project>');self.r.commit()
        m,r,_=self.r.update();self.assertEqual('needs-review',r['validity'])
        self.assertIn('src/App.csproj',m['unresolved_build_items'])

    def test_nonliteral_or_missing_dependency_not_invented(self):
        self.r.write('src/Inputs.targets','<Project><Import Project="$(Unknown)/A.targets"/><ItemGroup><Compile Include="Missing.cs"/></ItemGroup></Project>');self.r.commit()
        m,r,_=self.r.update();self.assertEqual(0,r['new_verified_relations'])
        self.assertEqual('needs-review',r['validity'])

    def test_xml_commented_reference_is_not_evidence(self):
        text='<Project><!-- <ProjectReference Include="../other/Other.csproj" /> --></Project>'
        self.assertEqual([],cg.build_items('src/App.csproj',text)[0])

    def test_old_stale_manual_edge_is_never_revived_by_restored_text(self):
        original=(self.r.path/'src/A.cs').read_text()
        self.r.write('src/A.cs','class A {}');self.r.commit();m,_,_=self.r.update();self.r.save(m);self.r.commit()
        self.r.write('src/A.cs',original);self.r.commit();m,_,_=self.r.update()
        self.assertEqual('stale',m['relationships'][0]['status'])

    def test_same_blob_mode_change_reads_no_content(self):
        self.r.git('update-index','--chmod=+x','src/A.cs');self.r.git('commit','-qm','mode')
        m,r,git=self.r.update();self.assertEqual(set(),git.reads)
        self.assertEqual('verified',m['relationships'][0]['status'])

    def test_invalid_seed_witness_stays_stale(self):
        seed=copy.deepcopy(self.r.seed);seed['relationships'][0]['evidence']['text']='made up'
        m,r,_=self.r.update()
        out,_=cg.update(cg.Git(self.r.path),seed,seed['base_commit'])
        self.assertEqual('stale',out['relationships'][0]['status']);self.assertEqual('needs-review',out['validity']['status'])

    def test_pending_seed_requires_a_recorded_prior_manual_review(self):
        seed=copy.deepcopy(self.r.seed);seed['validity']['status']='pending-validation'
        with self.assertRaises(ValueError):cg.update(cg.Git(self.r.path),seed,seed['base_commit'])
        seed['bootstrap_manual_review']={'commit':seed['base_commit'],'previous_status':'verified-for-base-commit'}
        out,_=cg.update(cg.Git(self.r.path),seed,seed['base_commit'])
        self.assertEqual('validated-for-analyzed-commit',out['validity']['status'])

    def test_inventory_base_mismatch_fails_closed(self):
        self.r.write('src/A.cs','class A {}');self.r.commit();m=self.r.manifest();m['file_inventory']['src/A.cs']='0'*40
        with self.assertRaises((ValueError,AssertionError)):cg.update(cg.Git(self.r.path),m,'HEAD')

    def test_hand_edited_verified_edge_without_witness_validation_is_rejected(self):
        m=self.r.manifest();m['relationships'][0]['evidence']['text']='invented call'
        with self.assertRaises(ValueError):cg.update(cg.Git(self.r.path),m,'HEAD')

    def test_transitive_project_dependency_invalidates_dependents(self):
        self.r.write('src/App.csproj','<Project><ItemGroup><ProjectReference Include="../other/Other.csproj" /></ItemGroup></Project>')
        self.r.commit();m,_,_=self.r.update()
        # Deliberately reviewed baseline for the fixture's existing runtime edge.
        m['relationships'][0].update(status='verified');m['relationships'][0].pop('stale_reason',None)
        m['validation_digest']=cg.digest(m);self.r.save(m);self.r.commit()
        self.r.write('other/Other.csproj','<Project><ItemGroup><PackageReference Include="Transitive" Version="1" /></ItemGroup></Project>')
        self.r.commit();m,_,git=self.r.update()
        self.assertEqual('build-dependency-changed',m['relationships'][0]['stale_reason'])
        self.assertNotIn('src/A.cs',git.reads)

    def test_missing_history_fails_without_full_scan(self):
        m=self.r.manifest();m['base_commit']='0'*40;git=cg.Git(self.r.path)
        with self.assertRaises(subprocess.CalledProcessError):cg.update(git,m,'HEAD')
        self.assertEqual(set(),git.reads)

    def test_dry_run_changes_no_versioned_file_and_reports_failure(self):
        before=self.r.git('status','--porcelain');report=Path(self.temp.name)/'report.json'
        env={**os.environ,'GITHUB_REPOSITORY':'example/repo'}
        result=subprocess.run([sys.executable,str(SCRIPT),'--repo',str(self.r.path),'--dry-run','--report',str(report)],capture_output=True,env=env)
        self.assertEqual(0,result.returncode);self.assertEqual(before,self.r.git('status','--porcelain','--untracked-files=no'))
        self.assertTrue(json.loads(report.read_text())['dry_run'])
        result=subprocess.run([sys.executable,str(SCRIPT),'--repo',str(self.r.path),'--target','missing','--dry-run','--report',str(report)],capture_output=True,env=env)
        self.assertEqual(1,result.returncode);self.assertEqual('failed',json.loads(report.read_text())['validity'])

    def test_no_full_content_scan_with_981_baseline_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo=Repository(tmp,extra=975)
            self.assertEqual(981,len(repo.manifest()['file_inventory']))
            repo.write('src/A.cs','class A {}');repo.commit();m,r,git=repo.update()
            self.assertEqual({'src/A.cs'},git.reads);self.assertGreaterEqual(r['unchanged_files_not_read'],980)


if __name__=='__main__':
    unittest.main(verbosity=2)
