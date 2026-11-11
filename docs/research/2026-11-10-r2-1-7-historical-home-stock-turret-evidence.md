# RimWorld 1.7 R2 game-source evidence for historical on-map facilities and stock (2026-11-10)

## Sources, scope, and version assumption

- Actual game source **1.7.4985 rev1269** archived in Cloudflare R2 `rimworld-17/Game-Linux/` and audited `rimworld-17/research/vanilla-1.7.4985-audited/`:
  - `assembly/Assembly-CSharp/type-index.json.gz` and `chunk-*.json.gz` (real game class/field metadata); `il-*.json.gz` (method call graphs).
  - `resolved/all-five.json.gz` (official Core + DLC static Defs after XML inheritance/patch processing; **not 192-MOD resolved runtime**).
- User instruction: all unstable **1.7** revisions are **provisionally treated as compatible for development**, including the 1.7.5000 originals. It does *not* prove exact MOD patches, identical runtime fields or full gameplay correctness.
- User's eight private 1.7.5000 save originals, in two chronological cohorts of 3 and 5, were read **locally without modification**. Only aggregate count evidence is committed; no original save files, names, colony IDs, pawn IDs, MOD lists, hashes, seeds, or contents are uploaded.
- There is **one campaign** represented by the eight saves. They are successive observations, not eight additive colonies.

## Verified type and Def semantics

| Entity | Actual R2 1.7 game type or Def | Offline interpretation |
| --- | --- | --- |
| Vanilla turret | `RimWorld.Building_TurretGun` inherits `RimWorld.Building_Turret`; which inherits `Verse.Building` | Recognize exact saved `Class=Building_TurretGun` or `Building_Turret` only when its saved faction is the uniquely resolved active player faction and its map parent is a verified player-owned vanilla `Settlement` |
| Silver | Core `ThingDef Silver`, `thingClass=ThingWithComps`, `category=Item`, default `stackLimit=500` | Exact pair `Class=ThingWithComps` and `def=Silver`, positive saved `stackCount` (default 1), nonhostile/neutral faction and a verified home map; ignore explicitly forbidden |
| Herbal medicine | Core `MedicineHerbal`, `thingClass=Medicine`, default `stackLimit=25` | Same guarded stock path |
| Industrial medicine | Core `MedicineIndustrial`, `thingClass=Medicine`, default `stackLimit=25` | Same guarded stock path |
| Glitterworld medicine | Core `MedicineUltratech`, `thingClass=Medicine`, default `stackLimit=25` | Same guarded stock path |
| Medical beds | `RimWorld.Building_Bed.get_Medical` and private `medicalInt` (real assembly) | The eight save files have no explicitly usable medical state in their saved bed nodes: **do not equate all beds with medical beds** |
| Free colonists/prisoners/slaves | `Verse.Pawn.get_IsFreeColonist` invokes `get_HostFaction`; `get_IsPrisonerOfColony` calls `Pawn_GuestTracker.IsPrisoner` and `HostFaction`; `get_IsSlave` calls `Pawn_GuestTracker.IsSlave` | A saved map Pawn count, `faction` or `joinStatus` alone is **not** an authoritative free/prisoner/slave classification |
| Running generators | `RimWorld.CompPowerTrader.get_PowerOutput` includes `StunnedByEMP`; `CompPowerPlant.get_DesiredPowerOutput` invokes `CompProperties_Power.PowerConsumption` | Saved presence of generator/Def/nominal power does **not** prove positive effective output or a running state |

Because historical sources include other MODs, nested stock holders, inaccessible/forbidden inventory and unrecognized classes, **saved-state values are positive-only lower bounds with `MetricCoverage.Partial` / `MetricEvidence.Experimental`**. Missing values are Unknown, **never zero**. These are *not lifetime production/sales/medical treatment events*. Do not sum eight snapshots as a lifetime count.

## Anonymous verification of eight original saves

Counts below are the **direct, saved Thing evidence after the same map owner and class/Def filters**. The latest code path uses these same positive, partial semantics; this is independent Python/lxml evidence and is **not** game-runtime UI parity.

| Private cohort checkpoint (anonymized order) | Verified own turrets | Recognized direct-map Silver units | Recognized direct-map three vanilla medicine units |
| --- | ---: | ---: | ---: |
| Earlier #1 | 2 | 1,210 | 375 |
| Earlier #2 | 2 | 1,210 | 379 |
| Earlier #3 | 2 | 2,313 | 385 |
| Later #1 | 2 | 3,090 | 414 |
| Later #2 | 2 | 3,090 | 436 |
| Later #3 | 2 | 3,090 | 437 |
| Later #4 | 2 | 3,090 | 436 |
| Later #5 | 2 | 6 | 448 |

For all eight, exactly one confirmed player-owned Settlement map is present; no inferred cross-campaign lifetime sum is attempted. Recognized classes and Defs were independently audited against R2 game data. **The Silver fluctuation is an observed snapshot difference, not proof of sale, disappearance, caravan transfer or any event cause.**

## Implementation contract (Statistics draft PR #102)

- Reader **v12** added bounded `things/thing` streaming to the pre-existing strict `HistoricalHomeMapReader` player-home reference graph, emitting `FacilitiesSnapshot.TurretsPeak` Partial only when it finds actual owned turrets. CI [#42841943226](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42841943226): **723 Core passed, 0 failed**; actual game parity of turret UI not yet measured.
- Reader **v13** adds exact Class+Def on-map Silver and Medicine recognition; emits only `StockSnapshot.SilverLatest` and `StockSnapshot.MedicinePeak` if independently positive, always Partial/Experimental. Red tests at [commit `19b355c4`](https://github.com/pengin0503/Rimworld-Statistics/commit/19b355c4bde9a476e76c3f9d0354f5667c31ae32), [CI #42843344428](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42843344428) failed **two** stock positive/invalid cases before implementation. Verified v13 GREEN in [CI #42844594938](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42844594938): 726 Core successful, 0 failed, 9 skipped; 888 translation keys. The final source-pinned anonymous acceptance CLI also passed [CI #42845357275](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42845357275). [Actual 49 R2 originals reader v13](../testing/2026-11-10-r2-49-original-reader-v13-acceptance.md) passed: 49 unchanged, 0 errors, research 7/7 Partial, attributable turret positive observations 0/7, Silver 6/7, medicine 6/7; timing p50 67.846ms/p95 205.922ms, not controlled A/B.
- Deliberate exclusions: unrecognized MOD subclass turrets, unknown MOD silver/medicine Def patches, items in containers, pawn equipment and caravans, forbidden items, other 1.5/1.6 versions, missing maps or ambiguous owners. Do not fabricate medical bed counts or working generators.
- Existing metric IDs/revisions (**415 definitions**) and checkpoint payload format **v1** remain unchanged; reader version changes force re-read of old saves. No private files or user-specific identities are persisted by these new outputs.

## Follow-up verification gates

1. v13 Core RED→GREEN regressions plus full suite, translation and static game API checks.
2. Read-only **v13** production .NET `SaveReader.ReadDetailed` over private 49 R2 originals using an immutable Statistics source SHA; old [v11 run #42838545431](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545431) cannot be relabeled v13.
3. Qualified 1.7 actual game A+B startup/Harmony registration via `RimWorld-for-mod-test` using the new pinned source SHA. Old [v11 real-game run #42838545527](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545527) is historical, not v13.
4. The exact loaded MOD-resolved snapshot UI counts, 55+ independent original saves and TPS/saving benchmarks remain unverified. A partially reconstructed snapshot is not a verified full colony lifetime record.
