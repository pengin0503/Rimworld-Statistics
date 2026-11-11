# Real RimWorld 1.7 game A+B smoke — Statistics SaveReader v13 (2026-11-10)

**Qualified result: PASS for actual game launch and expected Harmony registration, source-pinned.** This is independent of the private 49-original offline reader acceptance and is **not** an in-game stat count comparison.

## Immutable provenance

| Component | Exact reference |
|---|---|
| Statistics candidate code | `pengin0503/Rimworld-Statistics@fe85905a5840742a288490b7c40e981dfe0ac120`, draft [PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102), SaveReader **v13** |
| Qualification harness | `pengin0503/RimWorld-for-mod-test@52ffea80a9944f4cd87881ef2d8118d96bdc8617`, merged [PR #73](https://github.com/pengin0503/RimWorld-for-mod-test/pull/73) |
| Actual game | Private Cloudflare R2 `rimworld-17/Game-Linux`, 1.7.4985 rev1269; real Harmony installed from R2 |
| Qualified workflow | [RWMT run #42846305625](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305625): **source-build-preflight success**, **statistics-a-smoke success** |

## Exact safe runner results

- **A-level actual-game launch** (game started with/without Statistics; qualified comparison): `{"fidelity":"qualified","newErrorCount":0,"reasons":[],"stageA":{"checks":0,"status":"completed"},"status":"passed"}`. No new startup exceptions relative to the test baseline. The reported `checks=0` is exact runner output, **not an in-game content-check count**.
- **B-level Harmony registration**: `{"fidelity":"qualified","reasons":[],"stageB":{"expectedPatches":2,"status":"completed","unexpectedPatches":9},"status":"passed"}`. Two explicit tested title-menu prefixes `GoToMainMenuPatch` and `MainMenuButtonPatch` matched. Nine non-contract patches existed, and the safe report does not attribute/explain them; do not assert there are only two total patches.
- **Both stages:** `passed`, `qualified`; no private raw game files or keys committed. Credentials scoped only to asset preparation; test execution does not receive R2 credentials.

## Relationship to real v13 save parsing

Separate [private original .NET run #42846305788](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305788) passed: all **49/49 source copies unchanged** and **0 errors**. Its 1.7 positive `Partial` historical observations were **research=7**, **turret=0**, **Silver=6**, **Core medicines=6** (maximum eligible seven 1.7 saves; no actual-zero interpretation). See [immutable original-file evidence](2026-11-10-r2-49-original-reader-v13-acceptance.md).

## Trust boundaries and remaining work

- Actual engine used **1.7.4985**. Per user's explicit decision, unstable 1.7.4985 and 1.7.5000 are **provisionally treated as equivalent for development**, not proved byte-equal.
- A+B verifies game startup and Harmony registration; it **does not load the eight newest user originals and compare in-game research/stock/turret numbers to the MOD UI**, verify net power generation, free/prisoner/slave roster, inventory holders, medical history, gameplay TPS/FPS or save/load benchmark.
- v13 historical snapshots remain **Partial/Experimental** and snapshot-only positive lower bounds. No claim of complete 415 in-game statistic coverage. This candidate is still an unmerged draft PR; main is shipped v1.4.0 with old reader version.
- Sources: [R2 game IL/Def 1.7 home stock and turret evidence](../research/2026-11-10-r2-1-7-historical-home-stock-turret-evidence.md), [post-v1.4 execution plan](../superpowers/plans/2026-11-10-post-v1.4-five-save-improvements.md).
