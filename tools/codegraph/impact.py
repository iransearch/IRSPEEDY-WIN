#!/usr/bin/env python3
"""Read-only, evidence-limited impact analysis for IRSPEEDY Codegraph.

Reports potential affected callers from *existing* graph edges. It never infers
new dependencies, upgrades stale evidence, suggests tests, or edits the graph.
"""
import argparse
from collections import defaultdict, deque
import json
from pathlib import Path
import subprocess
import sys

from update import BRANCH, Git, validate, digest

GRAPH_PATH = 'docs/codegraph/manifest.json'


def _analyze_checkpoint(git, manifest, target='HEAD', max_depth=4):
    if manifest.get('base_branch') != BRANCH:
        raise ValueError('Codegraph does not belong to the IRSPEEDY development branch')
    if manifest.get('schema_version') != '2.0.0':
        raise ValueError('Unsupported Codegraph manifest schema')
    validate(manifest)
    if manifest.get('validation_digest') != digest(manifest):
        raise ValueError('Codegraph validation_digest mismatch; revalidation required')
    graph_status = manifest.get('validity', {}).get('status')
    if graph_status not in ('needs-review', 'validated-for-analyzed-commit'):
        raise ValueError('Codegraph is not structurally validated: ' + str(graph_status))
    if not isinstance(max_depth, int) or not 1 <= max_depth <= 8:
        raise ValueError('max_depth must be 1..8')

    base = git.commit(manifest['base_commit'])
    target_sha = git.commit(target)
    if subprocess.run(['git', 'merge-base', '--is-ancestor', base, target_sha], cwd=git.root,
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode != 0:
        raise ValueError('Graph baseline is not an ancestor of target; fetch history or rebaseline')
    # An uncommitted checkout cannot be described solely as the requested commit.
    if git.run('status', '--porcelain', '--untracked-files=no').strip():
        raise ValueError('Tracked working tree is dirty; use a clean checkout')
    changes = [c for c in git.changes(base, target_sha)
               if not (c['path'].startswith('docs/codegraph/') and
                       c['old_path'].startswith('docs/codegraph/'))]
    changes.sort(key=lambda c: (c['path'], c['old_path']))
    changed_paths = {p for c in changes for p in (c['path'], c['old_path'])}
    rels = manifest['relationships']
    touched = []
    reverse = defaultdict(list)
    for edge in rels:
        src, dst = edge['from'], edge['to']
        if {src, dst, edge['evidence']['path']} & changed_paths:
            touched.append({
                'id': edge['id'], 'kind': edge['kind'], 'from': src, 'to': dst,
                'status_at_checkpoint': edge['status'], 'module': edge['module'],
                'reason': 'changed endpoint or evidence file; recheck before relying on it',
            })
        if src != dst:
            reverse[dst].append(edge)
    for edges in reverse.values():
        edges.sort(key=lambda e: e['id'])

    # The edge orientation is caller -> dependency. Reverse traversal discovers
    # *candidate dependents*; the data is not a complete runtime call graph.
    candidates = {}
    truncated = False
    for root in sorted(changed_paths):
        queue = deque([(root, (root,), (), False)])
        visited = set()
        while queue:
            node, visited_path, via, seen_stale = queue.popleft()
            depth = len(via)
            if depth >= max_depth:
                if any(e['from'] not in visited_path for e in reverse[node]):
                    truncated = True
                continue
            for edge in reverse[node]:
                parent = edge['from']
                if parent in visited_path:
                    continue
                stale = seen_stale or edge['status'] == 'stale'
                new_via = via + (edge['id'],)
                next_path = visited_path + (parent,)
                key = (parent, stale)
                if key not in visited:
                    visited.add(key)
                    queue.append((parent, next_path, new_via, stale))
                if parent in changed_paths:
                    continue  # Already a directly modified root.
                candidate = candidates.setdefault(parent, {
                    'path': parent, 'minimum_depth': len(new_via),
                    'via_relations': list(new_via), 'source_changed_path': root,
                    'has_stale_evidence': stale, 'confidence': 'potential-only',
                })
                if (len(new_via), stale, list(new_via)) < (candidate['minimum_depth'],
                                                          candidate['has_stale_evidence'],
                                                          candidate['via_relations']):
                    candidate.update(minimum_depth=len(new_via), via_relations=list(new_via),
                                     source_changed_path=root, has_stale_evidence=stale)
                else:
                    candidate['has_stale_evidence'] |= stale

    # Modules are a navigation hint, never proof that a missing edge has no effect.
    modules = []
    for module in manifest.get('modules', []):
        refs = set(module.get('files', [])) | set(module.get('entrypoints', []))
        ids = set(module.get('relation_ids', []))
        direct = sorted(refs & changed_paths)
        dependent = sorted(refs & candidates.keys())
        edges = sorted(e['id'] for e in touched if e['id'] in ids)
        if direct or dependent or edges:
            modules.append({'module': module['id'], 'directly_changed': direct,
                            'potential_dependents': dependent, 'touched_relations': edges})
    report = {
        'repository': manifest.get('repository'), 'branch': BRANCH,
        'baseline_commit': base, 'target_commit': target_sha,
        'graph_status_at_checkpoint': graph_status,
        'analysis_type': 'conservative-static-impact',
        'changed_files': [{'status': c['status'], 'path': c['path'],
                           'old_path': c['old_path'] if c['old_path'] != c['path'] else None}
                          for c in changes],
        'changed_file_count': len(changes),
        'directly_touched_relations': sorted(touched, key=lambda e: e['id']),
        'potential_dependents': sorted(candidates.values(), key=lambda x: (x['minimum_depth'], x['path'])),
        'affected_modules': sorted(modules, key=lambda x: x['module']),
        'unmapped_changed_paths': sorted(p for p in changed_paths
                                         if not any(p in (e['from'], e['to'], e['evidence']['path'])
                                                    for e in rels)),
        'preexisting_stale_relations': sum(e['status'] == 'stale' for e in rels),
        'preexisting_pending_review_files': len(manifest.get('pending_review_files', {})),
        'depth_limit_reached': truncated,
        'limitations': [
            'Candidates are not confirmed runtime effects or proof of a bug.',
            'Existing stale edges remain unverified; no status is upgraded.',
            'The graph is incomplete. Missing edges do not prove absence of impact.',
            'The analysis covers committed changes since the recorded graph source checkpoint only.',
        ],
    }
    return report


def analyze(git, manifest, target='HEAD', max_depth=4, pr_base=None):
    """Return original checkpoint impact plus advisory tests and optional PR-only scope."""
    report = _analyze_checkpoint(git, manifest, target, max_depth)
    modified = {p for item in report['changed_files']
                for p in (item['path'], item['old_path']) if p}
    impacted = modified | {x['path'] for x in report['potential_dependents']}
    suites = {}
    for rel in manifest['relationships']:
        source, target_path = rel['from'], rel['to']
        if not (source.startswith('tests/') and target_path in impacted and
                rel['kind'] in ('reads_source', 'includes_source')):
            continue
        parts = source.split('/')
        if len(parts) < 3:
            continue
        suite = '/'.join(parts[:2])
        item = suites.setdefault(suite, {
            'suite': suite, 'relation_ids': set(), 'linked_source_files': set(),
            'witness_files': set(), 'direct_source_change': False,
            'has_stale_evidence': False,
            'evidence_level': 'source-inclusion-or-read-only',
        })
        item['relation_ids'].add(rel['id'])
        item['linked_source_files'].add(target_path)
        item['witness_files'].add(source)
        item['direct_source_change'] |= target_path in modified
        item['has_stale_evidence'] |= rel['status'] == 'stale'
    related = []
    for item in suites.values():
        related.append({**item, **{
            key: sorted(item[key])
            for key in ('relation_ids', 'linked_source_files', 'witness_files')
        }})
    related.sort(key=lambda item: (not item['direct_source_change'], item['suite']))

    inventory = manifest.get('file_inventory', {})
    mapped = {path for edge in manifest['relationships']
              for path in (edge['from'], edge['to'], edge['evidence']['path'])}
    report.update(related_test_suites=related,
                  inventory_path_count=len(inventory),
                  mapped_inventory_paths=len(mapped & set(inventory)),
                  total_mapped_relations=len(manifest['relationships']),
                  pr_base_commit=None, pr_changed_files=None,
                  pr_change_count=None, pr_application_change_count=None)
    if pr_base is not None:
        sha = git.commit(pr_base)
        if subprocess.run(['git', 'merge-base', '--is-ancestor', sha,
                           report['target_commit']], cwd=git.root,
                          stdout=subprocess.DEVNULL,
                          stderr=subprocess.DEVNULL).returncode:
            raise ValueError('PR base must be an ancestor of the analyzed target')
        pr_changes = [x for x in git.changes(sha, report['target_commit'])
                      if not (x['path'].startswith('docs/codegraph/') and
                              x['old_path'].startswith('docs/codegraph/'))]
        pr_changes.sort(key=lambda x: (x['path'], x['old_path']))
        report.update(
            pr_base_commit=sha,
            pr_change_count=len(pr_changes),
            pr_application_change_count=sum(
                x['path'].startswith('IRSpeedyVPN/') or
                x['old_path'].startswith('IRSpeedyVPN/') for x in pr_changes),
            pr_changed_files=[{
                'status': x['status'], 'path': x['path'],
                'old_path': x['old_path'] if x['old_path'] != x['path'] else None,
            } for x in pr_changes],
        )
    report['limitations'].extend([
        'PR-only counts require an explicit --pr-base; checkpoint scope is cumulative.',
        'Test-harness file reads are not assertion/behavior coverage proof.',
        'Related test suites cannot replace or skip mandatory Regression Guard.',
    ])
    return report


def markdown(report):
    lines = ['# IRSPEEDY Codegraph — Impact Analysis', '',
             f"- Development branch: `{report['branch']}`",
             f"- Graph checkpoint: `{report['baseline_commit']}`",
             f"- Target commit: `{report['target_commit']}`",
             f"- Existing graph status: **{report['graph_status_at_checkpoint']}**",
             f"- Paths changed since graph checkpoint: **{report['changed_file_count']}**",
             f"- Directly touched relationships: **{len(report['directly_touched_relations'])}**",
             f"- Potential dependent files: **{len(report['potential_dependents'])}**",
             f"- Existing stale relationships: **{report['preexisting_stale_relations']}**",
             f"- Existing pending-review files: **{report['preexisting_pending_review_files']}**", '']
    lines.extend([
        f"- Related test suites (advisory): **{len(report['related_test_suites'])}**",
        f"- Inventory paths referenced in graph: **{report['mapped_inventory_paths']}/{report['inventory_path_count']}** (not coverage)",
        f"- Total mapped relationships: **{report['total_mapped_relations']}**",
    ])
    if report['pr_base_commit'] is None:
        lines.append('- PR-only counts: Not available (supply --pr-base)')
    else:
        lines.extend([
            f"- PR base: {report['pr_base_commit']}",
            f"- Paths changed in this PR: **{report['pr_change_count']}**",
            f"- Application paths changed in this PR: **{report['pr_application_change_count']}**",
        ])
    lines.append('')
    if report['changed_files']:
        lines.extend(['## Changes since graph checkpoint (may include previous PRs)', ''])
        for c in report['changed_files']:
            lines.append(f"- `{c['path']}` ({c['status']})" +
                         (f" from `{c['old_path']}`" if c['old_path'] else ''))
    else:
        lines.extend(['No tracked non-Codegraph changes since checkpoint.'])
    if report['pr_changed_files'] is not None:
        lines.extend(['', '## PR-only changes (explicit comparison base)', ''])
        if not report['pr_changed_files']:
            lines.append('None.')
        for item in report['pr_changed_files']:
            lines.append('- ' + item['path'] + ' (' + item['status'] + ')' +
                         (' from ' + item['old_path'] if item['old_path'] else ''))
    lines.extend(['', '## Related test suites (informational only)', ''])
    if not report['related_test_suites']:
        lines.append('No mapped test-suite link. This does not mean tests are unnecessary.')
    for item in report['related_test_suites']:
        relation = 'direct source change' if item['direct_source_change'] else 'possible downstream impact'
        stale = '; stale evidence: re-review required' if item['has_stale_evidence'] else ''
        lines.append('- ' + item['suite'] + ': ' + relation +
                     '; linked sources: ' + ', '.join(item['linked_source_files']) +
                     '; relations: ' + ', '.join(item['relation_ids']) + stale)
    lines.extend(['', '## Potential dependents (not runtime proof)', ''])
    if report['potential_dependents']:
        for item in report['potential_dependents']:
            suffix = ' — contains stale historical evidence' if item['has_stale_evidence'] else ''
            lines.append(f"- `{item['path']}`: depth {item['minimum_depth']}; graph edges "
                         f"{', '.join('`'+r+'`' for r in item['via_relations'])}{suffix}")
    else:
        lines.append('No candidates found in the mapped edges (coverage is incomplete).')
    if report['directly_touched_relations']:
        lines.extend(['', '## Directly touched graph relations', ''])
        for e in report['directly_touched_relations']:
            lines.append(f"- `{e['id']}` `{e['kind']}`: "
                         f"`{e['from']}` → `{e['to']}` "
                         f"(checkpoint: **{e['status_at_checkpoint']}**, recheck required)")
    lines.extend(['', '## Limits', '', *['- '+s for s in report['limitations']]])
    if report['depth_limit_reached']:
        lines.append('- Traversal depth limit reached; additional dependents may exist.')
    return '\n'.join(lines) + '\n'


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default='.')
    parser.add_argument('--target', default='HEAD')
    parser.add_argument('--max-depth', type=int, default=4)
    parser.add_argument('--pr-base', help='Explicit base commit for PR-only statistics')
    parser.add_argument('--report', help='Optional path for JSON report')
    parser.add_argument('--summary', help='Optional path for Markdown report')
    args = parser.parse_args(argv)
    try:
        root = Path(args.repo).resolve()
        manifest = json.loads((root / GRAPH_PATH).read_text(encoding='utf-8-sig'))
        result = analyze(Git(root), manifest, args.target, args.max_depth, args.pr_base)
        if args.report:
            dest = Path(args.report); dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
        summary = markdown(result)
        if args.summary:
            dest = Path(args.summary); dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(summary, encoding='utf-8')
        print(summary)
    except (KeyError, ValueError, OSError, subprocess.CalledProcessError, json.JSONDecodeError) as exc:
        print('IMPACT ANALYSIS FAILED: ' + str(exc), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
