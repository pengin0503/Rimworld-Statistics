# Cross-colony statistics scope policy and existing-catalog audit (2026-11-10)

**Status: agreed selection policy and non-destructive research. No code, metric definition, profile, save, or release changes.**
**Baseline:** `pengin0503/Rimworld-Statistics` main `93cd4aa6e801e79c9b09605cf632bb15faa15885`.

## 1. Binding product scope for future statistics proposals

Rimworld-Statistics' core value is **records across all independent colonies and past saves**, not an alternative HUD for a single loaded colony. **Do not add standalone latest/current/at-save readouts merely because they are easy to obtain from one .rws.** However, the user's subsequent decision explicitly **accepts meaningful cross-colony aggregates of representative saved states**, including sums across independent colonies, colonies holding the highest/lowest values, and comparisons/distributions. Such values are **cross-colony snapshots, not whole-career event accumulation**. The earlier proposal to add *individual-colony* latest wealth/free colonists/prisoners/mood/threat as separate rows remains rejected; adding **cross-colony sums or extrema using the same sources is allowed**. See the [superseding candidate policy](2026-11-10-cross-colony-aggregation-candidates.md).

Before accepting a new output, state (a) which distinct colony records must be examined, (b) exactly how one colony's duplicate/forked saves are deduplicated, (c) which cross-colony operation produces value beyond a single save, and (d) what information exists before the MOD was installed. Prioritize:

1. **Cross-colony lifetime totals:** completed-event counts, accumulated quantities/amounts, play time and confirmed endings, using at most one valid lineage representative per colony.
2. **Cross-colony snapshot aggregates:** the sum of each independent colony's representative population, animals/mechs, inventory, facilities or comparable state. This is an asynchronous portfolio of saved states, **not all-time acquired quantities or simultaneous inventory**.
3. **Cross-colony record holders and historical extrema:** the highest/lowest colony, highest/lowest observed values, longest streak, earliest valid achievement, and record holders, using evidence from independent colonies.
4. **Cross-colony type rankings, comparisons and distributions:** top harvested crop/top traded item from **sums of per-Def counts across colonies**, or grouped counts/rates by scenario, difficulty, biome and event kind.

Exclude *new standalone one-colony latest/current readouts* by default. The **same underlying snapshots** (inventory, headcount, research, goodwill, active quests, MODs, facilities, DLC state, storyteller values) are **eligible as cross-colony sums, record holders or extrema**, subject to utility, comparability and source coverage. A top result may be held by just one colony, but determining that result must compare the eligible independent colonies.

Source data availability alone is **not** a reason to create a new metric. Avoid duplicate 'top' aliases when an existing lifetime total plus breakdown already carries the same information. Keep historical ambiguity as Partial/Unknown/NotApplicable, never fabricate missing events or add sibling saves. Do not automatically classify `OverallAggregation.Sum` as lifetime: snapshot sums are not event accumulation.

**Grandfathering:** Existing 331 definitions and the v1 Overview remain intact for backwards compatibility. Classification here is *not* authorization to delete data, hide existing rows, change `MetricKeys` or `ProfileJson`, disable acquisition, or modify published releases. A future pruning or UI redesign needs a separate explicit decision and migration/visibility tests.

## 2. What was audited

Current static catalog: `MetricRegistry.Shipped` **57**, `LiveCatalog.Definitions` **224** (includes P1/P2 additions), `SnapshotCatalog.Definitions` **41**, `RankingDependencyCatalog.Definitions` **9** = **331 output definitions**. This is a source-code count, not 331 measured/verified values; third-party provider outputs are dynamic and not included.

`MetricCalculator.BuildView` intentionally omits `OverallAggregation.IndividualOnly` definitions from `NumericView.Overall`; they are individual-colony details and not global totals. Other `Sum` rows combine one reading per independent colony but may still be mere snapshots. `MetricDisplayPolicy.IsLifetimeCumulative` uses an explicit whitelist and must remain semantically separate from this future-feature selection policy.

## 3. Existing last/at-save inputs: preserve; allow meaningful cross-colony aggregates and rankings

| Existing output key | What it means | Scope decision |
|---|---|---|
| `wealth.items.latest` | history wealth items; representative recorded sample | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `wealth.buildings.latest` | history wealth buildings; representative recorded sample | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `wealth.pawns.latest` | history wealth pawns; representative recorded sample | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `colony.mods.latest` | latest MOD count; individual colony only | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `stock.food_nutrition.latest` | food stock at successful save | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `stock.silver.latest` | silver stock at successful save | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `stock.materials.latest` | materials stock at successful save | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |
| `facilities.running_generators.latest` | generator types running at successful save | Existing source; cross-colony aggregates/record holders allowed; no standalone duplicate |

Additionally, `StatsResult.LastWealthSum` / `OverviewTab` presents **the sum of each colony's last representative wealth** as an existing Overview figure. Although computed across colonies, it is not accumulated wealth earned across the campaigns: it adds stocks from different in-game dates. **Keep as a valid cross-colony saved-state aggregate** (not a lifetime earned-wealth total); its label and placement can be reviewed later, without removal in this change. `ColonyStats.WealthLast` is its per-colony input.

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

These **12 are not 12 deletion recommendations**. **Correction:** `work.harvest.top_crop`, `trade.sales.top_item` and `trade.buys.top_item` are currently `IndividualOnly`, **but their source data do support all-colony rankings**. Existing `work.harvest.by_crop`, `trade.sales.units` and `trade.buys.units` are `Sum` with per-Def breakdowns. Sum the per-Def counts **across independent colonies first**, then choose the overall top item; never mistake a per-colony top for the overall top. `colony.start.biome` is useful for cross-colony biome rankings, and `colony.mods.latest` appears in both tables, so category counts cannot be added together.

## 5. Saved-state sums are valid cross-colony aggregates, but not lifetime event totals

Of the 41 snapshot definitions, **11 have `OverallAggregation.Sum`**. This is a **legitimate cross-colony aggregation already implemented in the code** when data are recorded. The sum represents independent colonies' representative *states at potentially different times*, not cumulative successful events and not simultaneous world-wide inventory. Do not drop or reject these because they are not lifetime event totals:

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

The snapshot-derived ratios `research.snapshot.finished_rate` and `population.snapshot.age_mean` depend on representative recorded states. **Cross-colony comparisons may be useful and eligible, subject to meaningful weighting**; they are not lifetime event totals. Similarly, `population.snapshot.augmented_peak`, `facilities.medical_beds.peak`, `stock.weapons.peak`, `biotech.pollution.cells_peak`, etc. are historical *observed extrema*, **not confirmed all-time maxima** outside the save observation coverage. They can remain cross-colony record candidates in secondary UI; do not relabel their semantics.

Other existing `Ratio` and `Max/Min` rows such as average mood, deaths/raids per year, surgery success rate, and wealth change windows are not lifetime accumulations. Their cross-colony comparability is a separate product choice. The current Lifetime / Other / All presentation separation is already appropriate; do not equate 'not lifetime' with 'must delete'.

## 6. Reassess previously proposed save-only additions

| Previous proposal | Decision |
|---|---|
| latest total wealth, free colonists, prisoners, average mood | **Do not add standalone one-save 'latest' rows. Do allow** their meaningful cross-colony sums, highest/lowest holders and observed records. |
| latest `ThreatPoints`; latest `Adaptation`; latest `PopIntent` | **Do not add standalone latest rows**; cross-colony comparable records may be considered only after semantics and historical-source audit. |
| currently accepted/open quests, current world-site/object count | **Do not add a standalone one-colony current count**; cross-colony sum/holders are eligible with clearly limited retained-snapshot semantics, never as lifetime events. |
| threat-point **all-colony historical maximum** or highest relevant milestone | **Potentially relevant**: investigate saved recorder series, semantics, version/cadence and how a cross-colony maximum would be selected |
| retained-quest counts by type / observed maxima | **Conditional**: only if the aggregate adds a genuine cross-colony comparison, not another current-state display; surviving quests are not the complete history |
| historic accumulated counts derivable from verified direct-parent checkpoints | **Conditional**: prove lineage, identical version/MOD conditions, nondecreasing counters, no branch or sibling double-counting; never substitute a difference for a fully recorded event ledger |

## 7. Acceptance tests for any later implementation

- Present a two-independent-colony fixture and a same-colony three-direct-parent-save fixture (`Test1.7_1109` evidence); the **new metric must actually aggregate/compare independent colonies**, and never triple-count the same colony. A representative save is legitimate as one input to a cross-colony snapshot total.
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


## 9. 後続の方針修正：スナップショットの横断合計・最多/最少は正式な候補

同日、ユーザーは「作物/売買品目の全体最多」および「自由入植者・動物・メカノイド・在庫・稼働発電機の**全コロニー合計、最多/最少コロニー**」を許容し、**同様の他項目も対象に含める**と明示した。したがって、本監査の旧来の「スナップショットの合計は新規採用しない」と読める記述は**撤回**し、上記の各節を修正した。

既存の全コロニー合計、個別コロニーの最多値、観測上の歴代最大、歴代発生イベント数は**別の意味**であり、ラベルとデータ出典を混同しない。より幅広い候補、既存実装/未実装の線引き、計算例、欠測・DLC・分岐条件は [全コロニー横断集計とランキング候補](2026-11-10-cross-colony-aggregation-candidates.md) に記録する。コードの実装や既存項目の削除は行わない。
