#!/usr/bin/env bash
# Apply one translated block to all ten retranslated languages and check that the block is complete.
# usage: tools/translation/apply_chunk.sh NN START END
#   reads tools/translation/work/<Language>_NN.json; START..END are 1-based positions in the English key order
#   (see the block table in docs/translation/2026-11-14-retranslation-handoff.md).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
nn=${1:?usage: apply_chunk.sh NN START END}
start=${2:?usage: apply_chunk.sh NN START END}
end=${3:?usage: apply_chunk.sh NN START END}
langs="German French Spanish SpanishLatin Portuguese PortugueseBrazilian Russian ChineseSimplified ChineseTraditional Korean"
python3 - "$here" "$nn" "$start" "$end" $langs <<'PY'
import json, sys, xml.etree.ElementTree as ET
from pathlib import Path
here, nn, a, b, langs = Path(sys.argv[1]), sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), sys.argv[5:]
en = [e.tag for e in ET.parse(here.parents[1] / 'Languages/English/Keyed/RimworldStatistics.xml').getroot()]
block = set(en[a - 1:b]) - {'RWS_SettingsCategory'}
bad = False
for lang in langs:
    d = json.load(open(here / 'work' / f'{lang}_{nn}.json', encoding='utf-8'))
    missing, extra = sorted(block - set(d)), sorted(set(d) - block)
    if missing or extra:
        bad = True
        print(f'{lang}: missing {missing} extra {extra}')
if bad:
    sys.exit('block incomplete; nothing applied')
PY
for lang in $langs; do
  python3 "$here/apply_patch.py" "$lang" "$here/work/${lang}_${nn}.json"
done
bash "$here/../check_keys.sh"
