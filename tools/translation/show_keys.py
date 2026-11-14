#!/usr/bin/env python3
"""Show keys START..END (1-based, in English key order) with English and optional other languages.

Usage:
  python3 tools/translation/show_keys.py START END                 # English only
  python3 tools/translation/show_keys.py START END German,Japanese # English + listed languages
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

LANGUAGES = Path(__file__).resolve().parents[2] / 'Languages'


def load(lang):
    root = ET.parse(LANGUAGES / lang / 'Keyed' / 'RimworldStatistics.xml').getroot()
    return {el.tag: el.text for el in root}


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    a, b = int(sys.argv[1]), int(sys.argv[2])
    others = [l for l in sys.argv[3].split(',') if l] if len(sys.argv) > 3 else []
    en = load('English')
    loaded = [(l, load(l)) for l in others]
    for i, key in enumerate(list(en)[a - 1:b], start=a):
        if not loaded:
            print(f'{i} {key}: {en[key]}')
            continue
        print(f'{i} {key}\n  English: {en[key]}')
        for lang, values in loaded:
            print(f'  {lang}: {values[key]}')


if __name__ == '__main__':
    main()
