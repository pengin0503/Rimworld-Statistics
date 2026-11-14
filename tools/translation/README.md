# tools/translation

Scripts for reviewing and retranslating `Languages/*/Keyed/RimworldStatistics.xml`.
The status, remaining work, conventions and per-language official terminology are in
[docs/translation/2026-11-14-retranslation-handoff.md](../../docs/translation/2026-11-14-retranslation-handoff.md);
machine-readable progress is in [progress.json](progress.json).

| Script | Purpose |
| --- | --- |
| `build_vanilla_glossary.py` | Build English→language glossaries from the official RimWorld translations (local game dir or the private R2 mirror) into `.cache/` (git-ignored; Ludeon content is never committed). |
| `glossary.py` | Search the glossaries (`search`, `core`, `count`). |
| `show_keys.py` | Print keys by position in the English key order, optionally next to other languages. |
| `apply_patch.py` | Apply a `{key: text}` JSON to one language, changing only those values and validating placeholders/XML. |
| `derive_variant.py` | Draft Latin American Spanish, European Portuguese or Traditional Chinese from a finished sibling chunk (review every line). |
| `apply_chunk.sh` | Check and apply one block (`work/<Language>_NN.json`) for all ten retranslated languages, then run `tools/check_keys.sh`. |

Work files go in `tools/translation/work/` (may be committed to hand work over).
Only `About/`, `1.7/Assemblies/` and `Languages/` are packaged, so nothing here ships with the mod.
