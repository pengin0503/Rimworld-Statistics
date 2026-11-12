# Planned MOD-specific statistics — master index (2026-11-12)

**Scope:** RimWorld 1.7; main repository `pengin0503/Rimworld-Statistics`. These are **all 26 implementation-planned candidates** from the 2026-11-12 discussions (18 previously listed + 8 additional research targets). "Planned" is a backlog commitment to investigate and implement **eligible distinct statistics**, **not** a statement that every proposed metric is currently collectable, compatible, tested, or shipping. Do not silently delete candidates when blocked: annotate the reason and prerequisite.

**Authoritative for these proposals:** this folder. For pre-existing host integration status use the current source and README; older documents such as [generic-first audit](../research/2026-11-11-content-mod-coverage-and-priority-audit.md), [optional MOD audit](../research/2026-11-12-m7-optional-mod-audit.md), and [next MOD metric backlog](../research/2026-11-11-next-modded-statistics-backlog.md) contain dated evidence, **not a claim of present implementation state**.

## Non-negotiable implementation contract

1. **One MOD package:** implement directly in Rimworld-Statistics; no separate statistics add-on repository. Never turn existing generic captures into duplicate MOD-branded metric families.
2. **Cross-colony lifetime first:** prefer confirmed post-success events and meaningful per-Def breakdowns. **Snapshots** (current stock, active units, capacities, installed perks) must be labeled and aggregated according to their semantics, not summed across checkpoint time as lifetime production.
3. **Success, not attempts:** hooks must observe confirmed completed/succeeded actions after appropriate guards, not just method entry, Tick or nominal throughput. For high-frequency operations, use bounded buffering or efficient aggregation, not per-tick reflection/logging.
4. **No DLC assumptions:** gate optional DLC and optional gameplay MODs by actual presence; missing targets fail closed, previous records persist, gaps are Partial/Unknown rather than fabricated zero. In particular, Anomaly-only coverage is not a required acceptance test.
5. **RimWorld 1.7 check:** inspect the active `loadFolders.xml` and *actual selected assembly* (sometimes version folder 1.6); confirm public source/IL signature and successful event. 1.7 branding or folder existence alone is insufficient.
6. **Cross-MOD key identity:** use package identity plus Def identity/version where applicable; avoid treating a bare DefName as globally unique across unrelated MOD sets.
7. **Evidence gate:** source/IL audit, collision/duplication audit against existing families, negative/positive tests, build and packaged artifact checks; optional real-game confirmation when practical. Keep new unproven capture Experimental/opt-in.
8. **No code changes in this documentation task.** Existing v1.6.0 release and Steam Workshop are untouched.

## A. Previously investigated in this conversation (11 newly proposed targets)

| Target MOD | Priority | Proposed cumulative or comparison metrics | Status / primary gap |
|---|---|---|---|
| Vanilla Furniture Expanded - Factory | High | Automated harvest/sow success by plant; independently completed factory processing by product | Source of autofarmer confirmed; bill/harvest overlap must be excluded |
| Vanilla Factions Expanded - Insectoids 2 | High | Player-owned artificial-hive insect births by kind, hive-lifecycle achievements | 1.7 `CompHive.TrySpawnPawn` source inspected; count successful world spawn only |
| Vanilla Factions Expanded - Pirates | High | Warcasket conversions by type, hire/contract successes and silver paid | Source-specific committed outcome still needs audit |
| Vanilla Quests Expanded - Drone Factory | High | Completed drone production by kind, drone-specific completed objectives | 1.7 selects 1.6 source; hatcher and assembler observed; success check required |
| Vanilla Aspirations Expanded | High | Completed aspirations by type, fully fulfilled colonists, completions per colony | `Need_Fulfillment.Complete` observed; count once per completion |
| Vanilla Skills Expanded | High | Expertise unlocks, highest expertise-level achievements | Determine correct source/version and permanent-versus-ephemeral status |
| Vanilla Factions Expanded - Deserters | Med–High | Confirmed assassination mission outcomes, intelligence earned/spent, campaign milestones | Unique operations need outcome-level source audit |
| Vanilla Quests Expanded - The Generator | Medium | ARC generator upgrade completions, dedicated quest success | Separate completed stages from repeatable snapshots |
| Vanilla Races Expanded - Android | Medium | Completed android manufacture, awakening events, record android roster | Avoid generic pawn-creation and recruitment duplication |
| Prison Expanded | Medium | Actual parole/releases, riot outcomes, completed sentences | Resolve exact Workshop package/fork and callbacks first |
| Combat Extended | Low–Medium | Ammunition consumed by type, completed reloads, successful suppression events | Hot-path/TPS sensitivity; never patch every projectile without batching |

## B. Previously investigated in repository research and explicitly retained (7)

| Target MOD | Priority | Proposed distinct metrics | Status / primary gap |
|---|---|---|---|
| Vanilla Genetics Expanded | High | Completed DNA extractions and successful genetic transformations by kind | Actual 1.7 source/assembly plus outcome verification |
| Dubs Rimatomics | Medium | Actual nuclear processes, power generated/energy units, operating durations where defensible | 1.7 DLL/IL and measured output vs rated capacity |
| Rimefeller | High | Committed crude extraction, refined fuel production/consumption by quantity | Read-only IL audit follow-up [#176](https://github.com/pengin0503/Rimworld-Statistics/issues/176); must not count stock as production |
| Save Our Ship 2 | Medium | Ship launches, completed space battles/victories, damage milestones | Independent ship framework and outcome source audit |
| A RimWorld of Magic | Medium | Confirmed spell casts by spell, ability unlocks and class progression | Separate from Royalty/VPE casts; per-tick overhead controls |
| Gastronomy (Continued) | Medium | Fully settled guest orders, restaurant revenue and completed services | Confirm actual paid sales; avoid Hospitality or vanilla trade duplicates |
| Vanilla Brewing Expanded | Medium | Independent fermenter batches completed by output and quantity | Generic bill-produced products already counted; fermentation path only |

## C. Further investigated on 2026-11-12 (8 additional targets)

| Target MOD | Priority | Proposed distinct metrics | Source / implementation gate |
|---|---|---|---|
| Vanilla Factions Expanded - Tribals | High | Era advancements, cornerstones acquired, tribal research milestones | `GameComponent_Tribals.AdvanceToEra`, `AddCornerstone`; verify successful change, new-game/load behavior |
| Vanilla Factions Expanded - Classical | High | New perks, senator favor and unique diplomatic outcomes | `GameComponent_PerkManager.AddPerk`, `WorldComponent_Senators.GainFavorOf`; skip rehydration |
| Vanilla Factions Expanded - Settlers | Medium | Bounty-hunt completion, caravan raid mission success | Must distinguish quest success from generic quest metrics |
| Vanilla Quests Expanded - Cryptoforge | High | Completed scanner operations and generator restarts | `Scannable.Notify_EndScan`; confirm exactly-once, quest association |
| Vanilla Quests Expanded - Ancients | High | Successful archogen injections and outcome breakdown; vault achievements | `Building_ArchogenInjector.FinishProcess`; outcome success/rejection distinct |
| Vanilla Nutrient Paste Expanded | High | Actual nutrient slurry produced/dispensed via MOD systems, optionally nutrition units | `Building_NutrientGrinder.TryProducePaste`; must distinguish feed consumption vs production |
| Vanilla Books Expanded | Medium | Finished reading of MOD-specific books, authoring completion if unique | `JobDriver_Reading_ReadBook_Patch`; check whether vanilla already records it |
| Project RimFactory Revived | Conditional | Autonomous machine production by product and machine | Repo has unusual 1.7 `loadFolders.xml` selecting root only; **compatibility and active binary not yet established** |

## Follow-on research

- [Detailed source/method evidence and cautions](2026-11-12-source-research.md)
- [Implementation and deduplication checklist](implementation-checklist.md)
- This index is the planning register for all **26** targets; additions must be appended with an evidence level and a clear success-path audit requirement.
