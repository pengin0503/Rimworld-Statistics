# Private R2 49-original production .NET SaveReader v13 acceptance — 2026-11-10

**Result: PASS (offline original save parsing, unchanged originals, Partial coverage guards).** Actual game runtime parity and TPS are out of scope of this specific test.

## Immutable source and qualified run

- Reader code: `pengin0503/Rimworld-Statistics@fe85905a5840742a288490b7c40e981dfe0ac120`, draft PR [#102](https://github.com/pengin0503/Rimworld-Statistics/pull/102); `SaveReader.Version = 13`. Shipped v1.4.0 main remains v10.
- Reader Core unit suite: [Statistics Actions #42845357275](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42845357275), success. Earlier code run [#42844594938](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42844594938): 726 Core passed, 0 failed, 9 skipped, 888 translation keys.
- Validated isolated private harness: [RWMT PR #73](https://github.com/pengin0503/RimWorld-for-mod-test/pull/73), merged to `main@52ffea80a9944f4cd87881ef2d8118d96bdc8617`.
- **Actually completed:** [RWMT run #42846305788](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305788), both public preflight and private original jobs **success**.
- Input: exactly **49 original .rws/.rws.old R2 objects**, **824,593,049 bytes**, 12 game builds, 1.5:22, 1.6:20, 1.7:7, no embedded Statistics checkpoint. Filenames, contents, hashes, ownership and MOD list withheld.

## Anonymized observed results, actual production .NET reader

| Property | Result |
|---|---:|
| Original read/integrity errors | **0** |
| Original save copies verified unchanged | **49/49**, size/mtime/SHA-256 |
| Observations reconstructed for 1.7 research | **7** positive/observed; **7 Partial** |
| Observations reconstructed for 1.7 verified home-map turrets | **0** positive; **0 Partial** |
| Observations reconstructed for 1.7 home-map Silver stacks | **6** positive; **6 Partial** |
| Observations reconstructed for 1.7 Core medicine stacks | **6** positive; **6 Partial** |
| Old 1.5/1.6 accidentally imported as 1.7 salvage | **0**, enforced by <=7 and Partial-only gates |
| Per-file read time, p50 | **67.846 ms** |
| Per-file read time, p95 | **205.922 ms** |
| Median managed per-thread allocation | **809,480 bytes** |
| Temporary original files | erased by final cleanup |

Counts report **how many original saves emitted positive provisional values**, not values/units from any individual save, and definitely not number of independent colonies. A zero emitted turret-positive-observation count **does not mean all 49 had zero turrets**. It means the strict owner+home+class proof was not met by any of those R2 files. The user's distinct set of eight recent private original 1.7.5000 saves showed two attributable owned turrets each under a **separate Python source-structure audit**, not this R2 original cohort or a loaded game UI comparison.

### Performance interpretation

Older v11 real R2 [run #42838545431](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545431) reported p50 53.132 ms / p95 142.153 ms and median allocation 809,400 bytes. New v13 reported p50 67.846 ms / p95 205.922 ms and 809,480 bytes. These are different Actions executions, **not controlled same-host paired A/B**, so do not attribute numerical differences exclusively to changed map XML streaming or equate them with TPS. Repeated file scans are not game tick-time capture.

### Scope, evidence and follow-ups

- Verified real game source: R2 1.7.4985 original C# type/IL and static Core+DLC ThingDefs for `Building_TurretGun`, Silver `ThingWithComps`, the three Core `Medicine` class item defs. Source + eight original audit: [game-Def/ownership research](../research/2026-11-10-r2-1-7-historical-home-stock-turret-evidence.md).
- User directs us to assume different **unstable 1.7** build revisions are compatible **provisionally**; this does not prove fully MOD-resolved runtime item identity or loaded game stat counts.
- These new outputs are **strict positive lower bounds** on direct known map Thing instances with ownership evidence, **always `MetricCoverage.Partial` + `MetricEvidence.Experimental`**, never complete living population, all stock/holder inventory, generator uptime or historical medical treatments.
- Missing values do **not** imply observed zero; historical 1.5/1.6 or all unresolved 1.7 cases remain unknown.
- **Actual game startup/Harmony test for immutable v13 pin** is separate [RWMT run #42846305625](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305625), record its actual conclusion independently. The earlier v11 A+B game success cannot be relabeled v13.
- Private originals stay in ephemeral runner, erased. No merge/release of Statistics PR #102.
