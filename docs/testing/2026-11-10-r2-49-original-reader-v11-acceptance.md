# Read-only R2 original-file qualification of provisional SaveReader v11 (2026-11-10)

**Status: PASS for 49 original-save offline .NET parsing, provenance and integrity. This document does not claim research-value correctness in a fully modded loaded game.**

## Pin and reproduction

- Statistics source at immutable `52cb84de435e65197157a98c212b78546949715f` (PR #102, SaveReader v11); the source is intentionally **unmerged**.
- RWMT workflow/reviewer: [PR #72](https://github.com/pengin0503/RimWorld-for-mod-test/pull/72), merged as `main@fdbe5d907ac380ad35c25b012a33504244549c6c`.
- Actual completed [GitHub Actions run #42838545431](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545431): `public-preflight` and `private-r2-originals` **success**.
- Private R2 `rimworld-17/Saves/` cohort from [anonymous census](../research/2026-11-10-r2-save-compatibility-census.md), **49** originals totaling **824,593,049 bytes**; exactly 12 game builds. All original save names, individual hashes, MOD inventories, identifiers and raw bytes remain private in the RWMT ephemeral runner; R2 LIST/GET only.
- Pin is the code after Source/Tests changes, prior to subsequent purely documentary commits. Changing a working-tree head or reader source requires a **new immutable pin and a new run**.

## Verified from the private runner's completed logs

| Check | Result |
| --- | --- |
| Private input inventory gate | 49 originals / 824,593,049 bytes, accepted |
| Production .NET `SaveReader.ReadDetailed` | 49 read, 0 reader/integrity errors; old build distribution matched exactly |
| Source copy preservation | 49/49 unchanged by SHA-256, file length and modification time |
| Embedded Statistics checkpoints | 0 |
| 1.7 legacy research snapshot recognized | **7/7** R2 1.7 saves returned an inferred research observation |
| Confidence/coverage of inferred research | **7/7 Partial**; no Verified/Complete claim |
| 1.5/1.6 older saves | 42/42 parsed without importing a fictional 1.7 research value |
| SaveReader v11 per-file read timing | **p50 53.132 ms; p95 142.153 ms** |
| Per-file managed allocation median | **809,400 bytes** |
| Ephemeral file cleanup | Completed in `Always erase ephemeral private originals` step |

Compared with the **earlier v10** successful [run #42830780995](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42830780995) (`p50 36.808 ms; p95 125.850 ms; 808,696 managed bytes median`), the new observed run is slower in its reported timestamps, but neither represents a controlled matched-run A/B or RimWorld TPS measurement. **Do not claim a measured regression percentage attributable to v11**.

## Trust boundary and remaining steps

- The saved 1.7 `researchManager/progress` pairs are compared with the 122 official `ResearchProjectDef.baseCost` and tech level entries from the archived audited 1.7.4985 game source. The user explicitly accepts provisional equivalence for unstable 1.7 builds. This is not a proof of loaded, MOD-patched `ResearchProjectDef.IsFinished` values. The per-save estimated finished counts are *not* published and must not be interpreted as verified truth.
- The seven 1.7 R2 originals cover multiple modded contexts, but full gameplay semantics (free-colonist classification, stock ownership, powered generator state and medical lifetime events) have not been validated by this reader run.
- The eight private new/old 1.7.5000 originals are a **different single-campaign cohort** not included in these 49 R2 originals.
- The corresponding real game source-load + Harmony A/B smoke is tracked separately via [RWMT run #42838545527](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545527). **A successful .NET reader run cannot stand in for actual game startup.** Report the real-game run's actual result separately.
- No user saves are modified, no private input is committed, Stats PR #102 remains draft/unmerged, and all 415 existing metric definitions and checkpoint v1 remain intact.

## Next actions without rediscovery

Read [game IL and eight-save research mapping](../research/2026-11-10-r2-game-source-1-7-research-recovery.md), [complete 122-game-Def source](../research/2026-11-10-r2-1-7-research-def-catalog.json), [user's 1.7 compatibility ruling](../superpowers/README-current-and-historical.md) and the [R01–R14 plan](../superpowers/plans/2026-11-10-post-v1.4-five-save-improvements.md). Proceed to actual loaded MOD Def parity and P1 history only if facts support classification; otherwise keep Unknown/Partial. Re-check R2 exact build only for unexpected contradictory evidence.
