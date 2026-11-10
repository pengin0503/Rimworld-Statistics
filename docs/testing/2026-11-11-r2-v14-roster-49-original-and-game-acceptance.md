# SaveReader v14 — R2 real 49-original saves and real RimWorld 1.7 A+B acceptance (2026-11-11)

## Immutable, completed qualification

- Statistics proposed build/source: `pengin0503/Rimworld-Statistics@52b9b758dfdc2a641690a4527d3c11a1676f043e` in **unmerged draft PR #102**, SaveReader.Version **14**. It has independent `population.snapshot.free`, `population.snapshot.prisoners`, `population.snapshot.slaves`. `facilities.running_generators.latest` and its derived rankings were removed; direct stock and pawn-carried inventory remain, nested storage-holder recursion stopped. Existing profile schema **2**, checkpoint **v1** unchanged, **414** active registered metrics.
- Test infrastructure: `pengin0503/RimWorld-for-mod-test@ff67f7a598032488e72276dabb6bb2fd35237dc4`, [PR #74](https://github.com/pengin0503/RimWorld-for-mod-test/pull/74) merged. It pins the exact Statistics SHA above for both workflows.
- Core original-commit CI [#42922487345](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42922487345): **729 passed, 0 failed, 9 skipped**, translation keys **888** aligned; two public RWMT preflights also successful.
- Private R2 production `.NET` offline original files: [RWMT #42923032471](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42923032471): **success**, exactly **49/49 unchanged**, parse errors **0**, 1.7 originals **7** and older versions **42**, checkpoint count **0**, anonymous output schema **3**.
- Private actual game + Harmony: [RWMT #42923032480](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42923032480): **qualified PASS**, A `newErrorCount=0`, `stageA.status=completed` (`checks=0` as reported by runner); B `expectedPatches=2`, `unexpectedPatches=9`, `stageB.status=completed`. Uses real R2 **1.7.4985 rev1269** and Harmony, not an exact loaded-MOD per-Pawn UI equality test.

## Actual anonymous positive Partial observations among seven 1.7 R2 originals

| Historical metric | Positive saved-file observations | Verified Partial observations |
| --- | ---: | ---: |
| Research | 7 | 7 |
| Verified player-home turret | 0 | 0 |
| Direct-home Silver | 6 | 6 |
| Direct-home Core medicines | 6 | 6 |
| **Free colonists** | **7** | **7** |
| **Colony prisoners** | **0** | **0** |
| **Colony slaves** | **0** | **0** |

**0 positive prisoner/slave observations does not mean verified zero prisoners/slaves.** The original 49 R2 files represent heterogeneous earlier saves; the user's eight other local 1.7.5000 originals were structurally audited independently, with **11 player-free human pawn candidates and one explicit owned slave candidate per verified home map** under the game predicates. They were **not** input to the source-pinned private R2 .NET suite. The unit fixtures cover explicit prisoner/slave positive and contradictory/negative guest-state cases. Neither the R2 run nor 8-original Python audit validates all possible world/caravan/transport Pawn holders or MOD-defined humanlike races. Every historical positive is `MetricCoverage.Partial`/`MetricEvidence.Experimental`, never Complete or lifetime unique population. Unknown/missing is **not 0**.

R2 original reader timing: p50 **49.945ms**; p95 **152.877ms**; median thread-managed allocation **809,520 bytes**. No controlled paired benchmark or in-game TPS claim. The previous [v13 run #42846305788](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305788) belonged to a different Statistics source SHA; its timings cannot be attributed to a version change without same-host A/B.

## Verification boundaries and source evidence

The actual game type/IL audit is [R2 1.7 Pawn semantics and canceled scope](../research/2026-11-11-r2-1-7-roster-scope-retirements.md). `Pawn.IsFreeNonSlaveColonist`, `Pawn.IsPrisonerOfColony`, `Pawn.IsSlaveOfColony` call into actual `Pawn_GuestTracker` status, faction and host logic; `joinStatus=JoinAsSlave` alone is **not** a slave. The accepted 1.7 build-equivalence ruling is provisional. Actual-game A+B tests verify **startup + menu patch registration only**; loading 192 specific MODs and matching the full live roster, world pawns and UI counts remains unverified.

No raw original, pawn ID, individual count, save name, seed, personal records, MOD list, R2 credential or profile was uploaded to GitHub. Original copies were deleted after the private run. No Statistics merge or release was performed.
