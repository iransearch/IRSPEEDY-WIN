#!/usr/bin/env python3
"""Incremental, conservative WPF UI declaration index. No runtime-edge inference."""
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET

XAML_NS = '{http://schemas.microsoft.com/winfx/2006/xaml}'
EVENTS = frozenset(('Click','Loaded','Unloaded','IsVisibleChanged','SelectionChanged',
    'TextChanged','Checked','Unchecked','MouseLeftButtonDown','MouseLeftButtonUp',
    'MouseEnter','MouseLeave','SizeChanged','KeyDown','KeyUp','PreviewKeyDown',
    'PreviewMouseDown','PreviewMouseLeftButtonDown','Closing','Closed','Activated',
    'Deactivated','PasswordChanged','GotFocus','LostFocus','ValueChanged',
    'ScrollChanged','Drop','DragOver','Navigated','RequestNavigate',
    'Selected','Unselected','CurrentCellChanged','Sorting','BeginningEdit'))
RESOURCE = re.compile(r'\{(StaticResource|DynamicResource)\s+([^}\s]+)\s*\}')
CSPARTIAL = r'\bpartial\s+class\s+{name}\b'

def is_xaml(path):
    return path.endswith('.xaml')

def xaml_codebehind(path):
    return path + '.cs'

def witness(lines, fragment):
    for i, line in enumerate(lines, 1):
        if fragment in line:
            return {'line':i, 'text':line.strip()}
    raise ValueError('missing UI source witness: '+fragment)

def parse_xaml(text, path, sha):
    root = ET.fromstring(text)
    cls = root.attrib.get(XAML_NS+'Class')
    lines = text.splitlines()
    result = {'path':path, 'blob_sha':sha, 'status':'verified-declarations',
              'x_class':cls, 'evidence':{}, 'names':[], 'events':[],
              'resource_references':[], 'codebehind':None}
    if cls:
        result['evidence']['x_class'] = witness(lines, 'x:Class="'+cls+'"')
    for el in root.iter():
        named = el.attrib.get(XAML_NS+'Name')
        if named:
            result['names'].append({'name':named, 'tag':el.tag.split('}')[-1],
                'evidence':witness(lines, 'x:Name="'+named+'"')})
        for attr, val in el.attrib.items():
            field = attr.split('}')[-1]
            if field in EVENTS and re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', val or ''):
                result['events'].append({'event':field, 'handler':val,
                    'status':'xaml-declaration-only', 'evidence':witness(lines,val)})
            for kind, key in RESOURCE.findall(val or ''):
                result['resource_references'].append({'kind':kind, 'key':key,
                    'status':'key-reference-only', 'evidence':witness(lines,kind+' '+key)})
    for field in ('names','events','resource_references'):
        # Coalesce duplicate occurrences while retaining one exact source witness.
        dedup = {}
        for item in result[field]:
            key = (item.get('name'),item.get('tag'),item.get('event'),
                   item.get('handler'),item.get('kind'),item.get('key'))
            dedup.setdefault(key,item)
        result[field] = list(dedup.values())
    return result

def check_codebehind(git, node, inventory, cache):
    path = xaml_codebehind(node['path'])
    cls = node.get('x_class')
    if not cls:
        node['codebehind']=None
        return
    if path not in inventory:
        node['codebehind']={'path':path,'status':'missing'}
        return
    sha=inventory[path]
    if path not in cache:
        cache[path]=git.text(sha,path)
    text=cache[path]
    name=cls.rsplit('.',1)[-1]
    namespace=cls.rsplit('.',1)[0] if '.' in cls else ''
    match=re.search(CSPARTIAL.format(name=re.escape(name)),text)
    nsok=not namespace or bool(re.search(r'\bnamespace\s+'+re.escape(namespace)+r'\b',text))
    status='verified-declaration' if match and nsok else 'needs-review'
    node['codebehind']={'path':path,'blob_sha':sha,'status':status}
    if match:
        line=text.count('\n',0,match.start())+1
        node['codebehind']['evidence']={'line':line,'text':text.splitlines()[line-1].strip()}
    if status != 'verified-declaration':
        node['status']='needs-review'

def synchronize(git, previous, inventory, target):
    """On first run parse XAML inventory once; subsequently read only changed UI blobs."""
    checkpoint=previous.get('analyzed_commit') if previous else None
    if checkpoint:
        if subprocess.run(['git','merge-base','--is-ancestor',checkpoint,target],
                          cwd=git.root,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode:
            raise ValueError('UI checkpoint not in target ancestry')
    nodes=dict(previous.get('components',{})) if previous else {}
    if checkpoint:
        changes=git.changes(checkpoint,target)
        old_paths={c['old_path'] for c in changes}
        paths={c['path'] for c in changes}
        for path in old_paths:
            if is_xaml(path) and path not in paths:
                nodes.pop(path,None)
        modified_xaml={p for p in paths if is_xaml(p)}
        modified_cs={p for p in paths if p.endswith('.xaml.cs')}
        # Rename/copy may preserve blobs, but their path references must change.
        for change in changes:
            if change['status'].startswith(('R','C')) and is_xaml(change['old_path']):
                nodes.pop(change['old_path'],None)
    else:
        modified_xaml={p for p in inventory if is_xaml(p)}
        modified_cs=set()
    reads_before=len(git.reads)
    cache={}
    for path in sorted(modified_xaml):
        if path not in inventory:
            nodes.pop(path,None)
            continue
        sha=inventory[path]
        try:
            node=parse_xaml(git.text(sha,path),path,sha)
            check_codebehind(git,node,inventory,cache)
            nodes[path]=node
        except (ET.ParseError,UnicodeError,ValueError) as exc:
            nodes[path]={'path':path,'blob_sha':sha,'status':'needs-review',
                         'reason':type(exc).__name__}
    for path,node in list(nodes.items()):
        if path not in inventory:
            del nodes[path]
            continue
        codebehind_path=xaml_codebehind(path)
        if codebehind_path in modified_cs and path not in modified_xaml:
            check_codebehind(git,node,inventory,cache)
        elif codebehind_path not in inventory and node.get('codebehind'):
            node['codebehind']['status']='missing'
            node['status']='needs-review'
    pending=sorted(p for p,n in nodes.items() if n['status']!='verified-declarations')
    result={'schema_version':'1.0.0', 'analyzed_commit':target,
            'scope':'XAML declarations, name/event/resource-key references and matching partial class; not runtime behavior',
            'components':dict(sorted(nodes.items())),
            'pending_review':pending,
            'counts':{'xaml_components':len(nodes),
                      'named_controls':sum(len(n.get('names',[])) for n in nodes.values()),
                      'event_declarations':sum(len(n.get('events',[])) for n in nodes.values()),
                      'resource_key_references':sum(len(n.get('resource_references',[])) for n in nodes.values()),
                      'pending_review':len(pending)}}
    report={'ui_files_read':len(git.reads)-reads_before,
            'ui_xaml_reparsed':len(modified_xaml & inventory.keys()),
            'ui_components':len(nodes),'ui_pending_review':len(pending)}
    return result,report

def render(data):
    return json.dumps(data,ensure_ascii=False,indent=2)+'\n'
