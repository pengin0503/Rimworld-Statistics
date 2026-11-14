#!/usr/bin/env python3
"""Build English -> target-language glossaries from the official RimWorld translations.

The official translations are Ludeon's content, so they are NOT stored in this repository.
This script reads them from a local game install or from the private R2 mirror and writes
tools/translation/.cache/gloss_<Language>.tsv (git-ignored), one row per string:
    kind <TAB> key <TAB> English <TAB> translation
kind K = Keyed string joined with the English Keyed file by key,
kind D = DefInjected string whose file carries an "<!-- EN: ... -->" comment.

Usage:
  python3 tools/translation/build_vanilla_glossary.py --game-dir "/path/to/RimWorld"
  python3 tools/translation/build_vanilla_glossary.py --r2        # needs boto3 and the R2_* / AWS_* env vars
Options: --r2-prefix Game-Linux/  (default)   --languages German,French,...  (default: all repo languages)
"""
import argparse
import glob
import os
import re
import shutil
import sys
import tarfile
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
CACHE = HERE / '.cache'
REPO_LANGUAGES = HERE.parents[1] / 'Languages'
DLCS = ['Core', 'Royalty', 'Ideology', 'Biotech', 'Anomaly', 'Odyssey']
EN_COMMENT = re.compile(r'<!--\s*EN:\s*(.*?)\s*-->\s*\n\s*<([^>\s]+)>(.*?)</\2>', re.S)


def target_languages(arg):
    if arg:
        return arg.split(',')
    return sorted(p.name for p in REPO_LANGUAGES.iterdir() if p.is_dir() and p.name != 'English')


def fetch_r2(prefix, langs):
    import boto3  # only needed for --r2
    s3 = boto3.client('s3', endpoint_url=os.environ['R2_ENDPOINT'])
    bucket = os.environ['R2_BUCKET']
    raw = CACHE / 'raw'
    pages = s3.get_paginator('list_objects_v2')
    n = 0
    for dlc in DLCS:
        base = f'{prefix}Data/{dlc}/Languages/'
        for page in pages.paginate(Bucket=bucket, Prefix=base):
            for obj in page.get('Contents', []):
                rel = obj['Key'][len(base):]
                wanted = rel.startswith('English/Keyed/') or any(
                    rel.startswith(f'{l} (') or rel == f'{l}.tar' for l in langs)
                if not wanted:
                    continue
                out = raw / 'Data' / dlc / 'Languages' / rel
                out.parent.mkdir(parents=True, exist_ok=True)
                s3.download_file(bucket, obj['Key'], str(out))
                n += 1
    print(f'downloaded {n} files from R2')
    return raw


def keyed(folder):
    out = {}
    for p in glob.glob(os.path.join(folder, '**', '*.xml'), recursive=True):
        try:
            root = ET.parse(p).getroot()
        except ET.ParseError:
            continue
        for el in root:
            if isinstance(el.tag, str) and el.text:
                out[el.tag] = el.text.strip()
    return out


def language_dirs(game, lang, extracted):
    """Yield (dlc, folder) for one language, extracting .tar archives when needed."""
    for dlc in DLCS:
        langs_dir = game / 'Data' / dlc / 'Languages'
        if not langs_dir.is_dir():
            continue
        for entry in sorted(langs_dir.iterdir()):
            name = entry.name
            if not (name == lang or name.startswith(f'{lang} (') or name == f'{lang}.tar'):
                continue
            if entry.is_dir():
                yield dlc, entry
            elif entry.suffix == '.tar':
                dest = extracted / lang / dlc
                if not dest.exists():
                    dest.mkdir(parents=True)
                    with tarfile.open(entry) as tar:
                        if sys.version_info >= (3, 12):
                            tar.extractall(dest, filter='data')
                        else:
                            tar.extractall(dest)
                yield dlc, dest


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument('--game-dir', type=Path)
    src.add_argument('--r2', action='store_true')
    ap.add_argument('--r2-prefix', default='Game-Linux/')
    ap.add_argument('--languages', default='')
    args = ap.parse_args()
    langs = target_languages(args.languages)
    CACHE.mkdir(exist_ok=True)
    game = fetch_r2(args.r2_prefix, langs) if args.r2 else args.game_dir
    extracted = CACHE / 'extracted'
    english = {}
    for dlc in DLCS:
        english.update(keyed(game / 'Data' / dlc / 'Languages' / 'English' / 'Keyed'))
    if not english:
        sys.exit(f'No English Keyed files found under {game}/Data/*/Languages/English/Keyed')
    for lang in langs:
        rows = []
        for dlc, folder in language_dirs(game, lang, extracted):
            for key, value in keyed(folder / 'Keyed').items():
                if key in english:
                    rows.append(('K', key, english[key], value))
            for p in glob.glob(str(folder / 'DefInjected' / '**' / '*.xml'), recursive=True):
                text = open(p, encoding='utf-8-sig', errors='replace').read()
                for m in EN_COMMENT.finditer(text):
                    rows.append(('D', m.group(2), m.group(1).strip(), m.group(3).strip()))
        out = CACHE / f'gloss_{lang}.tsv'
        with open(out, 'w', encoding='utf-8') as f:
            for row in rows:
                f.write('\t'.join(x.replace('\t', ' ').replace('\n', '\\n') for x in row) + '\n')
        print(f'{lang}: {len(rows)} rows -> {out.relative_to(HERE.parents[1])}')
    if args.r2:
        shutil.rmtree(CACHE / 'raw', ignore_errors=True)


if __name__ == '__main__':
    main()
