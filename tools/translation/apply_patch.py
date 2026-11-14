#!/usr/bin/env python3
"""Apply a {key: new_text} JSON patch to one language's Keyed XML.

Only the inner text of the named elements changes; every other byte of the file is kept.
Nothing is written if a key is unknown or duplicated, a value is empty, the result does not
parse, or the {n} placeholders stop matching English.

Usage: python3 tools/translation/apply_patch.py LANG patch.json [--dry]
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path
from xml.sax.saxutils import escape

LANGUAGES = Path(__file__).resolve().parents[2] / 'Languages'
PLACEHOLDER = re.compile(r'\{\d+\}')


def path(lang):
    return LANGUAGES / lang / 'Keyed' / 'RimworldStatistics.xml'


def load(lang):
    return {el.tag: el.text or '' for el in ET.parse(path(lang)).getroot()}


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    lang, patch_file = sys.argv[1], sys.argv[2]
    dry = '--dry' in sys.argv
    patch = json.load(open(patch_file, encoding='utf-8'))
    text = path(lang).read_text(encoding='utf-8')
    english = load('English') if lang != 'English' else None
    old = load(lang)
    changed = 0
    for key, new in patch.items():
        if key not in old:
            sys.exit(f'{lang}: unknown key {key}')
        # A leading space is allowed (English marks such as " (dev mode)"); anything else must be trimmed.
        if not new.strip() or (new != new.strip() and not new.startswith(' ')):
            sys.exit(f'{lang}/{key}: empty or badly trimmed value {new!r}')
        ref = english[key] if english is not None else old[key]
        if Counter(PLACEHOLDER.findall(new)) != Counter(PLACEHOLDER.findall(ref)):
            sys.exit(f'{lang}/{key}: placeholders differ from English: {new!r} vs {ref!r}')
        pattern = re.compile(r'(<' + re.escape(key) + r'>)(.*?)(</' + re.escape(key) + r'>)', re.S)
        if len(pattern.findall(text)) != 1:
            sys.exit(f'{lang}/{key}: expected exactly one element')
        if old[key] == new:
            continue
        text = pattern.sub(lambda m: m.group(1) + escape(new) + m.group(3), text, count=1)
        changed += 1
    after = {el.tag: el.text or '' for el in ET.fromstring(text.encode('utf-8'))}
    if set(after) != set(old):
        sys.exit(f'{lang}: key set changed')
    for key, new in patch.items():
        if after[key] != new:
            sys.exit(f'{lang}/{key}: round-trip mismatch')
    if not dry:
        path(lang).write_text(text, encoding='utf-8', newline='')
    print(f'{lang}: {changed} values changed ({len(patch)} in patch){" [dry run]" if dry else ""}')


if __name__ == '__main__':
    main()
