# Popular MOD-specific statistics — follow-up research register (2026-11-14)

## Purpose and relationship to existing plans

This document saves **all 27 distinct candidates** from the four popularity-focused research rounds on 2026-11-14. They are **research candidates, not shipped integrations**. It does not reclassify the original 35 approved implementations, the 27 retained E/F/G/H third/fourth-wave research candidates, or the existing [supported gameplay MOD list](../supported-mods.md). The 27 names below were checked against those indexes; do not append names to the supported list before implementation.

**Research waves:** A = first 9, B = next 7, C = next 6, D = popularity-first re-evaluation 5. D03 and D04 were previously screened out, then expressly re-evaluated as candidates due to more specific unique outcomes. No rejected/low-value suggestions are silently treated as accepted.

**Popularity:** subscriber estimates come from discussion-time Workshop snapshots, not an authenticated live API result. These are rough ordering aids only, may age quickly, and are not claims about active 1.7 installations. Revalidate when scheduling engineering. **Status** means source/description evidence only; actual loaded 1.7 assembly and successful post-commit Harmony hook still require inspection.

**Host integration constraints:** Implement within Rimworld-Statistics; preserve lifetime/checkpoint/branch and missing-MOD history semantics; use completed events and before/after committed deltas, not requests/attempts, UI clicks, tick samples, nominal capacity or unsafely summed snapshots. Check for existing generic stat keys and mod adapters before adding any distinct family. Avoid per-tick reflection and new cross-map polling; use existing event capture where possible. Exclude Anomaly-dependent validation unless independently warranted. Skip an unprovable outcome rather than substitute a misleading proxy. No C# or release changes from this documentation.

### A. First round — nine candidates

| ID | Candidate / reference | Approx. subscribers discussed | Distinct potential statistics | Evidence / key gate | Priority |
|---|---|---:|---|---|---|
| A01 | [Vanilla Furniture Expanded – Farming](https://github.com/Vanilla-Expanded/VanillaFurnitureExpanded-Farming) | 914k | Real sprinkler growth-boost events and positive plant growth delta by PlantDef | `VFEF_SprinklersManager.BoostPlantAt` adds growth; measure actual clamped delta, do not count sprinkler visuals or growth > 1; 1.6 source/loaded 1.7 DLL must be matched | P0 |
| A02 | [Vanilla Furniture Expanded – Power](https://steamcommunity.com/sharedfiles/filedetails/?id=2062943477) | 894k | Confirmed meltdown incidents by generator type if unique | Do not duplicate power-plant stock/uptime or generic incidents; success site unconfirmed | P2 conditional |
| A03 | [Vanilla Fishing Expanded](https://github.com/Vanilla-Expanded/VanillaFishingExpanded) | 812k | Successful fishing trips/catches by fish Def and amount | `LoadFolders.xml` selects `1.6Odyssey` / `1.6NotOdyssey` for 1.7; **Odyssey fishing already exists in host**. Prefer one collector/no duplicate | P0 conditional on gap |
| A04 | [Vanilla Cooking Expanded – Stews](https://steamcommunity.com/sharedfiles/filedetails/?id=2134312965) | 503k | Independently completed stew batch by output Def | Existing generic cooking/products likely cover all; only include if truly unique cooking lifecycle | P3 conditional |
| A05 | [Alpha Genes](https://github.com/juanosarg/AlphaGenes) | 298k | Completed mod-specific gene editing, addition/removal, xenotype transformations | 1.7 `loadFolders.xml` exists; identify exact successful gene operations vs vanilla Biotech and [Vanilla Genetics Expanded] | P1 |
| A06 | [Vanilla Psycasts Expanded – Puppeteer](https://github.com/Vanilla-Expanded/VanillaPsycastsExpanded-Puppeteer) | 288k | Successfully established puppets and distinct successful consciousness transfers | `Ability_Puppet.Cast` registers puppets/hediff; do not simply count cast attempts or host VPE ability cast stats; loaded assembly proof needed | P0 |
| A07 | [Vanilla Races Expanded – Lycanthrope](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Lycanthrope) | 261k | Committed transformations by resulting xenotype | Distinguish transformations from vanilla/other xenotype changes; source-defined transition not yet confirmed | P1 conditional |
| A08 | [Vanilla Races Expanded – Fungoid](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Fungoid) | 252k | Successful fungal infection-caused xenotype conversion by destination | 1.7 source/load folders found; confirm terminal transition and avoid normal infection/heddiff counters | P1 |
| A09 | [Vanilla Races Expanded – Insector](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Insector) | 228k | Confirmed caste/geneline evolution and reversals by type | Evolution Defs present; successful change callback, 1.7 selected binary unconfirmed | P1 conditional |

### B. Second round — seven candidates

| ID | Candidate / reference | Approx. subscribers discussed | Distinct potential statistics | Evidence / key gate | Priority |
|---|---|---:|---|---|---|
| B01 | [Vanilla Ideology Expanded – Dryads](https://github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Dryads) | 357k | Completed dryad merges/awakenings by spawned dryad kind | `CompPawnMerge` is an *initiation*, while `CompSpawnAwakened.DoSpawn` spawns result: count verified spawned result only | P0 |
| B02 | [Vanilla Races Expanded – Saurid](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Saurid) | 320k | Saurid egg births by child xenotype and count | `Comp_HumanHatcher.Hatch` checks `PawnUtility.TrySpawnHatchedOrBornPawn`; classify general births, **not another total birth increment** | P0 |
| B03 | [Vanilla Races Expanded – Phytokin](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Phytokin) | 313k | Successful sapling child births by resulting xenotype | `Building_SaplingChild.Hatch`: `successfulBirth` set in `finally` **even on failed spawn**; observe true spawn outcome, no double total births | P0 |
| B04 | [Alpha Animals](https://github.com/juanosarg/AlphaAnimals) | 427k | Completed larvae→cocoon transformations and other *proven* special lifecycle changes | 1.7 `CompLarvaeToCocoon`/ `CompBlackSwarmlingToCocoon`; distinguish ordinary births, VFE Insectoids 2 and duplicates | P1 |
| B05 | [Vanilla Races Expanded – Sanguophage](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Sanguophage) | 354k | Actual hemogen network transfers from draincaskets | `CompDraincasket.CompTickInterval` distributes among storage; actual accepted delta vs configured amount must be proven; 1.7 loads 1.6 | P1 |
| B06 | [Vanilla Landmarks Expanded](https://github.com/Vanilla-Expanded/VanillaLandmarksExpanded) | 203k | Distinct landmark first visits and map/colony landform subtype | 1.7 loads 1.6. Must identify uniquely visited map/site rather than count world creation; coordinate landform classification with D02 | P1 conditional |
| B07 | [Vanilla Helixien Gas Expanded](https://github.com/Vanilla-Expanded/VanillaHelixienGasExpanded) | 273k | Actual extracted and consumed Helixien Gas | `HelixienGasHandler` only proves world deposits, **not extraction**. Find committed flow quantities; do not infer from deposit/capacity | P2 conditional |

### C. Third round — six candidates

| ID | Candidate / reference | Approx. subscribers discussed | Distinct potential statistics | Evidence / key gate | Priority |
|---|---|---:|---|---|---|
| C01 | [Vanilla Events Expanded](https://github.com/Vanilla-Expanded/VanillaEventsExpanded) | 835k | Completed multi-stage event chains by event/stage, unique duration | 1.7 `GameCondition_MultiStage.EnterStage/End` found; avoid generic incident counts, ensure true completed (not canceled) condition | P0 |
| C02 | [Vanilla Furniture Expanded – Spacer Module](https://github.com/Vanilla-Expanded/VanillaFurnitureExpanded-Spacer) | 726k | Automated repair applied HP delta and full-repair events by item Def | `CompRepairStored.TickInterval` modifies HP, original inspected file in 1.6; no per-item tick scan in host; check actual 1.7 DLL | P1 |
| C03 | [Vanilla Races Expanded – Highmate](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Highmate) | 351k | Unique mod initiated bonded-lovin or mod-mediated pregnancy outcomes | `JobDriver_InitiateLovin` has initiation/end logic; generic bonds, lovin and pregnancy already tracked elsewhere, so only independent proven outcome | P2 conditional |
| C04 | [Hospital](https://github.com/tomvd/Hospital) | 141k | Registered patients by type, distinct cured departures, patient death/surgery failure, actual billed silver collected | `HospitalMapComponent.PatientArrived/PatientLeaves/PatientDied/SurgeryFailed` and `Trigger_SentAway` confirmed; beware dismissal/cleanup vs cured, and attempted `GenPlace.TryPlaceThing` vs real payment | P0 |
| C05 | [Mechanoid Upgrades](https://steamcommunity.com/sharedfiles/filedetails/?id=3365118555) | 95k | Completed mech upgrade installation/removal by upgrade and tier | Source/outcome site not yet audited; distinguish generic Biotech and vehicle upgrades | P1 conditional |
| C06 | [The Dead Man's Switch II](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478) | 54k | Confirmed distinct tech/campaign milestones and recovered unique machines | 1.7 successor to better-known prior series, but source/committed milestones not verified; quest/research overlap risk | P2 conditional |

### D. Fourth round — five popularity-led discoveries/re-evaluations

| ID | Candidate / reference | Approx. subscribers discussed | Distinct potential statistics | Evidence / key gate | Priority |
|---|---|---:|---|---|---|
| D01 | [Humanoid Alien Races](https://github.com/erdelf/AlienRaces) | 1,125k | Cross-content race-wise recruitment/deaths, historical max roster, prisoner/slave category by `ThingDef_AlienRace` | High-reach **classification layer**, not another count; integrate into existing pawn life-event collectors and maintain correct Def identity | P0 |
| D02 | [Geological Landforms](https://github.com/m00nl1ght-dev/GeologicalLandforms) | 695k | Colony count, longest survival, total time, peak wealth/roster by landform `Id` | `Landform.Id`/tile mutator available, but only count *actual founded/played colonies*, not random map previews/site generation; cross-mod B06 | P0 |
| D03 | [Vanilla Ideology Expanded – Memes and Structures](https://github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Memes) | 603k | Actual trade caravans summoned by Trading Fair; committed ideology conversion by unique ritual; other verified ritual effects | Previously excluded as generic rituals; re-evaluated after reading 1.7 `RitualOutcomeEffectWorker_TradingFair/LeaderConversion`; count actual effects, not nominal requested/vanilla ritual totals | P1 |
| D04 | [Vanilla Traits Expanded](https://github.com/Vanilla-Expanded/VanillaTraitsExpanded) | 658k | Unique Coward forced escapes and confirmed Big Boned-induced furniture damage, etc. | Previously excluded; `TraitsManager.TryForceFleeCowards/TryBreakChairsUnderBigBoneds` inspected in 1.7. Only meaningful post-action outcomes (not every scheduled check), avoid vanilla panic/damage duplication | P2 |
| D05 | [Vanilla Weapons Expanded – Non-Lethal](https://github.com/Vanilla-Expanded/VanillaWeaponsExpanded-NonLethal) | 561k | Successful special stuns by weapon/damage Def, distinct incapacitations where causality is provable | `DamageWorker_ConditionalStun` in 1.6 source; no counting hits as incapacitations, minimize combat hot-path overhead, CE compatibility audit | P2 conditional |

## Implementation planning / evidence requirements

1. **First audit:** D01 (shared race classification), D02 (per-colony geological classification), C01 (confirmed event chain), C04 (patient lifecycle); A06, B01–B03, A01/C02 follow when loaded 1.7 methods match source.
2. **Stable identities:** for mod and race use packageId + Def identity; for landforms use stable landform Id plus source; for patient, events and cocoon/sapling use colony/lineage and unique per-game occurrence IDs. Never sum the same event across checkpoint branches.
3. **Correct semantics:** distinguish category breakdowns from new totals (births/deaths/lovin/quests), cumulative HP/hemogen transfer from snapshots, and committed outcome from attempts. Unknown after disabling/removing a mod is not zero.
4. **Proof before code:** active `loadFolders.xml` and actually selected 1.7 assembly, source/IL success-path verification, generic-stats overlap assessment, low-cost hook, tests and packaging. Skippable metrics should be explicitly waived if not demonstrably collectible.
5. **Next research track:** *popular additional race/xenotype MODs*; prioritize mature 1.7 active Workshop subscription counts, independently completed race mechanics, and a shared HAR/Biotech species classification foundation. Record findings separately; do not append unverified race names to the supported list.

References: [original planned scope](README.md) · [third-wave register](2026-11-13-thirdwave-candidate-register.md) · [non-VE audit](2026-11-13-non-ve-discovery.md) · [fourth-wave distinct genres](2026-11-13-fourthwave-distinct-genres.md) · [fourth-wave follow-on](2026-11-13-fourthwave-follow-on-research.md).
