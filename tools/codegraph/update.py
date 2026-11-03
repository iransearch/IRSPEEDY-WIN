#!/usr/bin/env python3
"""Incremental, conservative graph maintenance. Python standard library only."""
import argparse
import copy
import datetime as dt
import hashlib
import html
import json
import os
from pathlib import Path
import posixpath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import ui

BRANCH = 'agent/core-startup-diagnostics-fix'
OUTPUTS = ('docs/codegraph/manifest.json', 'docs/codegraph/dependencies.md', 'docs/codegraph/status.md', 'docs/codegraph/ui-index.json')
BUILD_KINDS = {'solution_member', 'includes_source', 'project_reference', 'imports_project', 'package_reference'}


def generated(path):
    return path.startswith('docs/codegraph/')


def source_file(path):
    return path.endswith(('.cs', '.xaml', '.csproj', '.props', '.targets', '.sln', '.slnx'))


class Git:
    def __init__(self, root):
        self.root = Path(root)
        self.reads = set()
        self.blob_reads = set()

    def run(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, stderr=subprocess.PIPE)

    def commit(self, ref):
        # rev-parse's end-of-options prevents interpreting a ref as a Git option.
        return self.run('rev-parse', '--verify', '--end-of-options', ref + '^{commit}').decode().strip()

    def text(self, sha, path):
        self.reads.add(path)
        self.blob_reads.add((path, sha))
        data = self.run('cat-file', 'blob', sha)
        if len(data) > 8 * 1024 * 1024:
            raise ValueError('Graph source exceeds 8 MiB: ' + path)
        return data.decode('utf-8-sig')

    def changes(self, base, target):
        raw = self.run('diff', '--raw', '-z', '--no-abbrev', '--find-renames=50%', base, target, '--').split(b'\0')
        changes, i = [], 0
        while i < len(raw) and raw[i]:
            oldmode, newmode, oldsha, newsha, status = raw[i].decode().lstrip(':').split()
            path = raw[i+1].decode('utf-8'); i += 2
            newpath = path
            if status.startswith(('R', 'C')):
                newpath = raw[i].decode('utf-8'); i += 1
            changes.append(dict(old_path=path, path=newpath, old_sha=oldsha, sha=newsha,
                                old_mode=oldmode, mode=newmode, status=status))
        return changes


def literal_target(source, value):
    if not value or any(c in value for c in '$%*?;') or re.match(r'^(?:[A-Za-z]:|[/\\])', value):
        return None
    target = posixpath.normpath(posixpath.join(posixpath.dirname(source), value.replace('\\', '/')))
    return None if target == '..' or target.startswith('../') else target


def build_items(path, text):
    """Extract declarations, never evaluate MSBuild or resolve C# calls by name."""
    result, unresolved = [], []
    if path.endswith('.sln'):
        for n, line in enumerate(text.splitlines(), 1):
            m = re.match(r'\s*Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+)"', line)
            if m and m[1].lower().endswith(('.csproj', '.vbproj', '.fsproj')):
                target = literal_target(path, m[1])
                if target:
                    result.append(('solution_member', target, {'path':path, 'line':n, 'text':line.strip()}, None))
        return result, None, unresolved
    xml = ET.fromstring(text)
    tags = lambda name: [e for e in xml.iter() if e.tag.split('}')[-1] == name]
    project = None
    if path.endswith('.csproj'):
        project = dict(path=path, frameworks=[e.text for n in ['TargetFramework', 'TargetFrameworks', 'TargetFrameworkVersion'] for e in tags(n)],
                       project_references=[e.attrib for e in tags('ProjectReference')],
                       package_references=[e.attrib for e in tags('PackageReference')],
                       linked_source_count=sum('..' in e.get('Include', '') for e in tags('Compile')))
    # Preserve offsets while removing comments; a commented declaration is not evidence.
    clean = re.sub(r'<!--.*?-->', lambda m: ''.join('\n' if c == '\n' else ' ' for c in m[0]), text, flags=re.S)
    expected = {(e.tag.split('}')[-1], e.get('Include') or e.get('Project')): e
                for e in xml.iter() if e.tag.split('}')[-1] in {'Compile','Import','ProjectReference','PackageReference'}}
    pattern = r'<(?:\w+:)?(Compile|Import|ProjectReference|PackageReference)\b[^>]*>'
    for m in re.finditer(pattern, clean, re.S):
        attrs = dict((k, html.unescape(v)) for k, _, v in re.findall(r'([\w:]+)\s*=\s*([\'"])(.*?)\2', m[0], re.S))
        tag, value = m[1], attrs.get('Include') or attrs.get('Project')
        node = expected.get((tag, value))
        if attrs.get('Update') and tag in ('PackageReference','ProjectReference','Compile'):
            unresolved.append('msbuild-update-item')
        if node is None or not value:
            continue
        ev = {'path':path, 'line':text.count('\n', 0, m.start())+1, 'text':text[m.start():m.end()].strip()}
        if tag == 'PackageReference':
            version = node.get('Version') or next((c.text for c in node if c.tag.split('}')[-1]=='Version'),None)
            # A nested Version is a separate witness; do not invent one from the start tag.
            if version and node.get('Version') is None:
                unresolved.append('nested-package-version'); continue
            ext = {'id':'nuget:'+value+('@'+version if version else ''), 'kind':'declared-nuget-package',
                   'name':value, 'declared_version':version}
            result.append(('package_reference', ext['id'], ev, ext)); continue
        target = literal_target(path, value)
        if target is None:
            unresolved.append('nonliteral-'+tag.lower()); continue
        kind = {'Compile':'includes_source', 'Import':'imports_project', 'ProjectReference':'project_reference'}[tag]
        result.append((kind, target, ev, None))
    return result, project, unresolved


def validate(m):
    def require(condition, reason):
        if not condition: raise ValueError('Graph validation: '+reason)
    require(m['schema_version'] == '2.0.0', 'unsupported schema')
    inventory, external = m['file_inventory'], m.get('external_nodes', {})
    ids = set()
    for r in m['relationships']:
        require(r['id'] not in ids, 'duplicate relation ID'); ids.add(r['id'])
        require(r['status'] in ('verified', 'stale'), 'invalid relation status')
        if r['status'] == 'verified':
            require(inventory.get(r['from']) == r['source_blob'], 'source pin mismatch')
            require(r['to'] in external or inventory.get(r['to']) == r['target_blob'], 'target pin mismatch')
            require(r['evidence']['blob'] == r['source_blob'], 'evidence pin mismatch')
            require(r['evidence']['text'] and r['evidence']['line'] > 0, 'missing source witness')
            require(r.get('last_verified_commit'), 'missing verification commit')
        else:
            require(r.get('stale_reason'), 'missing stale reason')
    for module in m['modules']:
        require(set(module['relation_ids']) <= ids, 'module contains unknown edge IDs')


def digest(m):
    # Detect edits that try to carry a verified label without a validation pass.
    payload = {k:v for k,v in m.items() if k != 'validation_digest'}
    return hashlib.sha256(json.dumps(payload,sort_keys=True,ensure_ascii=False).encode()).hexdigest()


def update(git, old, target):
    m = copy.deepcopy(old)
    if m.get('base_branch') != BRANCH:
        raise ValueError('Graph baseline belongs to another development branch')
    base = git.commit(m['base_commit']); target = git.commit(target)
    if subprocess.run(['git','merge-base','--is-ancestor',base,target], cwd=git.root,
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        raise ValueError('Baseline is missing from target ancestry; fetch history or explicitly rebaseline. No full scan was attempted.')
    diff = git.changes(base, target)
    changes = [c for c in diff if not (generated(c['old_path']) and generated(c['path']))]
    bootstrap = m['schema_version'] == '1.0.0'
    if not bootstrap and m['schema_version'] != '2.0.0':
        raise ValueError('Unsupported graph schema')
    if not bootstrap:
        validate(m)
        if m.get('validation_digest') != digest(m):
            raise ValueError('Graph metadata changed since validation; a reviewed rebaseline is required')
    report = dict(target_commit=target, previous_commit=base, changes=changes, files_changed=len(changes),
                  files_examined=0, content_files_read=0, relationships_checked=0, relationships_changed=0,
                  new_verified_relations=0, dependency_declarations_changed=0, bootstrap=bootstrap, dry_run=False)
    if not bootstrap and not changes:
        report.update(validity=m['validity']['status'], noop=True, analyzed_commit=m['base_commit'],
                      stale_relations=sum(r['status']=='stale' for r in m['relationships']), unchanged_files_not_read=len(m['file_inventory']))
        return m, report
    inventory = m['file_inventory']
    prior_inventory = dict(inventory)
    previous_relations = {r['id']:copy.deepcopy(r) for r in m['relationships']}
    pending = m.setdefault('pending_review_files', {})
    external = m.setdefault('external_nodes', {})
    diagnostics = m.setdefault('unresolved_build_items', {})
    if bootstrap:
        review = m.get('bootstrap_manual_review', {})
        if m['validity']['status'] != 'verified-for-base-commit' and not (
                review.get('commit') == base and review.get('previous_status') == 'verified-for-base-commit'):
            raise ValueError('An unvalidated seed cannot be automatically trusted')
        texts = {}
        report['relationships_checked'] += len(m['relationships'])
        for r in m['relationships']:
            r.update(origin='manual', status='verified', source_blob=inventory[r['from']],
                     target_blob=inventory[r['to']], last_verified_commit=base)
            ev = r['evidence']; ev.update(commit=base, blob=r['source_blob'])
            if r['from'] not in texts:
                texts[r['from']] = git.text(r['source_blob'], r['from']).splitlines()
            if not (0 < ev['line'] <= len(texts[r['from']])) or texts[r['from']][ev['line']-1].strip() != ev['text']:
                r.update(status='stale', stale_reason='seed-witness-mismatch')
        m['schema_version'] = '2.0.0'
    renames, affected, declarations, dependency_scopes = {}, set(), {}, []
    project_index = {p['path']:p for p in m.get('projects', [])}
    for c in changes:
        path, oldpath = c['path'], c['old_path']
        if path == oldpath and c['sha'] == c['old_sha'] and c['mode'] in ('100644','100755') and c['old_mode'] in ('100644','100755'):
            continue
        affected.update([path,oldpath])
        if c['old_sha'].strip('0') and not generated(oldpath):
            if prior_inventory.get(oldpath) != c['old_sha']:
                raise ValueError('Inventory/base mismatch: '+oldpath)
        if c['status'].startswith('R'):
            renames[oldpath] = path
        if c['status'].startswith(('R','D')) or c['mode'] == '160000':
            inventory.pop(oldpath, None); project_index.pop(oldpath, None)
        if c['status'] != 'D' and c['mode'] != '160000' and not generated(path):
            inventory[path] = c['sha']
        if generated(path):
            continue
        if c['status'] == 'D':
            diagnostics.pop(path, None)
            if source_file(path): pending[path] = 'source-deleted'
            if path.endswith(('.csproj','.props','.targets')):
                dependency_scopes.append(posixpath.dirname(path)+'/' if path.endswith('.csproj') else '')
            continue
        if c['mode'] not in ('100644','100755'):
            pending[path] = 'nonregular-git-entry'; continue
        if c['sha'] == c['old_sha'] and path == oldpath:
            continue
        if not source_file(path):
            continue
        text = git.text(c['sha'], path)
        if path.endswith(('.csproj','.props','.targets','.sln')):
            # Properties, conditions and framework changes can alter binding even
            # when literal dependency names do not change. Never carry runtime
            # edges through such a change on the strength of old text alone.
            if path.endswith(('.csproj','.props','.targets')):
                dependency_scopes.append(posixpath.dirname(path)+'/' if path.endswith('.csproj') else '')
                if oldpath != path:
                    dependency_scopes.append(posixpath.dirname(oldpath)+'/' if oldpath.endswith('.csproj') else '')
            try:
                items, project, unresolved = build_items(path, text)
                declarations[path] = items
                if project: project_index[path] = project
                if unresolved:
                    diagnostics[path] = sorted(set(unresolved)); pending[path] = 'unresolved-build-declaration'
                else:
                    diagnostics.pop(path, None); pending.pop(path, None)
                if c['old_sha'].strip('0') and oldpath.endswith(('.csproj','.props','.targets','.sln')):
                    before, _, _ = build_items(oldpath, git.text(c['old_sha'], oldpath))
                else: before = []
                keys = lambda xs: {(x[0],x[1]) for x in xs}
                report['dependency_declarations_changed'] += len(keys(before)^keys(items))
            except (ET.ParseError, ValueError, UnicodeError):
                diagnostics[path] = ['malformed-build-document']; pending[path] = 'malformed-build-document'
                if path in project_index: project_index[path]['status'] = 'stale'
                dependency_scopes.append('')
        else:
            pending[path] = 'runtime-or-ui-source-needs-review'
    # Follow literal project references in metadata, including transitive dependents.
    scopes = set(dependency_scopes)
    while True:
        expanded = set(scopes)
        for p in project_index.values():
            for item in p.get('project_references', []):
                target_path = literal_target(p['path'], item.get('Include'))
                if target_path and any(target_path.startswith(scope) for scope in scopes):
                    expanded.add(posixpath.dirname(p['path'])+'/')
        if expanded == scopes: break
        scopes = expanded
    # Invalidate metadata only, using reverse endpoints; do not re-read unchanged sources.
    for r in m['relationships']:
        impacted = bool({r['from'],r['to'],r['evidence']['path']} & affected)
        dependent = r.get('origin') == 'manual' and r['kind'] not in BUILD_KINDS and any(
            r['from'].startswith(scope) or r['to'].startswith(scope) for scope in scopes)
        if impacted or dependent:
            report['relationships_checked'] += 1
            r.update(status='stale', stale_reason='build-dependency-changed' if dependent else 'endpoint-or-evidence-changed')
            r['from'] = renames.get(r['from'], r['from']); r['to'] = renames.get(r['to'], r['to'])
            if r['from'] not in inventory or (r['to'] not in inventory and r['to'] not in external):
                r['stale_reason'] = 'endpoint-deleted'
    for path, items in declarations.items():
        for kind, target_path, ev, ext in items:
            if not ext and target_path not in inventory:
                pending[path] = 'missing-build-target'; diagnostics.setdefault(path, []).append('missing-build-target'); continue
            existing = next((r for r in m['relationships'] if (r['from'],r['to'],r['kind']) == (path,target_path,kind)), None)
            if existing is None:
                rid = 'B'+hashlib.sha256((kind+'\0'+path+'\0'+target_path).encode()).hexdigest()[:16]
                existing = dict(id=rid, module='tests' if path.startswith('tests/') else 'build', kind=kind,
                                **{'from':path,'to':target_path}, source_symbol=None,target_symbol=None)
                m['relationships'].append(existing); report['new_verified_relations'] += 1
            if ext: external[ext['id']] = ext
            ev.update(blob=inventory[path],commit=target)
            existing.update(status='verified', origin='literal-build-declaration', evidence=ev,
                            source_blob=inventory[path],target_blob=inventory.get(target_path),last_verified_commit=target,
                            verification='literal-declaration-only',note='Literal build declaration; MSBuild conditions and execution are not evaluated.')
            existing.pop('stale_reason', None)
    for module in m['modules']:
        for key in ['files','entrypoints']:
            module[key] = [renames.get(p,p) for p in module[key]]
        module['relation_ids'] = [r['id'] for r in m['relationships'] if r['module']==module['id']]
        module['status'] = 'needs-review' if any(r['status']=='stale' and r['id'] in module['relation_ids'] for r in m['relationships']) else 'primary-path-mapped'
    m['projects'] = sorted(project_index.values(),key=lambda p:p['path'])
    m.update(base_commit=target,base_tree=git.run('rev-parse',target+'^{tree}').decode().strip(),
             generated_at=dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds').replace('+00:00','Z'))
    m['validity'].update(status='pending-validation', assurance='incremental-blob-pins-and-literal-build-declarations',
                         analysis_complete=False,runtime_validated=False,
                         inventory_scope='Tracked blobs excluding docs/codegraph/**; maintained from raw Git diff, not a whole-tree content scan.')
    counts=m['counts'];counts.update(inventory_files=len(inventory),csharp_files=sum(p.endswith('.cs') for p in inventory),
        xaml_files=sum(p.endswith('.xaml') for p in inventory),solutions=sum(p.endswith('.sln') for p in inventory),
        projects=len(project_index),test_projects=sum(p.startswith('tests/') for p in project_index),
        python_run_py_files=sum(p.startswith('tests/') and p.endswith('/run.py') for p in inventory),
        verified_relations=sum(r['status']=='verified' for r in m['relationships']),
        stale_relations=sum(r['status']=='stale' for r in m['relationships']),total_relations=len(m['relationships']),
        relation_evidence_files=len({r['evidence']['path'] for r in m['relationships']}))
    validate(m)  # The valid label is assigned only after structural/pin checks pass.
    m['validity']['status'] = 'needs-review' if counts['stale_relations'] or pending or diagnostics else 'validated-for-analyzed-commit'
    m['validity']['validated_at'] = m['generated_at']
    counts['primary_paths_mapped'] = sum(x['status']=='primary-path-mapped' for x in m['modules'])
    m['validation_digest'] = digest(m)
    report.update(validity=m['validity']['status'],noop=False,analyzed_commit=target,
                  files_examined=len({c['path'] for c in changes if source_file(c['path'])}),
                  content_files_read=len(git.reads),blob_reads=len(git.blob_reads),
                  unchanged_files_not_read=len(set(inventory)-git.reads),stale_relations=counts['stale_relations'],
                  pending_review_files=sorted(pending),relationships_changed=sum(previous_relations.get(r['id'])!=r for r in m['relationships']))
    return m, report


def render(m, report):
    status = f"# Current Codegraph status\n\nAnalyzed commit: `{m['base_commit']}`. Validity: **{m['validity']['status']}**.\n\n"
    status += 'Only this status/manifest describe the current snapshot. Other narrative pages are manually reviewed baseline notes.\n\n'
    for key in ['files_changed','files_examined','content_files_read','relationships_changed','new_verified_relations','stale_relations']:
        status += f'- {key}: {report.get(key,0)}\n'
    status += '\nPending source review:\n\n'+ ('\n'.join('- `'+p+'`: '+reason for p,reason in m.get('pending_review_files',{}).items()) or 'None.')+'\n'
    deps = '# Current graph relationships\n\nSee [automation.md](automation.md) for validation limits and [status.md](status.md) for current status.\n\n'
    deps += '| ID | Status | Kind | From | To | Last verified source |\n| --- | --- | --- | --- | --- | --- |\n'
    for r in m['relationships']:
        ev=r['evidence'];commit=ev.get('commit',m['base_commit'])
        url=f"https://github.com/{m['repository']}/blob/{commit}/{ev['path']}#L{ev['line']}"
        clean=lambda s:s.replace('|','\\|').replace('\n',' ')
        deps+=f"| `{r['id']}` | {r['status']} | {r['kind']} | `{clean(r['from'])}` | `{clean(r['to'])}` | [witness]({url}) |\n"
    return dict(zip(OUTPUTS,[json.dumps(m,indent=2,ensure_ascii=False)+'\n',deps,status]))


def summary(report):
    return '\n'.join(['### Codegraph incremental run','',f"- Target: `{report.get('target_commit','unknown')}`",f"- Validity: **{report.get('validity','failed')}**"]+
       [f'- {key}: {report.get(key,0)}' for key in ['files_changed','files_examined','content_files_read','relationships_changed','new_verified_relations','stale_relations','ui_files_read','ui_xaml_reparsed','ui_components','ui_pending_review']])+ '\n'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo',default='.')
    parser.add_argument('--target',default='HEAD')
    parser.add_argument('--dry-run',action='store_true')
    parser.add_argument('--report',required=True)
    parser.add_argument('--summary')
    args=parser.parse_args(); git=Git(args.repo)
    report={'validity':'failed','dry_run':args.dry_run}
    try:
        old=json.loads((git.root/OUTPUTS[0]).read_text())
        if os.environ.get('GITHUB_REPOSITORY') and old['repository'] != os.environ['GITHUB_REPOSITORY']:
            raise ValueError('Graph repository does not match the workflow repository')
        m,report=update(git,old,args.target);report['dry_run']=args.dry_run
        ui_path=git.root/OUTPUTS[3]
        prior_ui=json.loads(ui_path.read_text()) if ui_path.exists() else None
        target=git.commit(args.target)
        ui_graph,ui_report=ui.synchronize(git,prior_ui,m['file_inventory'],target)
        report.update(ui_report)
        # UI output is part of the same reviewed/published snapshot as the codegraph.
        ui_modified=(prior_ui != ui_graph)
        if ui_modified:
            report['noop']=False
        if not report['noop'] and not args.dry_run:
            outputs=render(m,report)
            outputs[OUTPUTS[3]]=ui.render(ui_graph)
            for path,text in outputs.items():
                dest=git.root/path;dest.parent.mkdir(parents=True,exist_ok=True)
                tmp=dest.with_suffix(dest.suffix+'.tmp');tmp.write_text(text);os.replace(tmp,dest)
        print(summary(report))
    except (ValueError,AssertionError,KeyError,IndexError,UnicodeError,OSError,subprocess.CalledProcessError) as error:
        report.update(validity='failed',error=str(error));print(summary(report),file=sys.stderr)
        return 1
    finally:
        dest=Path(args.report);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_text(json.dumps(report,indent=2)+'\n')
        if args.summary:
            with open(args.summary,'a') as f:f.write(summary(report))
    return 0


if __name__=='__main__':
    sys.exit(main())
