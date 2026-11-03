#!/usr/bin/env python3
"""Publish only generated graph outputs, on the single authorized development ref."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

from update import BRANCH, OUTPUTS


def publish(root, expected, ref):
    if ref != 'refs/heads/' + BRANCH:
        raise ValueError('Codegraph publication is allowed only on ' + BRANCH)
    root = Path(root)
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=root, stderr=subprocess.PIPE).decode().strip()
    if git('rev-parse','HEAD') != expected:
        raise ValueError('Checkout changed since analysis')
    git('fetch','--no-tags','origin','refs/heads/'+BRANCH)
    if git('rev-parse','FETCH_HEAD') != expected:
        return {'publication_status':'superseded','published':False}
    staged = set(git('diff','--cached','--name-only').splitlines())
    if staged - set(OUTPUTS):
        raise ValueError('Refusing staged files outside generated graph outputs')
    # Never stage the application, workflow, tool or test sources.
    for path in OUTPUTS:
        if (root/path).exists(): git('add','--',path)
    if not git('diff','--cached','--name-only'):
        return {'publication_status':'no-change','published':False}
    git('-c','user.name=github-actions[bot]','-c','user.email=41898282+github-actions[bot]@users.noreply.github.com',
        'commit','-m','docs(codegraph): sync '+expected)
    # Non-force push is the final lease: a concurrent developer push is never overwritten.
    git('push','origin','HEAD:refs/heads/'+BRANCH)
    return {'publication_status':'pushed','published':True,'published_commit':git('rev-parse','HEAD')}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--repo',default='.');p.add_argument('--report',required=True)
    args=p.parse_args();report_path=Path(args.report);report=json.loads(report_path.read_text())
    try:
        if report['dry_run'] or report['validity']=='failed':
            raise ValueError('A dry run or failed analysis cannot be published')
        if report['target_commit'] != os.environ['GITHUB_SHA']:
            raise ValueError('Report does not describe the push commit')
        report.update(publish(args.repo,os.environ['GITHUB_SHA'],os.environ['GITHUB_REF']))
        return 0
    except (KeyError,ValueError,subprocess.CalledProcessError) as error:
        report.update(publication_status='failed',publication_error=str(error),published=False)
        return 1
    finally:
        report_path.write_text(json.dumps(report,indent=2)+'\n')
        print('Codegraph publication: '+report.get('publication_status','failed'))
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with open(os.environ['GITHUB_STEP_SUMMARY'],'a') as out:
                out.write('\nPublication: **'+report.get('publication_status','failed')+'**\n')


if __name__=='__main__':
    sys.exit(main())
