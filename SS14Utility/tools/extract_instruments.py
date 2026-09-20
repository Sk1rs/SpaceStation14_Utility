import json
import os
import re
import sys

import yaml


def unknown(loader, suffix, node):
    if isinstance(node, yaml.ScalarNode):
        return loader.construct_scalar(node)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node)
    return loader.construct_mapping(node)


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor('!', unknown)
Loader.add_multi_constructor('tag:', unknown)

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'repo')
PROTO_DIR = os.path.join(REPO, 'Resources', 'Prototypes')
LOCALE_DIR = os.path.join(REPO, 'Resources', 'Locale', 'ru-RU')

protos = {}

for root, _dirs, files in os.walk(PROTO_DIR):
    for fn in files:
        if not fn.endswith('.yml'):
            continue
        path = os.path.join(root, fn)
        try:
            with open(path, encoding='utf-8-sig') as f:
                docs = list(yaml.load_all(f, Loader=Loader))
        except Exception as e:
            print(f'skip {path}: {e}', file=sys.stderr)
            continue
        for doc in docs:
            if not isinstance(doc, list):
                continue
            for item in doc:
                if not isinstance(item, dict) or item.get('type') != 'entity':
                    continue
                pid = item.get('id')
                if not pid:
                    continue
                comps = {}
                for comp in item.get('components') or []:
                    if isinstance(comp, dict) and 'type' in comp:
                        comps[comp['type']] = comp
                parent = item.get('parent')
                if parent is None:
                    parents = []
                elif isinstance(parent, list):
                    parents = parent
                else:
                    parents = [parent]
                protos[pid] = {
                    'id': pid,
                    'parents': parents,
                    'abstract': bool(item.get('abstract', False)),
                    'name': item.get('name'),
                    'desc': item.get('description'),
                    'suffix': item.get('suffix'),
                    'comps': comps,
                    'file': os.path.relpath(path, REPO).replace(os.sep, '/'),
                }


def lookup(pid, getter, seen=None):
    seen = seen or set()
    if pid in seen or pid not in protos:
        return None
    seen.add(pid)
    p = protos[pid]
    val = getter(p)
    if val is not None:
        return val
    for parent in reversed(p['parents']):
        val = lookup(parent, getter, seen)
        if val is not None:
            return val
    return None


def has_comp(pid, comp, seen=None):
    seen = seen or set()
    if pid in seen or pid not in protos:
        return False
    seen.add(pid)
    p = protos[pid]
    if comp in p['comps']:
        return True
    return any(has_comp(par, comp, seen) for par in p['parents'])


def comp_field(pid, comp, field):
    return lookup(pid, lambda p: p['comps'].get(comp, {}).get(field))


ru_names = {}
ent_re = re.compile(r'^ent-([A-Za-z0-9_]+)\s*=\s*(.+)$')
for root, _dirs, files in os.walk(LOCALE_DIR):
    for fn in files:
        if not fn.endswith('.ftl'):
            continue
        with open(os.path.join(root, fn), encoding='utf-8-sig') as f:
            for line in f:
                m = ent_re.match(line.strip())
                if m:
                    ru_names.setdefault(m.group(1), m.group(2).strip())

out = []
for pid, p in protos.items():
    if p['abstract'] or not has_comp(pid, 'Instrument'):
        continue
    styles = None
    swap = lookup(pid, lambda x: x['comps'].get('SwappableInstrument', {}).get('instrumentList'))
    if isinstance(swap, dict):
        styles = []
        for style_name, value in swap.items():
            if isinstance(value, dict) and value:
                prog, bank = next(iter(value.items()))
                styles.append({'name': str(style_name), 'program': int(prog), 'bank': int(bank)})
            elif isinstance(value, list) and len(value) >= 2:
                styles.append({'name': str(style_name), 'program': int(value[0]), 'bank': int(value[1])})
            elif isinstance(value, list) and len(value) == 1:
                styles.append({'name': str(style_name), 'program': int(value[0]), 'bank': 0})
    out.append({
        'id': pid,
        'name': lookup(pid, lambda x: x['name']) or pid,
        'nameRu': ru_names.get(pid),
        'program': int(comp_field(pid, 'Instrument', 'program') or 0),
        'bank': int(comp_field(pid, 'Instrument', 'bank') or 0),
        'allowPercussion': bool(comp_field(pid, 'Instrument', 'allowPercussion') or False),
        'allowProgramChange': bool(comp_field(pid, 'Instrument', 'allowProgramChange') or False),
        'respectMidiLimits': comp_field(pid, 'Instrument', 'respectMidiLimits') is not False,
        'handheld': bool(comp_field(pid, 'Instrument', 'handheld') or False),
        'styles': styles,
        'file': p['file'],
    })

out.sort(key=lambda x: (x['program'], x['id']))
dest = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), 'instruments.json')
with open(dest, 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
print(f'{len(out)} instruments -> {dest}')
