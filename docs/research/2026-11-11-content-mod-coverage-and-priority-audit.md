# Content-added MOD coverage audit — generic-first (2026-11-11)

**Status:** Source-backed analysis and implementation candidates only. No new acquisition hooks or verified numeric claims. Host inspected at `9fc8b8ad5edbff1122ddddfb08846a2815f1508d` (v1.5.0), RimWorld 1.7. This supersedes the simplistic assumption that each popular content MOD requires its own statistics addon.

## Decision / acceptance rules

1. **No separate statistics addon MOD or repository.** Implement only in `pengin0503.rimworldstatistics`, using the existing family acquisition gates, SDK/descriptor/persistence and one host package.
2. Reuse the **real generic gameplay success path** (bill completion, crop harvest, actual trade, mining, etc.) when the target MOD only adds ThingDefs/recipes. Do not create aliases `mod.<owner>` for statistics already represented by generic Def breakdowns.
3. Only a **mechanically new successful event** gets a new collector. Prefer **one framework-wide collector** (e.g., vehicles/outposts/fishing), not one collector per content pack sharing the framework.
4. Lifetime `Event` = confirmed post-success observation **since opt-in**. `SaveSnapshot` = observed save-time value. Never derive consumed/produced/transported **lifetime** totals from changing stocks or nominal capacities.
5. All providers fail closed on missing/unsupported targets and source version/shape changes. Removed gameplay MODs must not delete historical profile metrics; gaps remain Partial/Unknown. Never synthesize historical events.
6. Cross-colony DefName comparisons across heterogeneous MOD sets are **Partial**, not fully comparable. DefName alone is not identity proof when different MODs can reuse it.
7. Only release an experimental implementation after red/green unit tests, public source or IL contract check, production ZIP inspection, and a clear statement that real-game TPS/numeric parity was not tested.

## Verified *existing host* capture points and limitations

| Existing metric family / code | Generic event evidence | What it covers | Limitation |
|---|---|---|---|
| `work.crafting.completed_by_def` / `WorkDetailsFamily` | `RecordsUtility.Notify_BillDone(Pawn, products)` after a recipe completes | MOD-created products from **ordinary bills**, using each product's `ThingDef.defName` | Not autonomous factories, fermentation, ground-resource pumping, off-map world production, or special recipes that bypass the bill callback |
| `work.harvest.by_crop` | `QuestManager.Notify_PlantHarvested` on player's crop job | MOD-added **normally harvested crops**, grouped by the **source Plant Def**, not every output item | Does not mean fish, animal products, or arbitrary custom harvest methods are captured |
| `trade.sales.units` / `trade.buys.units` | `TradeDeal.TryExecute` with `Tradeable.ThingDef.defName` | MOD-added items traded by **ordinary vanilla trade deals** | Doesn't capture banking, outpost settlements, exchange contracts, alternative shops, or stock trading |
| `work.mining.units_by_def` | `Mineable.TrySpawnYield` + `GenPlace.TryPlaceThing` confirmed placement | MOD-added ores mined by the vanilla miner | Doesn't capture refineries, drills with independent item generation, or pump output |
| `royalty.psycasts` | `Psycast.Activate` with `Ability.lastCastTick` transition | Base Royalty casts that follow **that exact ability activation path** | VPE implements `AbilityExtension_Psycast.Cast(...)`; do **not** assume it follows this path without an IL/call-graph audit |
| `DisasterFamily.Incident` | Generic `IncidentWorker.TryExecute` postfix | **Only** `CropBlight`, `MeteoriteImpact`, `ShortCircuit` are entered in the current family | **Important correction:** generic incident hook does **not** currently count every Vanilla Events Expanded incident; its code explicitly filters these three DefNames |
| `CrossColonyAggregator` | Per-Def representative checkpoint breakdowns | Keeps captured quantities across independent colonies | Marks heterogeneous `ModSetHash` cohorts **Partial** because Def identity across MOD versions/sets is not proven |

Relevant host sources:
[Bill/harvest/mining capture](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs),
[Trade capture](../../Source/RimworldStatistics/Patches/Metrics/TradeMetricPatches.cs),
[Work breakdown](../../Source/RimworldStatistics.Core/Stats/Families/WorkDetailsFamily.cs),
[Disaster filter](../../Source/RimworldStatistics.Core/Stats/Families/DisasterFamily.cs),
[Royalty cast hook](../../Source/RimworldStatistics/Patches/Metrics/RoyaltyActivationMetricPatches.cs),
[Cross-MOD comparison safeguard](../../Source/RimworldStatistics.Core/Stats/CrossColonyAggregator.cs).

## Reclassified popular content MODs

Figures are **approximate Steam Workshop current-subscriber counts from the 2026-11-11 earlier 104-row survey**, **not** a fresh complete Steam ranking and **not** evidence of installed users. Source coverage is based on the indicated public GitHub code/metadata, not actual in-game detection.

| MOD / framework | Approx subscribers | New collector? | Audited finding and implementation target |
|---|---:|---|---|
| Vanilla Furniture Expanded (core) | 1,114,810 | **No, generically** | Ordinary building/item defs are not reason to duplicate production/stock; bespoke furniture behaviors need separate proof |
| Vanilla Weapons Expanded (core) | 1,006,160 | **No, generically** | Weapon manufacture and trade via generic ThingDef-based family; special ammunition or modes require independent proof |
| Vanilla Furniture Expanded — Farming | 913,608 | **No, generically** | Ordinary plant harvest and building records; don't invent per-machine usage counters |
| Vanilla Armour Expanded | 852,845 | **No, generically** | Manufactured apparel and trade; success of special apparel effects is a distinct system, not proven |
| Vanilla Plants Expanded | 834,855 | **No, generically** | Normally grown crops use the generic plant-def harvest recorder |
| **Vanilla Events Expanded** | **834,293** | **Yes, generic incident extension first** | 1.7 `IncidentWorker` subclasses include EarthQuake and others, but host's generic incident tracker intentionally accepts only three vanilla DefNames. Add scoped, bounded **per-IncidentDef successful event counts** that also work for other event MODs |
| **Vanilla Fishing Expanded** | **802,078** | **Maybe generic fishing collector** | Current public branch's `1.6Odyssey` code patches **base `JobDriver_Fish.CompleteFishingToil`** and catch selection; the existing host lacks a fish-success family. Audit 1.7 Odyssey catch outcome *once*, not a VFE-only new hook; count native fish and add-on catches without overlap |
| Vanilla Apparel Expanded | 822,750 | **No, generically** | Ordinary tailoring bills/trade; no separate standard apparel metrics |
| Vanilla Cooking Expanded | 743,346 | **No for ordinary recipes** | `Notify_BillDone` captures produced food; custom aging/stew mechanics must be audited separately |
| **Vanilla Vehicles Expanded** | **612,247** | **Yes, framework-level** | Depends on **SmashPhil.VehicleFramework**. Derive confirmed vehicle departures/arrivals or completed trips from shared framework, not duplicate VVE and Tier 3 trackers. Do not count cells/ticks via SDK events |
| **Vanilla Outposts Expanded** | **605,915** | **Yes, framework-level** | VEF `Outpost.Tick` periodically calls `Produce` → `Deliver(ProducedThings())`. Off-map production bypasses normal bill/harvest metrics. Distinguish *generated* vs *successfully delivered* and avoid **double-enumerating `IEnumerable<Thing>`**, which may re-run random generation |
| **Vanilla Psycasts Expanded** | **596,505** | **Yes, after cast-outcome audit** | 1.7 `AbilityExtension_Psycast.Cast` invokes `Hediff_PsycastAbilities.UseAbility`. Base Royalty only hooks `Psycast.Activate`. Need proof of committed cast and no duplicate before accepting counts/level-ups |
| **Vanilla Brewing Expanded** | **593,969** | **Conditional** | Regular bills captured; fermentation completed through a separate comp requires source 1.7 proof and product-success semantics |
| Vanilla Social Interactions Expanded | 583,340 | **Conditional** | Existing `SocialFamily` covers some vanilla social callbacks; inspect each unique social event before a custom family |
| Vanilla Books Expanded | 557,164 | **Conditional** | Ordinary authored items may come from bill; special completion/read histories need source proof |
| Vanilla Vehicles Expanded — Tier 3 | 423,185 | **No separate collector** | Same shared Vehicle Framework family as core VVE |
| Vanilla Factions Expanded — Empire | 410,833 | **Conditional** | Vanilla raids, titles, trade and research already recorded; only extra faction missions with their own confirmed outcome need events |
| **Vanilla Trading Expanded** | **410,035** | **Yes, independent financial operations** | Source `Bank.DepositSilver`, `WithdrawSilver`, `TakeLoan` and contracts/stock management are not `TradeDeal.TryExecute` transactions |
| **Vanilla Genetics Expanded** | **409,542** | **Yes, unique experiment lifecycle** | 1.7 `JobDriver_ArchotechDNAExtraction`, `JobDriver_InsertGrowthCell`, `JobDriver_ParagonConversion` are custom jobs; count actual success, not start |
| **Dubs Rimatomics** | **401,040** | **Yes, after binary contract** | About advertises RimWorld 1.7, v`1.7.3986`; public repo includes `1.7/Assemblies/Rimatomics.dll` but no corresponding C# sources. Audit DLL read-only before designing reactor production/consumption |
| **Rimefeller** | **362,182** | **Yes, after binary contract** | About advertises RimWorld 1.7, v`1.2.2086`; binary `1.7/Assemblies/Rimefeller.dll`. Distinguish **actual** oil extraction/fuel processing from stored tank amounts and nominal production |
| Save Our Ship 2 | 213,337 | **Yes, source re-audit** | Independent spaceship/world combat system; capture confirmed launches/ship battles, not ordinary caravan/pod flights |
| A RimWorld of Magic | 207,589 | **Yes, source re-audit** | Unique skill/cast events; avoid generic combat or Royalty psycast duplicates |
| Giddy-Up 2 - Continued | 130,689 | **Conditional** | Mounted distance/movement unique if confirmed; vanilla caravan and animal statistics are not a ride ledger |
| SRTS Expanded | 101,368 | **Yes, separate travel engine** | Distinct from shared Vehicle Framework until source proves unified events |
| Gastronomy (Continued) | 90,438 | **Yes, after sale completion** | Guest orders/settlements have different semantics from normal trading and Hospitality guest arrivals |

**Rows are not an acceptance declaration:** "Yes" means a new acquisition source may be justified, not that it has been implemented, validated or assigned a final metric ID. Extra content may be covered by another generic family even when not named here.

## Read-only upstream evidence (specific revisions)

- [Vanilla Expanded Framework — `Outpost.Produce` / `Outpost.Deliver`](https://github.com/Vanilla-Expanded/VanillaExpandedFramework/tree/e7fab7dfb352/Source/Outposts/Outpost) — commit `e7fab7dfb352`; `ProducedThings` is enumerated in the delivery path; output semantics depend on delivery method.
- [Vanilla Outposts Expanded](https://github.com/Vanilla-Expanded/VanillaOutpostsExpanded/tree/2b4d68693987) — `About.xml` advertises 1.7 but visible C# source/assemblies are in the `1.6` directory; **runtime ABI not verified for 1.7**.
- [Vanilla Fishing Expanded — Odyssey fishing postfix](https://github.com/Vanilla-Expanded/VanillaFishingExpanded/blob/101be9fccc98/1.6Odyssey/Source/VCE-Fishing/VCE-Fishing/Harmony/JobDriver_Fish_CompleteFishingToil.cs) — repo declares 1.7, but visible source binary layout is in 1.6Odyssey; **1.7 catch-flow must be verified**.
- [Vanilla Events Expanded — EarthQuake](https://github.com/Vanilla-Expanded/VanillaEventsExpanded/blob/9b20c84da823/1.7/Source/VEE/IncidentWorker/EarthQuake.cs) — successful custom `IncidentWorker.TryExecuteWorker`.
- [Vanilla Psycasts Expanded — AbilityExtension_Psycast](https://github.com/Vanilla-Expanded/VanillaPsycastsExpanded/blob/379b3235a4e9/1.7/Source/VanillaPsycastsExpanded/AbilityExtension_Psycast.cs) — real 1.7 source.
- [Vehicle Framework — VehiclePathFollower](https://github.com/SmashPhil/Vehicle-Framework/blob/5bd3ca478a07/Source/Vehicles/Components/Vehicles/VehiclePathFollower.cs), [About.xml](https://github.com/SmashPhil/Vehicle-Framework/blob/5bd3ca478a07/About/About.xml) — v1.7.2503 rev7834; shared vehicle movement engine. **Its About.xml declares `vanillaexpanded.outposts` incompatible**; verify version-specific reason before supporting combined installations.
- [Vanilla Trading Expanded — Bank](https://github.com/Vanilla-Expanded/VanillaTradingExpanded/blob/85524a235b4f/1.5/Source/VanillaTradingExpanded/Banks/Bank.cs) — banking source (repo advertises 1.7, later assembly exists, ABI not audited).
- [Vanilla Genetics Expanded](https://github.com/Vanilla-Expanded/VanillaGeneticsExpanded/tree/1837722d1ac7/1.7/Source/GeneticRim/GeneticRim/AI/JobDrivers) — custom experiment jobs.
- [Rimatomics](https://github.com/Dubwise56/Rimatomics/tree/de328ca971de/1.7/Assemblies) and [Rimefeller](https://github.com/Dubwise56/Rimefeller/tree/ab45267f1dd8/1.7/Assemblies) — exact public binaries, read-only metadata/IL needed, no target DLL redistribution.

## No-go / acceptance guards

- Do not infer "production completed" from `Tick` counters, potential production capacity or a stock snapshot.
- Do not count any event based on a pre-success method entry, an interrupted job, or merely a new `Thing` allocation.
- Do not enumerate an effect-producing `IEnumerable<Thing>` more than once merely to measure it.
- Do not assume a `1.6` directory or an About 1.7 declaration proves its C# signature is identical in actual 1.7.
- Do not register identical "separate add-on" metric IDs for the same vanilla work/TradeDef observations; keep existing cross-colony numbers intact.
- Do not add always-on per-tick reflection or SDK events; measure at successful low-frequency transactions or save.
- Existing historical profile metrics survive target removal; generic product keys remain available but can become unverifiable under different MOD inventories.

## Recommended next work

**P0 — highest-value source and feasibility audits:**
1. Confirm 1.7 fishing success/catch outcome in Odyssey. Prefer a generic `fishing` family that also captures VFE catch Defs.
2. Confirm 1.7 successful IncidentDef execution and player-home scope. Consider a generic per-incident type event breakdown without changing the existing hardcoded disaster counters.
3. Confirm shared VEF Outpost production/delivery commitments; if a durable success point is not available, use **save-time per-colony outpost counts** only, not fabricated lifetime resource production.

**P1 — independent gameplay systems:** shared Vehicle Framework trip completions; VPE successful ability casts/level-up; VTE banking; genetics completion. **P2:** binary-only Rimatomics/Rimefeller; ship combat; special social/book systems.

Keep all new metric series **Experimental / opt-in** until target-mod real-game acceptance. Release version and Workshop upload are separate user decisions.
