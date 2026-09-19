"""Extracts the 128 GM program names (en/ru) exactly as the game labels MIDI channels."""
import json
import os
import re
import sys

REPO = sys.argv[1] if len(sys.argv) > 1 else 'repo'
ENUM = os.path.join(REPO, 'Content.Client', 'Instruments', 'MidiParser', 'MidiInstrument.cs')
PREFIX = 'instruments-component-menu-midi-channel-'

pascal_to_kebab = re.compile(r'(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])')


def kebab(name):
    return pascal_to_kebab.sub(r'-\1', name).strip().lower()


programs = {}
with open(ENUM, encoding='utf-8-sig') as f:
    for line in f:
        m = re.match(r'\s*([A-Za-z0-9_]+)\s*=\s*(\d+)\s*,', line)
        if m:
            programs[int(m.group(2))] = m.group(1)


def load_locale(lang):
    path = os.path.join(REPO, 'Resources', 'Locale', lang, 'instruments', 'instruments-component.ftl')
    out = {}
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            if line.startswith(PREFIX):
                key, _, value = line.partition('=')
                out[key.strip()[len(PREFIX):]] = value.strip()
    return out


en = load_locale('en-US')
ru = load_locale('ru-RU')

out = []
for i in range(128):
    name = programs.get(i)
    key = kebab(name) if name else None
    out.append({
        'program': i,
        'key': key,
        'en': en.get(key) or name or f'Program {i}',
        'ru': ru.get(key) or en.get(key) or name or f'Программа {i}',
    })

missing = [p['program'] for p in out if p['key'] and p['key'] not in ru]
dest = sys.argv[2] if len(sys.argv) > 2 else 'tools/programs.json'
with open(dest, 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
print(f'{len(out)} programs -> {dest}; missing ru: {missing}')
