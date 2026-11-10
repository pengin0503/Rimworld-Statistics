# Cross-colony statistics scope policy and existing-catalog audit (2026-11-10)

**Status: agreed selection policy and non-destructive research. No code, metric definition, profile, save, or release changes.**
**Baseline:** `pengin0503/Rimworld-Statistics` main `93cd4aa6e801e79c9b09605cf632bb15faa15885`.

## 1. Binding product scope for future statistics proposals

Rimworld-Statistics' core value is **records across all independent colonies and past saves**, not an alternative HUD for the currently loaded colony or a one-save state viewer. **Do not add new 'latest/current/at-save' metrics merely because their numbers are accessible from a single .rws**, even if a technical Sum can combine the representative snapshots of several colonies. The same restriction applies to a newly proposed scalar called 'current' or 'active' rather than 'latest'. The prior proposal to add latest overall wealth, latest free colonists/prisoners, latest average mood, latest storyteller threat points and similar present-state readings is **rejected**.

Before accepting a new output, state (a) which distinct colony records must be examined, (b) exactly how one colony's duplicate/forked saves are deduplicated, (c) which cross-colony operation produces value beyond a single save, and (d) what information exists before the MOD was installed. Prioritize:

1. **Cross-colony lifetime totals:** completed-event counts, accumulated quantities/amounts, play time and confirmed endings, using at most one valid lineage representative per colony.
2. **Cross-colony historical records:** maximum/minimum, longest streak, earliest valid achievement, or holder of a record, using comparable evidence from independent colonies. These are *records*, not lifetime sums, and belong in secondary/ranking UI unless separately prioritized.
3. **Cross-colony comparisons/distributions:** meaningful counts/rates grouped by scenario, difficulty, starting biome, event kind, etc., with explicit denominator and partial-evidence treatment.

Exclude from new work by default: the last snapshot's inventory, headcount, research state, goodwill, currently active quests, MOD count, story-state adaptation, colony-specific first/last reading, or single-colony configuration. A historical peak or accumulated quantity remains eligible when it actually compares/combines independent colonies; a metric is not disqualified merely because its winning record occurred in one colony.

Source data availability alone is **not** a reason to create a new metric. Avoid duplicate 'top' aliases when an existing lifetime total plus breakdown already carries the same information. Keep historical ambiguity as Partial/Unknown/NotApplicable, never fabricate missing events or add sibling saves. Do not automatically classify `OverallAggregation.Sum` as lifetime: snapshot sums are not event accumulation.

**Grandfathering:** Existing 331 definitions and the v1 Overview remain intact for backwards compatibility. Classification here is *not* authorization to delete data, hide existing rows, change `MetricKeys` or `ProfileJson`, disable acquisition, or modify published releases. A future pruning or UI redesign needs a separate explicit decision and migration/visibility tests.

## 2. What was audited

Current static catalog: `MetricRegistry.Shipped` **57**, `LiveCatalog.Definitions` **224** (includes P1/P2 additions), `SnapshotCatalog.Definitions` **41**, `RankingDependencyCatalog.Definitions` **9** = **331 output definitions**. This is a source-code count, not 331 measured/verified values; third-party provider outputs are dynamic and not included.

`MetricCalculator.BuildView` intentionally omits `OverallAggregation.IndividualOnly` definitions from `NumericView.Overall`; they are individual-colony details and not global totals. Other `Sum` rows combine one reading per independent colony but may still be mere snapshots. `MetricDisplayPolicy.IsLifetimeCumulative` uses an explicit whitelist and must remain semantically separate from this future-feature selection policy.

## 3. Existing entries with a last/at-save meaning: preserve; do not add similar ones

| Existing output key | What it means | Scope decision |
|---|---|---|
| `wealth.items.latest` | history wealth items; representative recorded sample | Existing only; no new latest equivalent |
| `wealth.buildings.latest` | history wealth buildings; representative recorded sample | Existing only; no new latest equivalent |
| `wealth.pawns.latest` | history wealth pawns; representative recorded sample | Existing only; no new latest equivalent |
| `colony.mods.latest` | latest MOD count; individual colony only | Existing only; no new latest equivalent |
| `stock.food_nutrition.latest` | food stock at successful save | Existing only; no new latest equivalent |
| `stock.silver.latest` | silver stock at successful save | Existing only; no new latest equivalent |
| `stock.materials.latest` | materials stock at successful save | Existing only; no new latest equivalent |
| `facilities.running_generators.latest` | generator types running at successful save | Existing only; no new latest equivalent |

Additionally, `StatsResult.LastWealthSum` / `OverviewTab` presents **the sum of each colony's last representative wealth** as an existing Overview figure. Although computed across colonies, it is not accumulated wealth earned across the campaigns: it adds stocks from different in-game dates. **Flag for later product/UI review, not removal in this change**. `ColonyStats.WealthLast` is its per-colony input.

## 4. Existing 12 IndividualOnly outputs — no all-colony total

| Existing output key | Source meaning / review direction |
|---|---|
| `wealth.total.first` | initial wealth of a colony |
| `wealth.total.peak_day` | day of peak wealth within colony |
| `colony.mods.latest` | MOD count at representative save (also A) |
| `colony.mods.peak` | max MOD count among observed saves |
| `colony.mods.min` | min MOD count among observed saves |
| `research.live.completed_ticks` | duration by project, individual only |
| `work.harvest.top_crop` | top crop for individual colony |
| `trade.sales.top_item` | most sold item for individual colony |
| `trade.buys.top_item` | most bought item for individual colony |
| `colony.start.biome` | starting biome; potentially valid input to cross-colony rankings |
| `diplomacy.snapshot.goodwill_range` | goodwill min/max at one retained state |
| `population.snapshot.passion` | skill/passion distribution at one retained state |

These **12 are not 12 deletion recommendations**: `colony.start.biome` is a useful input to cross-colony biome rankings; the research/harvest/trade breakdowns may be useful as drill-down evidence. `colony.mods.latest` appears in both sections, so candidate totals must not add the tables together.

## 5. Snapshot aggregate trap: combining different colony states is not lifetime accumulation

Of the 41 snapshot definitions, **11 have `OverallAggregation.Sum`**. Their numbers can appear in the cross-colony Overall view, but the sum may represent independent, asynchronous selected *states*, not cumulative successes. Audit these before any promotion to the main lifetime view:

| Existing output key | Why Sum is not automatically a lifetime total |
|---|---|
| `research.snapshot.finished` | completed research count at selected save, not completed research events |
| `diplomacy.snapshot.relations` | remaining supported current faction relations |
| `population.snapshot.free` | free colonists at selected save |
| `population.snapshot.prisoners` | prisoners at selected save |
| `population.snapshot.animals` | player animals at selected save |
| `population.snapshot.mechs` | player mechs at selected save |
| `facilities.running_generators.latest` | running generators at selected save (also A) |
| `stock.food_nutrition.latest` | food stock at selected save (also A) |
| `stock.silver.latest` | silver stock at selected save (also A) |
| `stock.materials.latest` | materials stock at selected save (also A) |
| `royalty.quest_success` | successes among *retained* Empire quests; not complete historical event ledger |

The snapshot-derived ratios `research.snapshot.finished_rate` and `population.snapshot.age_mean` also depend on a representative recorded state. Their cross-colony ratios may be informative, but they are not lifetime totals. Similarly, `population.snapshot.augmented_peak`, `facilities.medical_beds.peak`, `stock.weapons.peak`, `biotech.pollution.cells_peak`, etc. are historical *observed extrema*, **not confirmed all-time maxima** outside the save observation coverage. They can remain cross-colony record candidates in secondary UI; do not relabel their semantics.

Other existing `Ratio` and `Max/Min` rows such as average mood, deaths/raids per year, surgery success rate, and wealth change windows are not lifetime accumulations. Their cross-colony comparability is a separate product choice. The current Lifetime / Other / All presentation separation is already appropriate; do not equate 'not lifetime' with 'must delete'.

## 6. Reassess previously proposed save-only additions

| Previous proposal | Decision |
|---|---|
| latest total wealth, free colonists, prisoners, average mood | **Do not add**: one-save/final-sample state |
| latest `ThreatPoints`; latest `Adaptation`; latest `PopIntent` | **Do not add**: storyteller state of an observation, not cross-colony career record |
| currently accepted/open quests, current world-site/object count | **Do not add** merely as the saved current-state count; cannot infer all historical quests or sites |
| threat-point **all-colony historical maximum** or highest relevant milestone | **Potentially relevant**: investigate saved recorder series, semantics, version/cadence and how a cross-colony maximum would be selected |
| retained-quest counts by type / observed maxima | **Conditional**: only if the aggregate adds a genuine cross-colony comparison, not another current-state display; surviving quests are not the complete history |
| historic accumulated counts derivable from verified direct-parent checkpoints | **Conditional**: prove lineage, identical version/MOD conditions, nondecreasing counters, no branch or sibling double-counting; never substitute a difference for a fully recorded event ledger |

## 7. Acceptance tests for any later implementation

- Present a two-independent-colony fixture and a same-colony three-direct-parent-save fixture (`Test1.7_1109` evidence); the metric must not be a mere restatement of the last saved snapshot or triple-count a colony.
- For accumulated events, prove the exact observation period and maintain coverage status when enabled mid-game or after save/MOD/DLC changes.
- For historical maxima/record holders, compare all applicable distinct colonies and label observed vs genuinely all-time.
- For cross-colony ratios, use the correct numerator/denominator; do not average percentages without weight.
- Preserve the 331 existing definitions, profile serialization, detail visibility, SDK and versioned save compatibility unless explicitly authorized otherwise.
- Keep `AcquisitionStatus.Unverified` and default-off acquisition gates intact; passing a static reader test does not prove game-side capture.

## 8. Source evidence and follow-up

- `Source/RimworldStatistics.Core/Stats/{MetricRegistry,LiveCatalog,SnapshotCatalog,RankingDependencyCatalog,MetricDisplayPolicy,MetricCalculator}.cs`
- `Source/RimworldStatistics.Core/Stats/StatsCalculator.cs` and `Source/RimworldStatistics/UI/OverviewTab.cs` (existing last-wealth sum)
- `Source/RimworldStatistics.Core/Stats/{ResearchStateSnapshot,RosterSnapshot,FacilitiesSnapshot,StockSnapshot,RoyaltyQuestSnapshot}.cs`
- Existing design `docs/superpowers/specs/2026-11-10-lifetime-cumulative-statistics-design.md`; research Issue #82.

**No gameplay/TPS claim:** this is a definition/aggregation audit at the above commit. Do not expand the scope of current v1.4.0 release acceptance, or imply new values have already been verified in the game.
