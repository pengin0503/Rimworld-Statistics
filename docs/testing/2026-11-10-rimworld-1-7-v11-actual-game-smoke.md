# Actual RimWorld 1.7 A+B smoke acceptance of Statistics v11 — 2026-11-10

**Qualified result: PASS for real-game load and expected Harmony patch registration only.** This is NOT in-game parity of all statistics or a measured TPS/save-time result.

## Immutable evidence

| Component | Source |
| --- | --- |
| Statistics source under test | `pengin0503/Rimworld-Statistics@52cb84de435e65197157a98c212b78546949715f`; draft PR #102, SaveReader v11 |
| Qualified actual-game harness | `pengin0503/RimWorld-for-mod-test@fdbe5d907ac380ad35c25b012a33504244549c6c`; merged PR #72 |
| Game runtime in R2 | `rimworld-17/Game-Linux/`, `Game-Linux/Version.txt`: **RimWorld 1.7.4985 rev1269** |
| Harmony dependency | Real private Harmony game dependency prepared by existing RWMT R2 workflow |
| Workflow and full status | [RWMT Actions run #42838545527](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545527), `source-build-preflight` **success**, `statistics-a-smoke` **success** |
| Data handling | Credential-scoped R2 read-only private asset preparation; no game originals, private credentials or raw diagnostic artifacts committed to public repo |

## Exactly verified from the completed job's fixed safe summary

**A-level actual-game load** (with and without pinned Statistics): `{"fidelity":"qualified","newErrorCount":0,"reasons":[],"stageA":{"checks":0,"status":"completed"},"status":"passed"}`.

**B-level Harmony registration:** `{"fidelity":"qualified","reasons":[],"stageB":{"expectedPatches":2,"status":"completed","unexpectedPatches":9},"status":"passed"}`.

The two expected patch contracts are the Statistics title-menu `GoToMainMenuPatch` and `MainMenuButtonPatch` prefixes, as specified in the [RWMT pinned A+B workflow](https://github.com/pengin0503/RimWorld-for-mod-test/blob/main/.github/workflows/private-statistics-smoke.yml). The safe result contains **nine non-contract patches** as `unexpectedPatches`; the qualified gate passed, but **these nine were not individually attributed/explained by the safe result**. Do not assert that zero additional patches exist. Likewise `stageA.checks=0` is the runner's recorded value and not nine hundred gameplay assertions.

These results establish that the **real pinned 1.7.4985 engine was exercised**, the requested menu patch signatures were registered under the test conditions, and the tested startup did not report additional errors versus baseline. They **do not** test the game UI's displayed research completion count, MOD-patched research Def costs, Pawn/Thing ownership, facility output, no-TPS-regression, 1.7.5000-specific engine behavior or real save reload with the user's 192-MOD campaign.

## Relationship to other evidence and next verification

The **independent real R2 49-original .NET reader v11 acceptance** in [#42838545431](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545431) passed with 49 unchanged, 0 errors, 7/7 1.7 historical research values **Partial**, p50 53.132 ms, p95 142.153 ms, median managed allocations 809,400 bytes. This is an offline reader test; combining its evidence with A+B does **not** create an in-game exact research comparator. See the [full immutable v11 reader record](2026-11-10-r2-49-original-reader-v11-acceptance.md).

The user explicitly directs that unstable 1.7.4985 and 1.7.5000 be treated as behaviorally equivalent **as a development assumption**, while preserving source-version evidence. Only the 1.7.4985 actual game binary was tested in the A+B run. No runtime `ResearchProjectDef.IsFinished` comparison for a fully modded 1.7.5000 save has been done; reconstructed project values remain `Partial/Experimental`.

For follow-up work use [the game IL and 122 research Def evidence](../research/2026-11-10-r2-game-source-1-7-research-recovery.md), [current specifications and compatibility decision](../superpowers/README-current-and-historical.md), and the [full implementation/acceptance plan](../superpowers/plans/2026-11-10-post-v1.4-five-save-improvements.md). **No merge or release of Statistics PR #102** has been performed.
