# Planned MOD-specific statistics — master index (2026-11-12)

**Scope:** RimWorld 1.7; main repository `pengin0503/Rimworld-Statistics`. These are **all 35 implementation-planned candidates** from the 2026-11-12 discussions (18 previously listed + 8 additional research targets + 9 popularity-selected follow-ups). "Planned" is a backlog commitment to investigate and implement **eligible distinct statistics**, **not** a statement that every proposed metric is currently collectable, compatible, tested, or shipping. Do not silently delete candidates when blocked: annotate the reason and prerequisite.

**Authoritative for these proposals:** this folder. For pre-existing host integration status use the current source and README; older documents such as [generic-first audit](../research/2026-11-11-content-mod-coverage-and-priority-audit.md), [optional MOD audit](../research/2026-11-12-m7-optional-mod-audit.md), and [next MOD metric backlog](../research/2026-11-11-next-modded-statistics-backlog.md) contain dated evidence, **not a claim of present implementation state**.

## Implementation entry point

Start with the [implementation handoff](2026-11-13-implementation-handoff.md). The [35-candidate disposition](2026-11-13-candidate-disposition.md) records the first implementation slices and the evidence each remaining proposal needs. Use the [pinned references](2026-11-13-reference-lock.md) to reproduce the source and assembly checks. These three documents carry the current decisions when an earlier research note describes a preliminary status.

## Second-wave scope is also committed

The [nine second-wave implementation tasks (26–34)](2026-11-13-secondary-implementation-scope.md) originally made the previously deferred secondary statistics **required implementation targets** (subsequently amended only for evidence-gated Tasks 27–29), not excluded ideas. They cover Combat Extended ammunition/suppression; Gastronomy paid orders; Prison Expanded departures, riots and completed sentences; Rimefeller measured production/consumption; Pirates contracts/payments; Skills expertise-level records; Security artillery; Genetics hybrid births/transformations; and Drone Factory hatching. They augment, and never replace, the first 25 slices or the 35-MOD master register. Outcomes awaiting a proven 1.7 commit site remain explicitly tracked until implemented or evidenced as covered by an existing generic metric.

**2026-11-13 amendment:** The owner permitted dropping difficult-to-verify individual metrics rather than adding misleading proxies. The remaining unproven Task 27 paid-order count, Task 28 whole-riot result and Task 29 refined-product deliveries / crude-feed inputs are [waived, not implemented](2026-11-13-evidence-gated-outcome-disposition.md). Prior measurements in #210, #212 and #213 are retained. This exception does not cancel any other planned MOD targets.

## Non-negotiable implementation contract

1. **One MOD package:** implement directly in Rimworld-Statistics; no separate statistics add-on repository. Never turn existing generic captures into duplicate MOD-branded metric families.
2. **Cross-colony lifetime first:** prefer confirmed post-success events and meaningful per-Def breakdowns. **Snapshots** (current stock, active units, capacities, installed perks) must be labeled and aggregated according to their semantics, not summed across checkpoint time as lifetime production.
3. **Success, not attempts:** hooks must observe confirmed completed/succeeded actions after appropriate guards, not just method entry, Tick or nominal throughput. For high-frequency operations, use bounded buffering or efficient aggregation, not per-tick reflection/logging.
4. **No DLC assumptions:** gate optional DLC and optional gameplay MODs by actual presence; missing targets fail closed, previous records persist, gaps are Partial/Unknown rather than fabricated zero. In particular, Anomaly-only coverage is not a required acceptance test.
5. **RimWorld 1.7 check:** inspect the active `loadFolders.xml` and *actual selected assembly* (sometimes version folder 1.6); confirm public source/IL signature and successful event. 1.7 branding or folder existence alone is insufficient.
6. **Cross-MOD key identity:** use package identity plus Def identity/version where applicable; avoid treating a bare DefName as globally unique across unrelated MOD sets.
7. **Evidence gate:** source/IL audit, collision/duplication audit against existing families, negative/positive tests, build and packaged artifact checks; optional real-game confirmation when practical. Keep new unproven capture classified Experimental/Unverified, but enable eligible statistics by default on new installations. Respect explicit existing opt-outs, and allow recording changes without restart.
8. **No code changes in this documentation task.** Existing v1.6.0 release and Steam Workshop are untouched.

## Popularity-aware execution priority

The **authoritative execution ordering** (including previously registered candidates) is in [the 2026-11-12 subscriber/feasibility ranking](2026-11-12-popularity-priority.md). The legacy `Priority` cells in sections A–C are the **initial technical-interest ratings**, not a claim that those candidates outrank higher-subscriber options. For new engineering batches, first rank by active Workshop subscribers (a dated, imperfect proxy for reach), then by existence of genuinely distinct events, evidence quality, integration cost and 1.7 compatibility. Do **not** create a useless or duplicate metric merely because the base MOD is popular.

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

## D. Popularity-led additions (9 more, 2026-11-12)

These are also **planned**; the source/method status is not a promise that all proposals are directly implementable. Workshop approximate subscribers are a **crawled snapshot**, not an exact 2026-11-12 real-time count. Exact reference URLs, lower-confidence cases and exclusion rationale are documented in [the popularity audit](2026-11-12-popularity-priority.md).

| Target MOD | Approx subscribers | Execution lane | Proposed distinct metrics | Source and gating |
|---|---:|---|---|---|
| Vanilla Furniture Expanded - Security | ~890k | Popularity P0 | Successfully rearmed traps; confirmed off-map artillery bombardments by type | 1.7 loads 1.6; `CompRearmable.Rearm` and `JobDriver_RearmTrap` confirmed; exclude spawn setup and job attempts; artillery outcome proof required |
| Vanilla Cooking Expanded | ~739k | Popularity P0, conditional | Independently finished soup/cheese aging batches by product and count | 1.7 loads 1.6; standard recipe products already generic; locate the actually loaded ferment/aged-food success callback (may be VEF) before a metric |
| Vanilla Social Interactions Expanded | ~583k | Popularity P0 | Aspirations fully completed by inspiration; successful unique teaching interactions | 1.7-specific source: `Aspiration.OnComplete`, `InteractionWorker_Teaching.Interacted`; distinct from Vanilla Aspirations Expanded and existing social family |
| Vanilla Factions Expanded - Empire | ~409k | Popularity P1 | Newly awarded special honors/court milestones; dedicated imperial ceremonies successfully completed | 1.7-specific source: `GameComponent_Honors.AddHonor`; verify no inherited/read-from-save duplication and that quest outcomes are not already counted |
| Vanilla Chemfuel Expanded | ~386k | Popularity P1, conditional | Actual drilling/refining quantities and material type breakdowns | 1.7 loads 1.6; local `CompRefinery` only wraps VEF `CompResourceProcessor` animation: inspect framework actual committed flows, do not derive quantities from capacity |
| Vanilla Factions Expanded - Medieval 2 | ~321k | Popularity P1 | Successfully harvested apiary honey quantities and beekeeper achievements | 1.7 loads 1.6; `Building_Apiary.TakeOutHoney` returns produced stack, but only count if downstream delivery truly succeeds |
| Vanilla Achievements Expanded | ~262k | Popularity P1 | Earned achievements by Def, earned achievement points across independent colonies | 1.7 code: `AchievementCard.UnlockCard` and `AchievementPointManager.AddPoints`; debug unlock, save reload and source's own existing achievement records require careful deduplication |
| Giddy-Up 2 - Continued | ~125–132k | Popularity P2 | Successfully mounted riding sessions by mount-kind, dismount completions if meaningful | 1.7 code: `MountUtility.GoMount`; distinguish successful player mounts vs premounted arrivals and AI retries |
| Simple Warrants | ~127–128k | Popularity P2, conditional | Actually fulfilled warrants by target category, payouts earned/paid | Steam announces 1.7 update Oct 27 2026; verify actual loaded DLL and confirmed `Fulfill Warrant` transaction; vanilla quest count must not be duplicated |

## Follow-on research

- [Detailed source/method evidence and cautions](2026-11-12-source-research.md)
- [Implementation and deduplication checklist](implementation-checklist.md)
- [Popularity-first execution priority, source references and explicit exclusions](2026-11-12-popularity-priority.md)
- This index is the planning register for all **35** targets; additions must be appended with an evidence level and a clear success-path audit requirement.
