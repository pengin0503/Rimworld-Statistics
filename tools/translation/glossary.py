#!/usr/bin/env python3
"""Look up official RimWorld terms in the glossaries built by build_vanilla_glossary.py.

Usage:
  python3 tools/translation/glossary.py search LANG REGEX [MAX]   # English text matches REGEX (case-insensitive)
  python3 tools/translation/glossary.py core LANG                 # official forms of the terms this mod uses most
  python3 tools/translation/glossary.py count LANG EN_REGEX CAND1 CAND2 ...
      # among strings whose English matches EN_REGEX, how many translations contain each candidate
Examples:
  glossary.py search German '^raid$'
  glossary.py count Japanese '\\bfactions?\\b' 派閥 勢力
"""
import collections
import re
import sys
from pathlib import Path

CACHE = Path(__file__).resolve().parent / '.cache'
CORE = [
    'colonist', 'colonists', 'colony', 'wealth', 'raid', 'faction', 'goodwill', 'ally', 'hostile', 'prisoner', 'slave',
    'caravan', 'settlement', 'quest', 'storyteller', 'scenario', 'difficulty', 'biome', 'development mode',
    'mental break', 'mental breaks', 'inspiration', 'psylink', 'mechanitor', 'bandwidth', 'gravship', 'ideoligion',
    'ritual', 'honor', 'permits', 'mechanoid', 'mech gestator', 'toxic wastepack', 'wastepack atomizer', 'pollution',
    'growing zone', 'hydroponics basin', 'cryptosleep casket', 'transport pod', 'drop pod', 'infestation',
    'manhunter pack', 'mech cluster', 'siege', 'solar flare', 'toxic fallout', 'cold snap', 'heat wave',
    'volcanic winter', 'eclipse', 'blight', 'short circuit', 'meteorite impact', 'ambush', 'medicine',
    'food poisoning', 'withdrawal', 'scar', 'social fight', 'bond', 'tame', 'slaughter', 'hunt', 'legendary',
    'masterwork', 'awful', 'quality', 'steel', 'component', 'advanced component', 'silver', 'turret', 'battery',
    'persona weapons', 'passion', 'skill', 'downed', 'incapacitated', 'bills', 'royal ascent', 'cerebrex core',
    'ancient danger', 'archonexus', 'outpost', 'save', 'load', 'mood', 'free colonists', 'tend quality',
    'growth moment', 'fishing', 'filth', 'hauling', 'extinguish', 'research', 'trader', 'visitor', 'guest',
]


def rows(lang):
    path = CACHE / f'gloss_{lang}.tsv'
    if not path.exists():
        sys.exit(f'{path} not found; run build_vanilla_glossary.py first')
    with open(path, encoding='utf-8') as f:
        for line in f:
            parts = line.rstrip('\n').split('\t')
            if len(parts) == 4:
                yield parts


def search(lang, pattern, limit=15):
    rx = re.compile(pattern, re.I)
    seen = set()
    for kind, key, en, xx in rows(lang):
        if rx.search(en) and (en, xx) not in seen:
            seen.add((en, xx))
            print(f'{key[:40]:40} | {en[:90]} | {xx[:120]}')
            if len(seen) >= limit:
                break


def core(lang):
    hits = collections.defaultdict(collections.Counter)
    for kind, key, en, xx in rows(lang):
        e = en.strip().lower()
        if e in CORE and xx.strip().lower() != e:
            hits[e][xx.strip()] += 1
    for term in CORE:
        if term in hits:
            print(f'{term}: ' + ' | '.join(f'{v} ({n})' for v, n in hits[term].most_common(3)))


def count(lang, pattern, candidates):
    rx = re.compile(pattern, re.I)
    tally = {c: 0 for c in candidates}
    n = 0
    for kind, key, en, xx in rows(lang):
        if rx.search(en):
            n += 1
            for c in candidates:
                if c.lower() in xx.lower():
                    tally[c] += 1
    print(f'{lang} /{pattern}/ matched {n}:', tally)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    cmd, lang = sys.argv[1], sys.argv[2]
    if cmd == 'search' and len(sys.argv) >= 4:
        search(lang, sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 15)
    elif cmd == 'core':
        core(lang)
    elif cmd == 'count' and len(sys.argv) >= 5:
        count(lang, sys.argv[3], sys.argv[4:])
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
