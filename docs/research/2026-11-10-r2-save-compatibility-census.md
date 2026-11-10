# R2 private save compatibility census — 49 originals (2026-11-10)

**Evidence grade:** read-only object-body metadata extraction, not production .NET SaveReader/game acceptance. The connected Cloudflare account's existing `rimworld-17/Saves/` prefix was fully listed (49 objects), and **every one of the 49 original .rws/.rws.old objects** was read in-memory via R2 GET. No original object was written, deleted, copied to GitHub, or placed in a CI artifact. The object keys, exact per-object byte lengths, ETags, ordered MOD lists, colony/pawn IDs and full contents are deliberately **not published**. Exact game-build identifiers and aggregate counts are retained because they are necessary to reproduce version-specific tests.

## Complete distribution

| Game build from `meta/gameVersion` | Save objects | MOD IDs minimum–maximum |
| --- | ---: | ---: |
| `1.5.4104 rev435` | 11 | 32–117 |
| `1.5.4243 rev947` | 4 | 117–119 |
| `1.5.4297 rev1079` | 5 | 119–127 |
| `1.5.4409 rev1120` | 2 | 127 |
| `1.6.4523 rev1054` | 1 | 74 |
| `1.6.4535 rev993` | 1 | 79 |
| `1.6.4566 rev607` | 8 | 96–193 |
| `1.6.4630 rev467` | 1 | 192 |
| `1.6.4633 rev1261` | 7 | 201–209 |
| `1.6.4850 rev646` | 2 | 204–205 |
| `1.7.4979 rev109` | 1 | 207 |
| `1.7.4985 rev1269` | 6 | 239–252 |
| **Total** | **49** | **32–252** |

**Major/minor:** 1.5 **22** objects, 1.6 **20**, 1.7 **7**; **12 exact game builds**. Across the 49 in-memory object bodies, the extraction found **0 `numericStatistics` embedded checkpoints**. This supports choosing legacy-without-checkpoint test candidates, **not** a claim that the complete XML for each original has passed a schema validator or that a vanilla event did not occur. Four sampled older originals independently exposed `researchManager`; the entire set of 49 has **not** been formally validated for usable research progress structure.

**Crucial new opportunity:** `Game-Linux/Version.txt` and the audited 2,672-file source corpus match **six R2 1.7.4985 original saves** exactly at the *build identifier* level. But these six saves contain **239–252** MODs. Matching build is necessary and **not sufficient** for completed research, Pawn, stock or power semantics; vanilla/Core+DLC Defs do not resolve all installed MOD-defined types. They are valid candidates for a qualified game-host compatibility test with the **actual saved MOD inventories** loaded privately. Do not treat `1.7.4979` or any `1.7.5000` attached sample as exact-match for these 4985 DLLs.

## Subsequent production .NET validation

After this original metadata census, a **separate private run using the actual production .NET SaveReader** was executed and **passed**: [RWMT #42830780995](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42830780995), [immutable acceptance evidence](../testing/2026-11-10-private-r2-49-original-net-acceptance.md). 49/49 originals preserved, 0 reader/integrity errors, all 12 exact build counts matched, p50 **36.808 ms** and p95 **125.850 ms** (median managed allocations **808,696 bytes**). The census on this page remains a **metadata method**, not a runtime Def correctness verification. The program does not print raw source IDs or original names.

## Reproducible read-only method

1. `GET /accounts/{account_id}/r2/buckets/rimworld-17/objects?prefix=Saves/&per_page=1000`; continue `result_info.cursor` until `is_truncated=false`. The present inventory returned 49 objects and 824,593,049 bytes.
2. For each object in a private runner, read `GET /accounts/{account_id}/r2/buckets/rimworld-17/objects/{object_key}` and extract only `<meta><gameVersion>` and `<meta><modIds><li>` counts, plus the presence/absence of the `numericStatistics` tag. Never put `object_key` or the body in output, tests, commits or issue comments. On this verification call the APIs returned an in-memory text body (UTF-8 code-unit length differs from R2 byte length).
3. Group by exact `major.minor.build revN` and output aggregate `count, minimumModIds, maximumModIds, hasCheckpointCount`. Do **not** report file-level fingerprints, exact lengths or the MOD names.
4. This is a metadata **census**, not a game-version correctness acceptance. To turn a row into a true historical-backfill test, use `pengin0503/RimWorld-for-mod-test` with the corresponding game+MOD build in an isolated private runner and perform `SaveReader.ReadDetailed`, check classification against game-side `SavedStateMetricReader`, and verify unchanged source SHA/size/mtime. Record only bounded anonymized assertion results.
5. Independent local original set: **five newer** + **three older** 1.7.5000 / 192-MOD samples, all one campaign with private records; these **are not in this 49-R2-object census**. See [R2 source inventory](2026-11-10-r2-private-reference-inventory.md) and [five-save evidence](2026-11-10-post-v1.4-private-five-save-audit.md).

## Ready-made private test matrix (do not re-research candidate availability)

- `1.7.4985` exact source build: **6 originals**, 239–252 MODs, no embedded numeric checkpoint — best first target for existing RimWorld-for-mod-test private 4985 harness, after saved MOD list isolation/reconstruction.
- Older 1.7 build: **1 original** `1.7.4979` (207 MODs) — requires matching source or explicit incompatible-version fallback.
- Historical 1.6: **20 originals**, six builds; distinguish unsupported modded Def semantics, DLC inventory and incomplete callbacks from verified values.
- Historical 1.5: **22 originals**, four builds; verify older snapshot schema and that once-only reader upgrades do not replace last-good data.
- Cross-colony ranking needs **distinct actual campaigns**; exact build and MOD count alone never prove unique campaigns or same MOD fingerprint.
- A completed research count must still be derived from an exact matched `ResearchProjectDef.IsFinished` authoritative source, not from positive `researchManager/progress`, even for the six build-matched legacy saves.

**R2 security:** No R2 credentials or private originals were sent to public GitHub. This document intentionally avoids per-object aliases/IDs to prevent use of object sizes as identity proxies. Use the existing R2-bucket credential boundary and do not add secrets to public PR workflows.
